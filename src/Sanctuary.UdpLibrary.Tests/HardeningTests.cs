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
}
