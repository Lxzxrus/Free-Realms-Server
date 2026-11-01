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

    // The F4 and F10 tests run twice: with the application's callbacks inline, as the Login server has them, and queued, as
    // the Gateway has them (GatewayServer sets EventQueuing).

    [TestMethod]
    [DataRow(false, DisplayName = "callbacks inline")]
    [DataRow(true, DisplayName = "callbacks queued, as on the Gateway")]
    public void HandlerExceptionDropsOnlyThatConnection(bool queued)
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.EventQueuing = queued);
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
    [DataRow(false, DisplayName = "callbacks inline")]
    [DataRow(true, DisplayName = "callbacks queued, as on the Gateway")]
    public void ExceptionFromDisconnectCallbackDoesNotEscape(bool queued)
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.EventQueuing = queued);
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
        var connect = new byte[UdpConnection.ConfirmPacketSize];

        connect[1] = 1; // Connect
        BinaryPrimitives.WriteInt32BigEndian(connect.AsSpan(2), 3);
        BinaryPrimitives.WriteInt32BigEndian(connect.AsSpan(6), 12345);
        BinaryPrimitives.WriteInt32BigEndian(connect.AsSpan(10), 512);
        name.CopyTo(connect, 14);

        network.Deliver(spoofed, server.EndPoint, connect);
        network.Pump(1, server);

        var spoofedConnection = server.Accepted.Single();
        Assert.AreEqual(Status.Connected, spoofedConnection.Status);

        // the confirm, which the owner of that address receives and ignores
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

    // ---- F10: a connection that throws repeatedly is disconnected ----

    [TestMethod]
    [DataRow(false, DisplayName = "callbacks inline")]
    [DataRow(true, DisplayName = "callbacks queued, as on the Gateway")]
    public void RepeatedHandlerFaultsDisconnect(bool queued)
    {
        var network = new FakeNetwork();
        var server = Server(network, p =>
        {
            p.FaultLimit = 3;
            p.FaultWindow = 60000;
            p.NoDataTimeout = 0;
            p.EventQueuing = queued;
        });

        server.Behaviour.ReportFaults = true;

        var (client, a) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        void Fault()
        {
            a.Send(UdpChannel.Reliable1, [FakeBehaviour.Throw]);
            network.Pump(3, server, client);
        }

        Fault();
        Fault();
        Assert.AreEqual(Status.Connected, serverA.Status, "two within the window is under the limit");

        // the window starts at the first fault; let it pass
        network.Pump(61, 1000, server, client);

        Fault();
        Fault();
        Assert.AreEqual(Status.Connected, serverA.Status, "the count starts again in a new window");

        Fault();
        Assert.AreEqual(Status.Disconnected, serverA.Status);
        Assert.AreEqual(DisconnectReason.CorruptPacket, serverA.DisconnectReason);
        Assert.AreEqual(1, server.Stats.FaultLimitDisconnects);
        Assert.AreEqual(0, server.Stats.ConnectionFaults, "reported faults never reach the exception guard");
    }

    // ---- Queued callbacks, as on the Gateway ----

    // a packet whose handler disconnects its connection, as the Gateway's login handler does when it refuses a ticket
    private const byte DisconnectMe = 0xDC;

    [TestMethod]
    [DataRow(false, DisplayName = "callbacks inline")]
    [DataRow(true, DisplayName = "callbacks queued, as on the Gateway")]
    public void NoPacketIsHandledAfterItsConnectionDisconnects(bool queued)
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.EventQueuing = queued);

        server.Behaviour.OnPacket = (connection, packet) =>
        {
            if (packet[0] == DisconnectMe)
                connection.Disconnect();
        };

        var (client, a) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        // one datagram grouping two packets (00 03): queued, the second waits in the queue before the first is handled
        network.Deliver(client.EndPoint, server.EndPoint, a.Craft(0, 3, 2, DisconnectMe, 1, 2, FakeBehaviour.Throw, 2));
        network.Pump(3, server);

        CollectionAssert.AreEqual(new byte[] { DisconnectMe, 1 }, serverA.Received.Single());
        Assert.AreEqual(Status.Disconnected, serverA.Status);
        Assert.AreEqual(DisconnectReason.Application, serverA.DisconnectReason);
        Assert.IsTrue(serverA.Terminated);
        Assert.AreEqual(0, server.Stats.ConnectionFaults, "the second packet, which throws, never reached its handler");
        Assert.AreEqual(0, serverA.QueuedPackets);
    }

    [TestMethod]
    public void ConnectionOverTheQueuedPacketLimitIsDisconnected()
    {
        var network = new FakeNetwork();
        var server = Server(network, p =>
        {
            p.EventQueuing = true;
            p.MaxQueuedPacketsPerConnection = 4;
        });

        var (clientA, a) = Client(network, server, "10.0.0.2");
        var (clientB, b) = Client(network, server, "10.0.0.3");
        var serverA = server.Accepted[0];
        var serverB = server.Accepted[1];

        // all read in one poll, before any is handled: five from A, one over the limit, and four from B
        for (byte i = 1; i <= 5; i++)
        {
            network.Deliver(clientA.EndPoint, server.EndPoint, a.Craft(i));

            if (i <= 4)
                network.Deliver(clientB.EndPoint, server.EndPoint, b.Craft(i));
        }

        network.Pump(1, server);

        Assert.AreEqual(Status.Disconnected, serverA.Status);
        Assert.AreEqual(DisconnectReason.CorruptPacket, serverA.DisconnectReason);
        Assert.AreEqual(1, server.Stats.QueueLimitDisconnects);
        Assert.IsEmpty(serverA.Received, "what it had queued went with it");

        Assert.AreEqual(Status.Connected, serverB.Status);
        Assert.HasCount(4, serverB.Received);

        // the limit is on packets waiting, not packets sent: B's queue emptied as it was delivered
        for (byte i = 5; i <= 8; i++)
            network.Deliver(clientB.EndPoint, server.EndPoint, b.Craft(i));

        network.Pump(1, server);

        Assert.AreEqual(Status.Connected, serverB.Status);
        Assert.HasCount(8, serverB.Received);
        Assert.AreEqual(1, server.Stats.QueueLimitDisconnects);
    }

    [TestMethod]
    public void ConnectionOverTheQueuedPacketLimitOnAReliablePacketLeavesThePriorityQueue()
    {
        var network = new FakeNetwork();
        var server = Server(network, p =>
        {
            p.EventQueuing = true;
            p.MaxQueuedPacketsPerConnection = 4;
        });

        var (client, a) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        // four unreliable packets fill the queue, then a reliable one (00 09, id 0) goes over: its acknowledgement is sent
        // after the connection has been disconnected for it, and taken out of the priority queue
        for (byte i = 1; i <= 4; i++)
            network.Deliver(client.EndPoint, server.EndPoint, a.Craft(i));

        network.Deliver(client.EndPoint, server.EndPoint, a.Craft(0, 9, 0, 0, 5));
        network.Pump(1, server);

        Assert.AreEqual(Status.Disconnected, serverA.Status);
        Assert.AreEqual(1, server.Stats.QueueLimitDisconnects);
        Assert.AreEqual(-1, serverA.PriorityQueuePosition, "the acknowledgement didn't put it back");

        // a connection in the queue is given time at least every 10 minutes, and puts itself back each time
        network.Pump(130, 10000, server);

        Assert.AreEqual(-1, serverA.PriorityQueuePosition);
        Assert.AreEqual(0, server.Stats.ConnectionCount);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "callbacks inline")]
    [DataRow(true, DisplayName = "callbacks queued, as on the Gateway")]
    public void ConnectionDroppedForACorruptPacketInsideAReliableOneLeavesThePriorityQueue(bool queued)
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.EventQueuing = queued);
        var (client, a) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        // a reliable packet (00 09, id 0) carrying a multi-packet (00 03) whose piece claims 9 bytes when 1 is left
        network.Deliver(client.EndPoint, server.EndPoint, a.Craft(0, 9, 0, 0, 0, 3, 9, 1));
        network.Pump(1, server);

        Assert.AreEqual(Status.Disconnected, serverA.Status);
        Assert.AreEqual(DisconnectReason.CorruptPacket, serverA.DisconnectReason);
        Assert.AreEqual(-1, serverA.PriorityQueuePosition);
    }

    [TestMethod]
    [DataRow(false, DisplayName = "callbacks inline")]
    [DataRow(true, DisplayName = "callbacks queued, as on the Gateway")]
    public void PacketsFailingTheirCrcCheckNeverDisconnectAndOnlySoManyAreQueued(bool queued)
    {
        var network = new FakeNetwork();
        var server = Server(network, p =>
        {
            p.EventQueuing = queued;
            p.MaxQueuedPacketsPerConnection = 4;
        });

        var (client, a) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        var badCrc = a.Craft(1, 2, 3);
        badCrc[^1] ^= 0xFF;

        // all read in one poll, before any is handled: 50 that fail their CRC check from the player's address (anyone can
        // send them, with a forged sender address), well over both limits, then three of the player's own
        for (var i = 0; i < 50; i++)
            network.Deliver(client.EndPoint, server.EndPoint, badCrc);

        for (byte i = 1; i <= 3; i++)
            network.Deliver(client.EndPoint, server.EndPoint, a.Craft(i));

        network.Pump(1, server);

        Assert.AreEqual(Status.Connected, serverA.Status, "they prove nothing about who sent them");
        Assert.AreEqual(0, server.Stats.QueueLimitDisconnects, "and don't count towards the player's packets");
        Assert.HasCount(3, serverA.Received);
        Assert.AreEqual(50, server.Stats.CrcRejectedPackets);

        // queued, those over the limit are dropped; inline there is no queue to fill
        var told = queued ? FakeManager.MaxQueuedCrcRejects : 50;

        Assert.AreEqual(told, serverA.CrcRejects);
        Assert.AreEqual(0, serverA.QueuedCrcRejects);

        // the limit is on those waiting, not those sent
        network.Deliver(client.EndPoint, server.EndPoint, badCrc);
        network.Pump(1, server);

        Assert.AreEqual(told + 1, serverA.CrcRejects);
        Assert.AreEqual(Status.Connected, serverA.Status);
    }

    [TestMethod]
    public void SocketAndConnectionsGetTimeWhileABacklogOfEventsDrains()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.EventQueuing = true);

        // each of these takes 60 ms to handle, so two use up one GiveTime's time for delivering events
        const byte Slow = 0x51;

        server.Behaviour.OnPacket = (_, packet) =>
        {
            if (packet[0] == Slow)
                network.Now += 60;
        };

        var (client, a) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        for (byte i = 0; i < 10; i++)
            network.Deliver(client.EndPoint, server.EndPoint, a.Craft(Slow, i));

        server.GiveTime();

        Assert.HasCount(2, serverA.Received);
        Assert.AreEqual(8, server.Stats.EventListCount);

        // events are still waiting, and the next GiveTime reads the socket and gives the connection its time anyway
        network.Deliver(client.EndPoint, server.EndPoint, a.Craft(7));

        var serviced = server.Stats.PriorityQueueProcessed;

        server.GiveTime();

        Assert.IsEmpty(server.Driver.Inbox, "the socket was read");
        Assert.IsTrue(server.Stats.PriorityQueueProcessed > serviced, "the connection got its time");
        Assert.HasCount(4, serverA.Received);

        network.Pump(10, server, client);

        Assert.HasCount(11, serverA.Received, "and the backlog drained");
        CollectionAssert.AreEqual(new byte[] { 7 }, serverA.Received[^1], "in order");
        Assert.AreEqual(Status.Connected, serverA.Status);
    }

    // ---- Packets from a player's address: only one that passes its CRC check counts as the player's ----

    [TestMethod]
    [DataRow(new byte[] { 0, (byte)UdpPacketType.KeepAlive }, false, DisplayName = "a bare keep-alive")]
    [DataRow(new byte[] { 0, (byte)UdpPacketType.KeepAlive }, true, DisplayName = "a keep-alive with a wrong CRC")]
    [DataRow(new byte[] { 1, 2, 3 }, true, DisplayName = "a packet with a wrong CRC")]
    [DataRow(new byte[] { 0, (byte)UdpPacketType.UnreachableConnection }, false, DisplayName = "an unreachable packet")]
    [DataRow(new byte[] { 0, (byte)UdpPacketType.RequestRemap, 0, 0, 0, 1, 0, 0, 0, 2 }, false, DisplayName = "a remap request")]
    [DataRow(new byte[] { 0, (byte)UdpPacketType.Connect, 0, 0, 0, 3, 0, 0, 0x30, 0x39, 0, 0, 2, 0, (byte)'F', (byte)'a', (byte)'k', (byte)'e', 0, 0, 0 },
        false, DisplayName = "a connect request with another code")]
    public void PacketsAnyoneCouldSendDontKeepAConnectionAlive(byte[] packet, bool wrongCrc)
    {
        var network = new FakeNetwork();
        var server = Server(network);
        var (client, a) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        var forged = packet;

        if (wrongCrc)
        {
            forged = a.Craft(packet);
            forged[^1] ^= 0xFF;
        }

        // the player's client has crashed (it never gets time again), and someone sends the packet from its address every 10 s
        for (var i = 0; i < 6; i++)
        {
            network.Pump(500, server);
            network.Deliver(client.EndPoint, server.EndPoint, forged);
            network.Pump(500, server);

            if (i < 2)
                Assert.AreEqual(Status.Connected, serverA.Status, "nor does the packet disconnect it");
        }

        Assert.AreEqual(Status.Disconnected, serverA.Status);
        Assert.AreEqual(DisconnectReason.Timeout, serverA.DisconnectReason, "NoDataTimeout, 30 s after the client's last packet");
    }

    [TestMethod]
    public void RealKeepAlivesKeepAQuietConnectionAliveAndItTimesOutAfterTheLast()
    {
        var network = new FakeNetwork();
        var server = Server(network);

        // a keep-alive after 10 s without sending anything, as the Gateway's link to Login has
        var clientParams = FakeManager.ClientParams();
        clientParams.KeepAliveDelay = 10000;

        var client = FakeManager.Create(network, "10.0.0.2", clientParams);
        var a = client.EstablishConnection(ServerIp, server.EndPoint.Port);
        Assert.IsNotNull(a);
        network.Pump(5, server, client);

        var serverA = server.Accepted.Single();
        var before = network.Sent.Count;

        // three minutes with nothing to say
        network.Pump(1800, 100, server, client);

        var fromClient = network.Sent.Skip(before).Where(x => x.From.Equals(client.EndPoint)).Select(x => x.Data).ToList();

        Assert.IsTrue(fromClient.Count >= 17, $"{fromClient.Count} keep-alives");
        Assert.IsTrue(fromClient.All(x => x[0] == 0 && x[1] == (byte)UdpPacketType.KeepAlive), "nothing but keep-alives");
        Assert.AreEqual(Status.Connected, serverA.Status);
        Assert.AreEqual(Status.Connected, a.Status);
        Assert.AreEqual(0, server.Stats.CrcRejectedPackets, "each passed its CRC check");

        // the client goes just after its next keep-alive, and the server drops it NoDataTimeout (30 s) after that one
        before = network.Sent.Count;

        while (!network.Sent.Skip(before).Any(x => x.From.Equals(client.EndPoint)))
            network.Pump(1, 100, server, client);

        network.Pump(290, 100, server);
        Assert.AreEqual(Status.Connected, serverA.Status, "29 s after its last keep-alive");

        network.Pump(20, 100, server);
        Assert.AreEqual(Status.Disconnected, serverA.Status, "31 s after it");
        Assert.AreEqual(DisconnectReason.Timeout, serverA.DisconnectReason);
    }

    [TestMethod]
    public void UnreachablePacketsFromAPlayersAddressDontDisconnectIt()
    {
        var network = new FakeNetwork();
        var server = Server(network);
        var (client, a) = Client(network, server, "10.0.0.2");
        var serverA = server.Accepted[0];

        byte[] unreachable = [0, (byte)UdpPacketType.UnreachableConnection];

        // the player says nothing for 12 s, and someone sends unreachable packets from its address 6 s apart: the second
        // used to disconnect it, coming more than 5 s (MaximumTimeAllowedForPortRemapping) after the first
        network.Deliver(client.EndPoint, server.EndPoint, unreachable);
        network.Pump(600, server, client);
        network.Deliver(client.EndPoint, server.EndPoint, unreachable);
        network.Pump(600, server, client);

        Assert.AreEqual(Status.Connected, serverA.Status);

        a.Send(UdpChannel.Reliable1, [5]);
        network.Pump(10, server, client);

        CollectionAssert.AreEqual(new byte[] { 5 }, serverA.Received.Single());
    }

    [TestMethod]
    public void ClientStillGivesUpOnAServerThatAnswersUnreachable()
    {
        var network = new FakeNetwork();
        var server = Server(network);
        var (client, a) = Client(network, server, "10.0.0.2");

        byte[] unreachable = [0, (byte)UdpPacketType.UnreachableConnection];

        // the server no longer knows the connection (it restarted) and answers everything with unreachable: the side that
        // opened the connection asks for a remap for 5 s, then gives up
        network.Deliver(server.EndPoint, client.EndPoint, unreachable);
        network.Pump(600, client);
        network.Deliver(server.EndPoint, client.EndPoint, unreachable);
        network.Pump(1, client);

        Assert.AreEqual(Status.Disconnected, a.Status);
        Assert.AreEqual(DisconnectReason.UnreachableConnection, a.DisconnectReason);
    }

    [TestMethod]
    public void ClientTimeoutCountsFromTheConfirm()
    {
        var network = new FakeNetwork();
        var server = Server(network);
        var client = FakeManager.Create(network, "10.0.0.2", FakeManager.ClientParams());
        var a = client.EstablishConnection(ServerIp, server.EndPoint.Port);
        Assert.IsNotNull(a);

        // the server is busy for 20 s, so the client's connect requests (one a second) wait in its socket
        network.Pump(200, 100, client);
        Assert.AreEqual(Status.Negotiating, a.Status);

        // then it answers them, and says nothing more
        network.Pump(1, 100, server);
        network.Pump(1, 100, client);
        Assert.AreEqual(Status.Connected, a.Status);

        // the confirm has no CRC, but the client's NoDataTimeout (90 s) still counts from it, not from the first request
        network.Pump(850, 100, client);
        Assert.AreEqual(Status.Connected, a.Status, "85 s after the confirm");

        network.Pump(100, 100, client);
        Assert.AreEqual(Status.Disconnected, a.Status, "95 s after it");
        Assert.AreEqual(DisconnectReason.Timeout, a.DisconnectReason);
    }

    // ---- Task 10: a connection that falls behind is disconnected instead of queueing without bound ----

    [TestMethod]
    public void ConnectionThatFallsBehindIsDisconnected()
    {
        var network = new FakeNetwork();
        var server = Server(network, p => p.ReliableOverflowBytes = 64 * 1024);

        var (lagging, _) = Client(network, server, "10.0.0.2");
        var (healthy, h) = Client(network, server, "10.0.0.3");
        var serverLagging = server.Accepted[0];
        var serverHealthy = server.Accepted[1];

        // the healthy client acknowledges as it goes; the lagging one stops reading (never acknowledges)
        for (var i = 0; i < 100; i++)
        {
            serverHealthy.Send(UdpChannel.Reliable1, Repeat(2, 1024));
            serverLagging.Send(UdpChannel.Reliable1, Repeat(2, 1024));
            network.Pump(1, server, healthy);
        }

        // the backlog is checked when the connection next gets time; with a full window that's its resend timer (at most
        // ResendDelayCap, 8 s), so a connection can go over the limit by what it is sent in that time
        network.Pump(900, server, healthy);

        Assert.AreEqual(Status.Disconnected, serverLagging.Status);
        Assert.AreEqual(DisconnectReason.ReliableOverflow, serverLagging.DisconnectReason);

        Assert.AreEqual(Status.Connected, serverHealthy.Status);
        Assert.AreEqual(100, h.Received.Count);
        Assert.IsTrue(serverHealthy.GetStatsValue().MaxPendingBytes > 0);
    }

    [TestMethod]
    public void BacklogThatDoesNotDrainDisconnectsButABurstDoesNot()
    {
        var network = new FakeNetwork();
        var server = Server(network, p =>
        {
            p.ReliableBacklogBytes = 8 * 1024;
            p.ReliableBacklogTimeout = 5000;
        });

        var (lagging, _) = Client(network, server, "10.0.0.2");
        var (healthy, h) = Client(network, server, "10.0.0.3");
        var serverLagging = server.Accepted[0];
        var serverHealthy = server.Accepted[1];

        // a burst eight times the threshold, like a zone entry, to both; only the healthy client reads it
        for (var i = 0; i < 64; i++)
        {
            serverHealthy.Send(UdpChannel.Reliable1, Repeat(3, 1024));
            serverLagging.Send(UdpChannel.Reliable1, Repeat(3, 1024));
        }

        network.Pump(400, server, healthy);

        Assert.AreEqual(64, h.Received.Count, "the burst drains well inside the timeout");
        Assert.AreEqual(Status.Connected, serverLagging.Status, "4 s behind is under the 5 s timeout");

        network.Pump(600, server, healthy);

        Assert.AreEqual(Status.Connected, serverHealthy.Status);
        Assert.AreEqual(Status.Disconnected, serverLagging.Status);
        Assert.AreEqual(DisconnectReason.ReliableOverflow, serverLagging.DisconnectReason);
    }

    [TestMethod]
    public void PlayerUdpOptionsApplyLaunchDefaults()
    {
        var udpParams = new UdpParams();

        new PlayerUdpOptions().ApplyTo(udpParams);

        Assert.AreEqual(4 * 1024 * 1024, udpParams.IncomingBufferSize, "the size the load test showed was enough for 200 clustered players");
        Assert.AreEqual(64 * 1024, udpParams.IncomingLogicalPacketMax);
        Assert.IsTrue(udpParams.MaxConnectionsPerIp > 0);
        Assert.IsTrue(udpParams.ConnectRatePerIp > 0);
        Assert.IsTrue(udpParams.ConnectRateGlobal > 0);
        Assert.IsTrue(udpParams.HandshakeTimeout > 0);
        Assert.IsTrue(udpParams.ReliableOverflowBytes > 4400 * 1024, "above the 4.2 MiB a zone entry queues");
        Assert.IsTrue(udpParams.ReliableBacklogTimeout > 0);
        Assert.IsTrue(udpParams.FaultLimit > 0);
        Assert.IsTrue(udpParams.MaxQueuedPacketsPerConnection > 0);
    }
}

internal static class ConnectionTestExtensions
{
    public static Statistics.UdpConnectionStatistics GetStatsValue(this UdpConnection connection)
    {
        connection.GetStats(out var stats);
        return stats;
    }
}
