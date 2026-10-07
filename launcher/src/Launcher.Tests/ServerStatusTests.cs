using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Launcher.Helpers;

namespace Launcher.Tests;

/// <summary>
/// The Login server answers a status request only if the reply (6 bytes) is no larger than the request, since anyone
/// can forge the request's sender address (task 18, src/Sanctuary.UdpLibrary/UdpManager.cs TryAdmitUnverifiedReply).
/// </summary>
[TestClass]
public class ServerStatusTests
{
    private const int ReplySize = 6;

    [TestMethod]
    public void Request_is_padded_to_the_size_of_the_reply()
    {
        var request = ServerStatusHelper.CreateRequest();

        Assert.IsTrue(request.Length >= ReplySize, $"{request.Length} bytes");
        Assert.AreEqual(0x00, request[0]);
        Assert.AreEqual(32, request[1], "UdpPacketType.ServerStatus");
    }

    [TestMethod]
    public async Task Reads_the_reply_of_a_server_that_only_answers_requests_at_least_as_large()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;

        var statusTask = ServerStatusHelper.GetAsync($"127.0.0.1:{port}", timeout: 5000);

        var request = await server.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(request.Buffer.Length >= ReplySize, "the server would leave a smaller request unanswered");

        var reply = new byte[ReplySize];
        reply[0] = 1; // online
        reply[1] = 0; // not locked
        BinaryPrimitives.WriteInt32LittleEndian(reply.AsSpan(2), 7);

        await server.SendAsync(reply, request.RemoteEndPoint);

        var status = await statusTask;

        Assert.IsTrue(status.IsOnline);
        Assert.IsFalse(status.IsLocked);
        Assert.AreEqual(7, status.OnlinePlayers);
    }
}
