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
/// Task 14 (udp-hardening): one bad client can't take the server down. Findings F4, F5 and F10 are from
/// docs/security/threat-model.md; the buffer and backlog limits are from docs/performance/baseline.md.
/// </summary>
[TestClass]
public class HardeningTests
{
    private const string ServerIp = "10.0.0.1";

    private static FakeManager Server(FakeNetwork network, Action<UdpParams>? configure = null)
    {
        var udpParams = FakeManager.ServerParams();
        configure?.Invoke(udpParams);
        return FakeManager.Create(network, ServerIp, udpParams);
    }

    private static (FakeManager Manager, FakeConnection Connection) Client(FakeNetwork network, FakeManager server, string ipAddress)
    {
        var client = FakeManager.Create(network, ipAddress, FakeManager.ClientParams());
        var connection = client.EstablishConnection(ServerIp, server.EndPoint.Port);

        Assert.IsNotNull(connection);

        network.Pump(5, server, client);

        return (client, connection);
    }

    private static byte[] Repeat(byte value, int count) => Enumerable.Repeat(value, count).ToArray();

    // ---- F4: an exception while processing one connection drops that connection, never the host ----

    [TestMethod]
    public void HandlerExceptionDropsOnlyThatConnection()
    {
        var network = new FakeNetwork();
        var server = Server(network);
        var (clientA, a) = Client(network, server, "10.0.0.2");
        var (clientB, b) = Client(network, server, "10.0.0.3");

        Assert.AreEqual(Status.Connected, a.Status);
        Assert.AreEqual(Status.Connected, b.Status);

        var serverA = server.Accepted[0];
        var serverB = server.Accepted[1];

        a.Send(UdpChannel.Reliable1, [FakeBehaviour.Throw, 1, 2]);
        b.Send(UdpChannel.Reliable1, [7, 8, 9]);

        // without the guard, this throws out of GiveTime: the server's only loop
        network.Pump(10, server, clientA, clientB);

        Assert.AreEqual(Status.Disconnected, serverA.Status);
        Assert.AreEqual(DisconnectReason.CorruptPacket, serverA.DisconnectReason);
        Assert.IsTrue(serverA.Terminated);
        Assert.AreEqual(Status.Disconnected, a.Status, "the client is told it was terminated");

        Assert.AreEqual(Status.Connected, serverB.Status);
        CollectionAssert.AreEqual(new byte[] { 7, 8, 9 }, serverB.Received.Single());
        Assert.AreEqual(1, server.Stats.ConnectionFaults);

        b.Send(UdpChannel.Reliable1, [10]);
        network.Pump(10, server, clientA, clientB);

        Assert.AreEqual(2, serverB.Received.Count, "the other player carries on");
    }

    [TestMethod]
    public void ExceptionFromDisconnectCallbackDoesNotEscape()
    {
        var network = new FakeNetwork();
        var server = Server(network);
        server.Behaviour.ThrowOnTerminated = true;

        var (_, _) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        // the client goes silent: NoDataTimeout (30 s) disconnects it inside GiveTime, and OnTerminated throws there
        network.Pump(400, 100, server);

        Assert.AreEqual(Status.Disconnected, serverA.Status);
        Assert.AreEqual(DisconnectReason.Timeout, serverA.DisconnectReason);
        Assert.AreEqual(1, server.Stats.ConnectionFaults);
        Assert.AreEqual(0, server.Stats.ConnectionCount);

        server.Behaviour.ThrowOnTerminated = false;

        var (_, c) = Client(network, server, "10.0.0.3");
        Assert.AreEqual(Status.Connected, c.Status, "the server still accepts players");
    }

    [TestMethod]
    [DataRow(new byte[] { 0, 25, 0xff }, DisplayName = "group length cut off after its first byte")]
    [DataRow(new byte[] { 0, 25, 0xff, 0xff, 0xff, 0x80 }, DisplayName = "group length cut off inside its 4-byte form")]
    [DataRow(new byte[] { 0, 25, 0xff, 0xff, 0xff, 0x80, 0, 0, 0, 1 }, DisplayName = "negative group length")]
    [DataRow(new byte[] { 0, 13, 0, 0, 0, 0, 0, 4, 1, 2, 3, 4, 5, 6, 7, 8 }, DisplayName = "fragment longer than its declared total")]
    public void MalformedPacketIsCorruptNotAnException(byte[] packet)
    {
        var network = new FakeNetwork();
        var server = Server(network);
        var (client, a) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        network.Deliver(client.EndPoint, server.EndPoint, a.Craft(packet));
        network.Pump(3, server);

        Assert.AreEqual(Status.Disconnected, serverA.Status);
        Assert.AreEqual(DisconnectReason.CorruptPacket, serverA.DisconnectReason);
        Assert.AreEqual(0, server.Stats.ConnectionFaults, "caught as corruption, not by the exception guard");
        Assert.AreEqual(1, server.Stats.CorruptPacketErrors);
    }

