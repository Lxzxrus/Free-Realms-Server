using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sanctuary.WebAPI.Tests;

[TestClass]
public class StatusEndpointTests
{
    /// <summary>A Login server's status reply, from a UDP socket on loopback. Counts the requests it answers.</summary>
    private sealed class FakeLoginServer : IDisposable
    {
        private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource _stop = new();

        public int Requests;

        public FakeLoginServer(bool online, bool locked, int players)
        {
            var reply = new byte[6];
            reply[0] = online ? (byte)1 : (byte)0;
            reply[1] = locked ? (byte)1 : (byte)0;
            BitConverter.TryWriteBytes(reply.AsSpan(2), players);

            _ = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    try
                    {
                        var request = await _socket.ReceiveAsync(_stop.Token);
                        Interlocked.Increment(ref Requests);

                        // As the Login server: the status packet type, and never a reply larger than the request.
                        if (request.Buffer.Length >= reply.Length && request.Buffer[0] == 0 && request.Buffer[1] == 32)
                            await _socket.SendAsync(reply, request.RemoteEndPoint, _stop.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                    }
                }
            });
        }

        public string Address => $"127.0.0.1:{((IPEndPoint)_socket.Client.LocalEndPoint!).Port}";

        public void Dispose()
        {
            _stop.Cancel();
            _socket.Dispose();
        }
    }

    private static WebAPIHost Host(string loginServer) => new(new Dictionary<string, string?>
    {
        ["WebAPI:StatusLoginServer"] = loginServer,
        ["WebAPI:StatusTimeout"] = "00:00:00.500",
        ["WebAPI:RateLimits:StatusPerMinute"] = "1000",
    });

    private static async Task<JsonElement> GetStatusAsync(WebAPIHost host)
    {
        using var response = await host.CreateClient(WebAPIHost.DefaultAddress).GetAsync("/status.json");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [TestMethod]
    public async Task ReportsWhatTheLoginServerSays()
    {
        using var login = new FakeLoginServer(online: true, locked: false, players: 12);
        using var host = Host(login.Address);
        await host.StartAsync();

        var status = await GetStatusAsync(host);

        Assert.AreEqual("online", status.GetProperty("status").GetString());
        Assert.IsTrue(status.GetProperty("online").GetBoolean());
        Assert.AreEqual(12, status.GetProperty("players").GetInt32());
    }

    [TestMethod]
    public async Task LockedIsNotOnline()
    {
        using var login = new FakeLoginServer(online: true, locked: true, players: 0);
        using var host = Host(login.Address);
        await host.StartAsync();

        Assert.AreEqual("locked", (await GetStatusAsync(host)).GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task NoGatewayIsOffline()
    {
        using var login = new FakeLoginServer(online: false, locked: false, players: 0);
        using var host = Host(login.Address);
        await host.StartAsync();

        Assert.AreEqual("offline", (await GetStatusAsync(host)).GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task NoAnswerIsOffline()
    {
        // A port nothing listens on: a refused send or a timeout, both offline.
        using var unused = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var address = $"127.0.0.1:{((IPEndPoint)unused.Client.LocalEndPoint!).Port}";
        unused.Dispose();

        using var host = Host(address);
        await host.StartAsync();

        var status = await GetStatusAsync(host);

        Assert.AreEqual("offline", status.GetProperty("status").GetString());
        Assert.AreEqual(0, status.GetProperty("players").GetInt32());
    }

    [TestMethod]
    public async Task OneAnswerIsSharedSoThePageCantFloodLogin()
    {
        using var login = new FakeLoginServer(online: true, locked: false, players: 3);
        using var host = Host(login.Address);
        await host.StartAsync();

        for (var i = 0; i < 20; i++)
            await GetStatusAsync(host);

        Assert.AreEqual(1, login.Requests);

        host.Time.Advance(TimeSpan.FromSeconds(11));
        await GetStatusAsync(host);

        Assert.AreEqual(2, login.Requests);
    }

    [TestMethod]
    public async Task StatusPageIsServed()
    {
        using var login = new FakeLoginServer(online: true, locked: false, players: 0);
        using var host = Host(login.Address);
        await host.StartAsync();

        using var response = await host.CreateClient(WebAPIHost.DefaultAddress).GetAsync("/status");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "status.json");
        Assert.IsTrue(response.Headers.Contains("Content-Security-Policy"));
    }

    [TestMethod]
    public async Task StatusIsRateLimited()
    {
        using var login = new FakeLoginServer(online: true, locked: false, players: 0);
        using var host = new WebAPIHost(new Dictionary<string, string?>
        {
            ["WebAPI:StatusLoginServer"] = login.Address,
            ["WebAPI:RateLimits:StatusPerMinute"] = "3",
        });
        await host.StartAsync();

        var client = host.CreateClient(WebAPIHost.DefaultAddress);

        for (var i = 0; i < 3; i++)
            (await client.GetAsync("/status.json")).Dispose();

        using var limited = await client.GetAsync("/status.json");

        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
