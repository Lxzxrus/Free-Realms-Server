using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;
using Sanctuary.UdpLibrary.Internal;

namespace Sanctuary.UdpLibrary.Tests;

/// <summary>
/// Task 18 (status-reflection): a request whose sender address can be forged never gets a reply larger than itself, and
/// one address gets at most UnverifiedReplyRatePerIp such replies per window, so the server can't be used to flood
/// whoever owns a forged address.
/// </summary>
[TestClass]
public class ReflectionTests
{
    private const string ServerIp = "10.0.0.1";

    // the Login server's status reply: online, locked, players
    private static readonly byte[] StatusReply = [1, 0, 7, 0, 0, 0];

    private static readonly IPEndPoint Victim = new(IPAddress.Parse("203.0.113.9"), 5000);

    private static FakeManager Server(FakeNetwork network, Action<UdpParams>? configure = null)
    {
        var udpParams = FakeManager.ServerParams();
        udpParams.HandshakeTimeout = 10000;
        configure?.Invoke(udpParams);

        var server = FakeManager.Create(network, ServerIp, udpParams);
        server.StatusReply = StatusReply;

        return server;
    }

    private static byte[] StatusRequest(int length)
    {
        var request = new byte[length];
        request[1] = (byte)UdpPacketType.ServerStatus;
        return request;
    }

    private static byte[] Connect(int connectCode, string protocolName = "Fake", int? length = null)
    {
        var name = Encoding.ASCII.GetBytes(protocolName);
        var connect = new byte[length ?? Math.Max(14 + name.Length + 1, UdpConnection.ConfirmPacketSize)];

        connect[1] = (byte)UdpPacketType.Connect;
        BinaryPrimitives.WriteInt32BigEndian(connect.AsSpan(2), 3);
        BinaryPrimitives.WriteInt32BigEndian(connect.AsSpan(6), connectCode);
        BinaryPrimitives.WriteInt32BigEndian(connect.AsSpan(10), 512);
        name.AsSpan(0, Math.Min(name.Length, connect.Length - 14)).CopyTo(connect.AsSpan(14));

        return connect;
    }

    /// <summary>Sends <paramref name="request"/> from <paramref name="from"/> and returns what the server sent back to it.</summary>
    private static byte[][] Exchange(FakeNetwork network, FakeManager server, IPEndPoint from, byte[] request)
    {
        var before = network.Sent.Count;

        network.Deliver(from, server.EndPoint, request);
        network.Pump(1, server);

        return network.Sent.Skip(before).Where(x => x.To.Equals(from)).Select(x => x.Data).ToArray();
    }

    private static void AssertNoLargerThan(byte[] request, byte[][] replies)
    {
        foreach (var reply in replies)
            Assert.IsTrue(reply.Length <= request.Length, $"a {reply.Length} byte reply to a {request.Length} byte request ({Convert.ToHexString(request)})");
    }

    // ---- the server status reply (the launcher's ping) ----

    [TestMethod]
    public void StatusRequestSmallerThanTheReplyIsNotAnswered()
    {
        var network = new FakeNetwork();
        var server = Server(network);

        Assert.AreEqual(0, Exchange(network, server, Victim, StatusRequest(2)).Length, "OSFR's 2 byte ping got a 20 byte reply");
        Assert.AreEqual(0, Exchange(network, server, Victim, StatusRequest(5)).Length);
        Assert.AreEqual(2, server.Stats.RefusedReplySize);
    }

    [TestMethod]
    public void StatusRequestAsLargeAsTheReplyIsAnswered()
    {
        var network = new FakeNetwork();
        var server = Server(network);

        var reply = Exchange(network, server, Victim, StatusRequest(6)).Single();
        CollectionAssert.AreEqual(StatusReply, reply);

        var reply2 = Exchange(network, server, Victim, StatusRequest(40)).Single();
        CollectionAssert.AreEqual(StatusReply, reply2, "a larger request gets the same reply, not a larger one");
    }

    [TestMethod]
    public void StatusRepliesAreRateLimitedPerAddress()
    {
        var network = new FakeNetwork();
        var server = Server(network, p =>
        {
            p.UnverifiedReplyRatePerIp = 3;
            p.UnverifiedReplyWindow = 10000;
        });

        var answered = Enumerable.Range(0, 10).Count(_ => Exchange(network, server, Victim, StatusRequest(6)).Length > 0);
        Assert.AreEqual(3, answered);
        Assert.AreEqual(7, server.Stats.RefusedReplyRate);

        // the port doesn't matter: the limit is per IP address
        Assert.AreEqual(0, Exchange(network, server, new IPEndPoint(Victim.Address, 5001), StatusRequest(6)).Length);

        // another address has its own budget
        Assert.AreEqual(1, Exchange(network, server, new IPEndPoint(IPAddress.Parse("198.51.100.4"), 5000), StatusRequest(6)).Length);

        // and the victim's comes back after the window
        network.Now += 10000;
        Assert.AreEqual(1, Exchange(network, server, Victim, StatusRequest(6)).Length);
    }