    // ---- F5: connection limits, connect-rate limits, packet and fragment caps ----

    [TestMethod]
    public void PerIpConnectionLimit()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.MaxConnectionsPerIp = 2);

        var (c1, a1) = Client(network, server, "10.0.0.2");
        var (c2, a2) = Client(network, server, "10.0.0.2");
        var (c3, a3) = Client(network, server, "10.0.0.2");
        var (c4, other) = Client(network, server, "10.0.0.3");

        Assert.AreEqual(Status.Connected, a1.Status);
        Assert.AreEqual(Status.Connected, a2.Status);
        Assert.AreEqual(Status.Negotiating, a3.Status, "a third connection from one address is ignored");
        Assert.AreEqual(Status.Connected, other.Status, "other addresses are unaffected");
        Assert.IsTrue(server.Stats.RefusedAddressConnections >= 1);

        a1.Disconnect();

        // the refused client retries its connect request every second
        network.Pump(150, server, c1, c2, c3, c4);

        Assert.AreEqual(Status.Connected, a3.Status, "a slot frees when one of them leaves");
        Assert.AreEqual(2, server.ConnectionsFromAddress(server.Accepted.Last().SocketAddress));
    }

    [TestMethod]
    public void PerIpConnectRate()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.ConnectRatePerIp = 2);

        for (var i = 0; i < 2; i++)
        {
            var (client, connection) = Client(network, server, "10.0.0.2");
            Assert.AreEqual(Status.Connected, connection.Status);

            connection.Disconnect();
            network.Pump(2, server, client);
        }

        var (c3, a3) = Client(network, server, "10.0.0.2");
        Assert.AreEqual(Status.Negotiating, a3.Status, "a third new connection within the window is ignored");
        Assert.IsTrue(server.Stats.RefusedAddressRate >= 1);

        var (_, other) = Client(network, server, "10.0.0.3");
        Assert.AreEqual(Status.Connected, other.Status);

        // the window is 10 s
        network.Pump(1100, server, c3);

        Assert.AreEqual(Status.Connected, a3.Status);
    }

    [TestMethod]
    public void GlobalConnectRate()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.ConnectRateGlobal = 2);

        var (_, a) = Client(network, server, "10.0.0.2");
        var (_, b) = Client(network, server, "10.0.0.3");
        var (c3, c) = Client(network, server, "10.0.0.4");

        Assert.AreEqual(Status.Connected, a.Status);
        Assert.AreEqual(Status.Connected, b.Status);
        Assert.AreEqual(Status.Negotiating, c.Status);
        Assert.IsTrue(server.Stats.RefusedGlobalRate >= 1);

        network.Pump(1100, server, c3);

        Assert.AreEqual(Status.Connected, c.Status);
    }

    [TestMethod]
    public void SpoofedConnectIsDroppedSilentlyAfterHandshakeTimeout()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.HandshakeTimeout = 5000);

        // a connect request from an address that will never answer: nobody is bound to it
        var spoofed = new IPEndPoint(IPAddress.Parse("203.0.113.9"), 5000);
        var name = Encoding.ASCII.GetBytes("Fake");
        var connect = new byte[14 + name.Length + 1];

        connect[1] = 1; // Connect
        BinaryPrimitives.WriteInt32BigEndian(connect.AsSpan(2), 3);
        BinaryPrimitives.WriteInt32BigEndian(connect.AsSpan(6), 12345);
        BinaryPrimitives.WriteInt32BigEndian(connect.AsSpan(10), 512);
        name.CopyTo(connect, 14);

        network.Deliver(spoofed, server.EndPoint, connect);
        network.Pump(1, server);

        var spoofedConnection = server.Accepted.Single();
        Assert.AreEqual(Status.Connected, spoofedConnection.Status);

        // a keep-alive and the confirm, which the owner of that address receives and ignores
        var sentToSpoofed = network.Sent.Count(x => x.To.Equals(spoofed));

        var (client, real) = Client(network, server, "10.0.0.2");
        real.Send(UdpChannel.Reliable1, [5]);

        network.Pump(600, server, client);

        Assert.AreEqual(Status.Disconnected, spoofedConnection.Status);
        Assert.AreEqual(DisconnectReason.ConnectFail, spoofedConnection.DisconnectReason);
        Assert.AreEqual(sentToSpoofed, network.Sent.Count(x => x.To.Equals(spoofed)), "nothing more: no terminate, no keep-alives");

        Assert.AreEqual(Status.Connected, real.Status, "a client that talks is not affected");
        Assert.AreEqual(Status.Connected, server.Accepted[1].Status);
    }

    [TestMethod]
    public void PacketLargerThanIncomingLogicalPacketMaxIsRefused()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.IncomingLogicalPacketMax = 1000);
        var (client, a) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        a.Send(UdpChannel.Reliable1, Repeat(1, 900));
        network.Pump(10, server, client);

        Assert.AreEqual(900, serverA.Received.Single().Length, "fragments up to the limit reassemble");

        a.Send(UdpChannel.Reliable1, Repeat(1, 2000));
        network.Pump(10, server, client);

        Assert.AreEqual(Status.Disconnected, serverA.Status);
        Assert.AreEqual(DisconnectReason.CorruptPacket, serverA.DisconnectReason);
    }

    [TestMethod]
    public void FragmentBytesInFlightAreCappedAcrossChannels()
    {
        var network = new FakeNetwork();
        var server = Server(network, p =>
        {
            p.IncomingLogicalPacketMax = 1000;
            p.IncomingFragmentBytesMax = 1500;
        });

        var (client, a) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        void Send(params byte[] packet)
        {
            network.Deliver(client.EndPoint, server.EndPoint, a.Craft(packet));
            network.Pump(1, server);
        }

        static byte[] FirstFragment(byte channel, int total, int length) =>
            [0, (byte)(13 + channel), 0, 0, (byte)(total >> 24), (byte)(total >> 16), (byte)(total >> 8), (byte)total, .. Repeat(1, length)];

        // channel 1: a 900-byte packet in two fragments, completed, which gives its 900 bytes back
        Send(FirstFragment(0, 900, 450));
        Send([0, 13, 0, 1, .. Repeat(1, 450)]);
        Assert.AreEqual(900, serverA.Received.Single().Length);

        // channel 2 starts a 900-byte packet: 900 in flight
        Send(FirstFragment(1, 900, 100));
        Assert.AreEqual(Status.Connected, serverA.Status);
        Assert.AreEqual(900, serverA.IncomingFragmentBytes);

        // channel 3 starts another: 1800 would be in flight, over the 1500 cap
        Send(FirstFragment(2, 900, 100));
        Assert.AreEqual(Status.Disconnected, serverA.Status);
        Assert.AreEqual(DisconnectReason.CorruptPacket, serverA.DisconnectReason);
    }

    [TestMethod]
    public void LimiterKeysByAddressNotPort()
    {
        static SocketAddress At(string ip, int port) => new IPEndPoint(IPAddress.Parse(ip), port).Serialize();

        Assert.AreEqual(ConnectLimiter.AddressKey(At("10.0.0.2", 1)), ConnectLimiter.AddressKey(At("10.0.0.2", 65000)));
        Assert.AreNotEqual(ConnectLimiter.AddressKey(At("10.0.0.2", 1)), ConnectLimiter.AddressKey(At("10.0.0.3", 1)));
    }

    [TestMethod]
    public void LimiterTableIsBounded()
    {
        var limiter = new ConnectLimiter(new UdpParams { ConnectRatePerIp = 1, ConnectRateWindow = 10000 });
        var now = 1_000_000L;

        static SocketAddress At(int i) => new IPEndPoint(new IPAddress(BitConverter.GetBytes(0x0a000000 + i)), 1).Serialize();

        for (var i = 0; i < ConnectLimiter.MaxTrackedAddresses; i++)
            Assert.AreEqual(ConnectRefusal.None, limiter.TryAdmit(At(i), now));

        Assert.AreEqual(ConnectRefusal.TooManyAddresses, limiter.TryAdmit(At(ConnectLimiter.MaxTrackedAddresses), now));

        // once their windows pass, idle addresses are forgotten
        now += 10000;

        Assert.AreEqual(ConnectRefusal.None, limiter.TryAdmit(At(ConnectLimiter.MaxTrackedAddresses), now));
        Assert.AreEqual(1, limiter.TrackedAddresses);
    }

    [TestMethod]
    public void PlayerUdpOptionsApplyLaunchDefaults()
    {
        var udpParams = new UdpParams();

        new PlayerUdpOptions().ApplyTo(udpParams);

        Assert.AreEqual(64 * 1024, udpParams.IncomingLogicalPacketMax);
        Assert.IsTrue(udpParams.MaxConnectionsPerIp > 0);
        Assert.IsTrue(udpParams.ConnectRatePerIp > 0);
        Assert.IsTrue(udpParams.ConnectRateGlobal > 0);
        Assert.IsTrue(udpParams.HandshakeTimeout > 0);
    }
}
