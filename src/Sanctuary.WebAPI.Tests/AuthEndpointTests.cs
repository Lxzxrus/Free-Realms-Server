using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sanctuary.WebAPI.Tests;

[TestClass]
public class AuthEndpointTests
{
    private static Dictionary<string, string?> Lockout(int perAddress = 3, int perAccount = 1000) => new()
    {
        ["WebAPI:LoginLockout:FailuresPerAddress"] = perAddress.ToString(),
        ["WebAPI:LoginLockout:FailuresPerAccount"] = perAccount.ToString(),
        ["WebAPI:LoginLockout:BaseLockout"] = "00:01:00",
        ["WebAPI:LoginLockout:MaxLockout"] = "00:04:00",
        ["WebAPI:LoginLockout:AccountWindow"] = "00:15:00",
    };

    [TestMethod]
    public async Task LoginReturnsSessionAndLaunchArguments()
    {
        using var host = new WebAPIHost();
        await host.StartAsync();

        using var response = await host.LoginAsync("alice");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginBody>();

        Assert.IsNotNull(body);
        Assert.IsFalse(string.IsNullOrEmpty(body.SessionId));
        Assert.AreEqual("AssetDelivery:IndirectServerAddress=http://assets.example", body.LaunchArguments);
    }

    [TestMethod]
    public async Task WrongPasswordAndUnknownUserAreBothUnauthorized()
    {
        using var host = new WebAPIHost();
        await host.StartAsync();

        using var wrongPassword = await host.LoginAsync("alice", "wrong-password");
        using var unknownUser = await host.LoginAsync("nobody", "wrong-password");

        Assert.AreEqual(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
    }

    [TestMethod]
    public async Task BannedAccountIsForbidden()
    {
        using var host = new WebAPIHost();
        await host.StartAsync();

        using var response = await host.LoginAsync("banned");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task OverlongCredentialsAreRejectedBeforeLookup()
    {
        using var host = new WebAPIHost();
        await host.StartAsync();

        using var longUsername = await host.LoginAsync(new string('a', 51));
        using var longPassword = await host.LoginAsync("alice", new string('a', 101));

        Assert.AreEqual(HttpStatusCode.BadRequest, longUsername.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, longPassword.StatusCode);
    }

    [TestMethod]
    public async Task RepeatedFailuresLockTheAddressOutEvenWithTheRightPassword()
    {
        using var host = new WebAPIHost(Lockout(perAddress: 3));
        await host.StartAsync();

        for (var i = 0; i < 3; i++)
        {
            using var failure = await host.LoginAsync("alice", "wrong-password");
            Assert.AreEqual(HttpStatusCode.Unauthorized, failure.StatusCode);
        }

        using var locked = await host.LoginAsync("alice");

        Assert.AreEqual((HttpStatusCode)429, locked.StatusCode);
        Assert.AreEqual(TimeSpan.FromMinutes(1), locked.Headers.RetryAfter?.Delta);
    }

    [TestMethod]
    public async Task LockoutExpires()
    {
        using var host = new WebAPIHost(Lockout(perAddress: 3));
        await host.StartAsync();

        for (var i = 0; i < 3; i++)
            (await host.LoginAsync("alice", "wrong-password")).Dispose();

        host.Time.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(1));

        using var response = await host.LoginAsync("alice");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task LockoutGrowsButNeverPastTheCap()
    {
        using var host = new WebAPIHost(Lockout(perAddress: 3));
        await host.StartAsync();

        var retryAfters = new List<TimeSpan>();

        for (var i = 0; i < 8; i++)
        {
            (await host.LoginAsync("alice", "wrong-password")).Dispose();

            using var probe = await host.LoginAsync("alice", "wrong-password");

            if (probe.Headers.RetryAfter?.Delta is { } retryAfter)
            {
                retryAfters.Add(retryAfter);
                host.Time.Advance(retryAfter + TimeSpan.FromSeconds(1));
            }
        }

        // 1, 2, 4 minutes, then held at the 4 minute cap.
        Assert.AreEqual(TimeSpan.FromMinutes(1), retryAfters[0]);
        Assert.AreEqual(TimeSpan.FromMinutes(2), retryAfters[1]);
        Assert.IsTrue(retryAfters.All(x => x <= TimeSpan.FromMinutes(4)));
        Assert.AreEqual(TimeSpan.FromMinutes(4), retryAfters[^1]);

        host.Time.Advance(TimeSpan.FromMinutes(5));

        using var response = await host.LoginAsync("alice");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task AnAttackersLockoutDoesNotLockOutTheOwnerElsewhere()
    {
        using var host = new WebAPIHost(Lockout(perAddress: 3));
        await host.StartAsync();

        for (var i = 0; i < 5; i++)
            (await host.LoginAsync("alice", "wrong-password", address: "203.0.113.66")).Dispose();

        using var attacker = await host.LoginAsync("alice", address: "203.0.113.66");
        using var owner = await host.LoginAsync("alice", address: "198.51.100.20");

        Assert.AreEqual((HttpStatusCode)429, attacker.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, owner.StatusCode);
    }

    [TestMethod]
    public async Task UnknownUsernamesLockOutTheSameWay()
    {
        using var host = new WebAPIHost(Lockout(perAddress: 3));
        await host.StartAsync();

        for (var i = 0; i < 3; i++)
            (await host.LoginAsync("nobody", "wrong-password")).Dispose();

        using var response = await host.LoginAsync("nobody", "wrong-password");

        Assert.AreEqual((HttpStatusCode)429, response.StatusCode);
    }

    [TestMethod]
    public async Task UsernameCaseDoesNotDodgeTheLockout()
    {
        using var host = new WebAPIHost(Lockout(perAddress: 3));
        await host.StartAsync();

        (await host.LoginAsync("alice", "wrong-password")).Dispose();
        (await host.LoginAsync("ALICE", "wrong-password")).Dispose();
        (await host.LoginAsync("Alice", "wrong-password")).Dispose();

        using var response = await host.LoginAsync("alice");

        Assert.AreEqual((HttpStatusCode)429, response.StatusCode);
    }

    [TestMethod]
    public async Task SpreadOutGuessingLocksTheAccountExceptForAddressesThatLoggedInBefore()
    {
        using var host = new WebAPIHost(Lockout(perAddress: 100, perAccount: 4));
        await host.StartAsync();

        const string home = "198.51.100.20";

        using (var earlier = await host.LoginAsync("alice", address: home))
            Assert.AreEqual(HttpStatusCode.OK, earlier.StatusCode);

        for (var i = 0; i < 4; i++)
            (await host.LoginAsync("alice", "wrong-password", address: $"203.0.113.{i + 1}")).Dispose();

        using var newAddress = await host.LoginAsync("alice", address: "192.0.2.50");
        using var homeAddress = await host.LoginAsync("alice", address: home);

        Assert.AreEqual((HttpStatusCode)429, newAddress.StatusCode);
        Assert.AreEqual(TimeSpan.FromMinutes(15), newAddress.Headers.RetryAfter?.Delta);
        Assert.AreEqual(HttpStatusCode.OK, homeAddress.StatusCode);

        host.Time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        using var afterWindow = await host.LoginAsync("alice", address: "192.0.2.50");

        Assert.AreEqual(HttpStatusCode.OK, afterWindow.StatusCode);
    }

    [TestMethod]
    public async Task LoginIsRateLimitedPerAddress()
    {
        using var host = new WebAPIHost(new() { ["WebAPI:RateLimits:LoginPerMinute"] = "3" });
        await host.StartAsync();

        for (var i = 0; i < 3; i++)
        {
            using var allowed = await host.LoginAsync("alice");
            Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var limited = await host.LoginAsync("alice");
        using var otherAddress = await host.LoginAsync("alice", address: "198.51.100.99");

        Assert.AreEqual((HttpStatusCode)429, limited.StatusCode);
        Assert.IsNotNull(limited.Headers.RetryAfter?.Delta);
        Assert.AreEqual(HttpStatusCode.OK, otherAddress.StatusCode);
    }

    [TestMethod]
    public async Task IPv6AddressesInOneSlash64ShareALimit()
    {
        using var host = new WebAPIHost(new() { ["WebAPI:RateLimits:LoginPerMinute"] = "2" });
        await host.StartAsync();

        (await host.LoginAsync("alice", address: "2001:db8:1:2::1")).Dispose();
        (await host.LoginAsync("alice", address: "2001:db8:1:2::2")).Dispose();

        using var sameSlash64 = await host.LoginAsync("alice", address: "2001:db8:1:2:ffff::3");
        using var otherSlash64 = await host.LoginAsync("alice", address: "2001:db8:1:3::1");

        Assert.AreEqual((HttpStatusCode)429, sameSlash64.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, otherSlash64.StatusCode);
    }

    [TestMethod]
    public async Task RegisterIsRateLimitedPerAddress()
    {
        using var host = new WebAPIHost(new() { ["WebAPI:RateLimits:RegisterPerHour"] = "2" });
        await host.StartAsync();

        var client = host.CreateClient(WebAPIHost.DefaultAddress);

        for (var i = 0; i < 2; i++)
        {
            using var allowed = await client.PostAsJsonAsync("/register", new { username = $"newuser{i}", password = "password123" });
            Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var limited = await client.PostAsJsonAsync("/register", new { username = "newuser9", password = "password123" });

        Assert.AreEqual((HttpStatusCode)429, limited.StatusCode);
        Assert.IsNotNull(limited.Headers.RetryAfter?.Delta);

        using var login = await host.LoginAsync("newuser9", "password123", address: "198.51.100.99");

        Assert.AreEqual(HttpStatusCode.Unauthorized, login.StatusCode, "a rate-limited registration must not create the account");
    }

    [TestMethod]
    public async Task ForwardedForIsTrustedFromALocalProxy()
    {
        using var host = new WebAPIHost(new() { ["WebAPI:RateLimits:LoginPerMinute"] = "1" });
        await host.StartAsync();

        // Two players behind the same local TLS proxy are counted separately.
        using var first = await host.CreateClient("127.0.0.1", forwardedFor: "203.0.113.1").PostAsJsonAsync("/login", new { username = "alice", password = WebAPIHost.Password });
        using var second = await host.CreateClient("127.0.0.1", forwardedFor: "203.0.113.2").PostAsJsonAsync("/login", new { username = "alice", password = WebAPIHost.Password });

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
    }

    [TestMethod]
    public async Task ForwardedForIsIgnoredFromAnyoneElse()
    {
        using var host = new WebAPIHost(new() { ["WebAPI:RateLimits:LoginPerMinute"] = "1" });
        await host.StartAsync();

        // A client talking to WebAPI directly can't pick a fresh address per request.
        using var first = await host.CreateClient("198.51.100.7", forwardedFor: "203.0.113.1").PostAsJsonAsync("/login", new { username = "alice", password = WebAPIHost.Password });
        using var second = await host.CreateClient("198.51.100.7", forwardedFor: "203.0.113.2").PostAsJsonAsync("/login", new { username = "alice", password = WebAPIHost.Password });

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual((HttpStatusCode)429, second.StatusCode);
    }

    [TestMethod]
    public async Task ConfiguredProxyNetworkIsTrusted()
    {
        using var host = new WebAPIHost(new()
        {
            ["WebAPI:RateLimits:LoginPerMinute"] = "1",
            ["WebAPI:TrustedProxies:0"] = "172.16.0.0/12",
        });
        await host.StartAsync();

        using var first = await host.CreateClient("172.18.0.1", forwardedFor: "203.0.113.1").PostAsJsonAsync("/login", new { username = "alice", password = WebAPIHost.Password });
        using var second = await host.CreateClient("172.18.0.1", forwardedFor: "203.0.113.2").PostAsJsonAsync("/login", new { username = "alice", password = WebAPIHost.Password });

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
    }
}