    [TestMethod]
    public void NoRateLimitWhenTheSettingIsZero()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.UnverifiedReplyRatePerIp = 0);

        var answered = Enumerable.Range(0, 50).Count(_ => Exchange(network, server, Victim, StatusRequest(6)).Length > 0);
        Assert.AreEqual(50, answered);
    }

    [TestMethod]
    public void NoStatusReplyFromAManagerThatDoesntGiveOne()
    {
        var network = new FakeNetwork();
        var server = Server(network);
        server.StatusReply = null;

        Assert.AreEqual(0, Exchange(network, server, Victim, StatusRequest(64)).Length);
    }

    // ---- the unreachable-connection reply ----

    [TestMethod]
    public void UnreachableRepliesAreRateLimitedAndNoLargerThanTheRequest()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.UnverifiedReplyRatePerIp = 4);

        byte[] stray = [0x55, 0x01];

        var replies = Enumerable.Range(0, 10).SelectMany(_ => Exchange(network, server, Victim, stray)).ToArray();

        Assert.AreEqual(4, replies.Length);
        AssertNoLargerThan(stray, replies);
        Assert.IsTrue(replies.All(x => x[1] == (byte)UdpPacketType.UnreachableConnection));
    }

    // ---- the confirm answering a connect request ----

    [TestMethod]
    public void ConnectSmallerThanTheConfirmIsNotAnswered()
    {
        var network = new FakeNetwork();
        var server = Server(network);

        // a version 2 connect request has no protocol name: 14 bytes, against a 21 byte confirm
        Assert.AreEqual(0, Exchange(network, server, Victim, Connect(1, protocolName: "", length: 14)).Length);
    }

    [TestMethod]
    public void RepeatedConnectRequestsGetConfirmsOnlyWithinTheRateLimit()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.UnverifiedReplyRatePerIp = 5);

        var connect = Connect(12345);

        // the first request creates the connection; the rest are the same request again, which used to be answered every time
        var replies = Enumerable.Range(0, 30).SelectMany(_ => Exchange(network, server, Victim, connect)).ToArray();

        Assert.AreEqual(5, replies.Length);
        AssertNoLargerThan(connect, replies);
        Assert.IsTrue(replies.All(x => x[1] == (byte)UdpPacketType.Confirm));
        Assert.AreEqual(1, server.Accepted.Count);
    }

    [TestMethod]
    public void WrongProtocolNameGetsNoReplyAtAll()
    {
        var network = new FakeNetwork();
        var server = Server(network);

        // used to get a confirm and then a terminate: two replies to one request
        Assert.AreEqual(0, Exchange(network, server, Victim, Connect(1, protocolName: "SomethingElse")).Length);

        var connection = server.Accepted.Single();
        Assert.AreEqual(Status.Disconnected, connection.Status);
        Assert.AreEqual(DisconnectReason.OtherProtocolName, connection.DisconnectReason);
    }

    [TestMethod]
    public void NewConnectCodeOnAnUnprovenConnectionDropsItSilently()
    {
        var network = new FakeNetwork();
        var server = Server(network);

        Assert.AreEqual(1, Exchange(network, server, Victim, Connect(1)).Length);

        // a different connect code from the same address used to get a terminate, every time
        Assert.AreEqual(0, Exchange(network, server, Victim, Connect(2)).Length);

        var first = server.Accepted.Single();
        Assert.AreEqual(Status.Disconnected, first.Status);

        // and the client's next request gets a new connection
        Assert.AreEqual(1, Exchange(network, server, Victim, Connect(2)).Length);
        Assert.AreEqual(2, server.Accepted.Count);
    }

    [TestMethod]
    public void UnprovenConnectionSendsNothingButTheConfirm()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.KeepAliveDelay = 1000);

        var connect = Connect(1);
        var replies = Exchange(network, server, Victim, connect);
        Assert.AreEqual(1, replies.Length);

        // a keep-alive is due every second, but the address never proved it's the sender
        network.Pump(1100, server);

        Assert.AreEqual(1, network.Sent.Count(x => x.To.Equals(Victim)));
        Assert.AreEqual(DisconnectReason.ConnectFail, server.Accepted.Single().DisconnectReason);
    }

    // ---- the port remap request ----

    [TestMethod]
    public void ServerDoesNotAnswerAForgedUnreachableWithARemapRequest()
    {
        var network = new FakeNetwork();
        var server = Server(network);

        var client = FakeManager.Create(network, "10.0.0.2", FakeManager.ClientParams());
        var connection = client.EstablishConnection(ServerIp, server.EndPoint.Port);
        Assert.IsNotNull(connection);
        network.Pump(5, server, client);
        connection.Send(UdpChannel.Reliable1, [1]);
        network.Pump(5, server, client);

        // anyone can send this 2 byte packet from a player's address; the server used to answer it with a 21 byte packet
        byte[] unreachable = [0, (byte)UdpPacketType.UnreachableConnection];
        var before = network.Sent.Count;

        network.Deliver(client.EndPoint, server.EndPoint, unreachable);
        network.Pump(1, server);

        Assert.IsFalse(network.Sent.Skip(before).Any(x => x.From.Equals(server.EndPoint)));
        Assert.AreEqual(Status.Connected, server.Accepted.Single().Status);
    }

    [TestMethod]
    public void ClientStillSendsItsRemapRequest()
    {
        var network = new FakeNetwork();
        var server = Server(network);

        var client = FakeManager.Create(network, "10.0.0.2", FakeManager.ClientParams());
        var connection = client.EstablishConnection(ServerIp, server.EndPoint.Port);
        Assert.IsNotNull(connection);
        network.Pump(5, server, client);

        byte[] unreachable = [0, (byte)UdpPacketType.UnreachableConnection];
        var before = network.Sent.Count;

        network.Deliver(server.EndPoint, client.EndPoint, unreachable);
        network.Pump(1, client);

        var remap = network.Sent.Skip(before).Single(x => x.From.Equals(client.EndPoint)).Data;

        Assert.AreEqual((byte)UdpPacketType.RequestRemap, remap[1]);
        Assert.AreEqual(10, remap.Length, "header, connect code and encrypt code: no trailing zeros");
    }

    // ---- everything at once ----

    [TestMethod]
    public void NoUnverifiedRequestGetsALargerReply()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.UnverifiedReplyRatePerIp = 0);

        byte[][] requests =
        [
            StatusRequest(2),
            StatusRequest(6),
            [0, (byte)UdpPacketType.PortAlive],
            [0, (byte)UdpPacketType.KeepAlive],
            [0, (byte)UdpPacketType.UnreachableConnection],
            [0, (byte)UdpPacketType.Terminate, 0, 0, 0, 1, 0, 0],
            [0, (byte)UdpPacketType.RequestRemap, 0, 0, 0, 1, 0, 0, 0, 2],
            [0, (byte)UdpPacketType.Unknown, 1, 2, 3, 4],
            [0, (byte)UdpPacketType.ClockSync, 1, 2],
            [0x55, 0x01],
            Connect(1, protocolName: "", length: 14),
            Connect(2, protocolName: "Fake", length: 19),
            Connect(3),
            Connect(3),
            Connect(4),
            Connect(5, protocolName: "SomethingElse"),
        ];

        foreach (var request in requests)
            AssertNoLargerThan(request, Exchange(network, server, Victim, request));

        network.Pump(1500, server);

        // the connections those requests opened time out without another word
        Assert.IsTrue(server.Accepted.All(x => x.Status == Status.Disconnected));
    }

    [TestMethod]
    public void LimiterTableIsBounded()
    {
        var udpParams = new UdpParams { UnverifiedReplyRatePerIp = 1, UnverifiedReplyWindow = 10000 };
        var limiter = new ReplyLimiter(udpParams);

        for (var i = 0; i < ReplyLimiter.MaxTrackedAddresses; i++)
            Assert.IsTrue(limiter.TryAdmit(new IPEndPoint(new IPAddress(i + 1), 1).Serialize(), 1000));

        Assert.IsFalse(limiter.TryAdmit(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 1).Serialize(), 1000), "full: refuse rather than grow");
        Assert.AreEqual(ReplyLimiter.MaxTrackedAddresses, limiter.TrackedAddresses);

        // once the window has passed, old addresses are forgotten
        Assert.IsTrue(limiter.TryAdmit(new IPEndPoint(IPAddress.Parse("192.0.2.1"), 1).Serialize(), 1000 + 10000));
        Assert.AreEqual(1, limiter.TrackedAddresses);
    }
}
