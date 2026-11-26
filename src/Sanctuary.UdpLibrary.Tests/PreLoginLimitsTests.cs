using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;
using Sanctuary.UdpLibrary.Internal;

namespace Sanctuary.UdpLibrary.Tests;

/// <summary>
/// Limits on what a connection can make the server do before it has logged in: how large a packet may come out of
/// decryption, and how often a clock-sync request is answered.
/// </summary>
[TestClass]
public class PreLoginLimitsTests
{
    private const string ServerIp = "10.0.0.1";

    private static (FakeManager Server, FakeManager Client, FakeConnection Connection) Connect(FakeNetwork network, bool expanding)
    {
        var serverParams = FakeManager.ServerParams();
        var clientParams = FakeManager.ClientParams();

        if (expanding)
        {
            // as the Gateway turns compression on (Program.cs)
            serverParams.EncryptMethod[0] = EncryptMethod.UserSupplied;
            serverParams.UserSuppliedEncryptExpansionBytes = 1;
            clientParams.UserSuppliedEncryptExpansionBytes = 1;
        }

        var server = FakeManager.Create(network, ServerIp, serverParams);
        var client = FakeManager.Create(network, "10.0.0.2", clientParams);
        var connection = client.EstablishConnection(ServerIp, server.EndPoint.Port);

        Assert.IsNotNull(connection);

        network.Pump(5, server, client);

        Assert.AreEqual(Status.Connected, connection.Status);

        return (server, client, connection);
    }

    // ---- a packet may not come out of decryption larger than the largest packet a sender may build ----

    [TestMethod]
    public void APacketThatExpandsPastTheLargestPacketIsCorrupt()
    {
        var network = new FakeNetwork();
        var (server, client, connection) = Connect(network, expanding: true);
        var serverConnection = server.Accepted[0];

        // 4 bytes on the wire that decrypt to 1 + 2000
        connection.Send(UdpChannel.Unreliable, [7, FakeConnection.Expand, 2000 >> 8, 2000 & 0xFF]);

        network.Pump(10, server, client);

        Assert.AreEqual(0, serverConnection.Received.Count, "the application never sees it");
        Assert.AreEqual(Status.Disconnected, serverConnection.Status);
        Assert.AreEqual(DisconnectReason.CorruptPacket, serverConnection.DisconnectReason);
    }

    [TestMethod]
    public void APacketThatExpandsToTheLargestPacketStillArrives()
    {
        var network = new FakeNetwork();
        var (server, client, connection) = Connect(network, expanding: true);
        var serverConnection = server.Accepted[0];

        var largest = FakeManager.ServerParams().MaxRawPacketSize;

        // decrypts to exactly the largest packet: the first byte plus the expanded body
        connection.Send(UdpChannel.Unreliable, [7, FakeConnection.Expand, (byte)((largest - 1) >> 8), (byte)(largest - 1)]);

        network.Pump(10, server, client);

        Assert.AreEqual(Status.Connected, serverConnection.Status);
        Assert.AreEqual(largest, serverConnection.Received.Single().Length);
    }

    [TestMethod]
    public void OrdinaryPacketsPassThroughTheCipher()
    {
        var network = new FakeNetwork();
        var (server, client, connection) = Connect(network, expanding: true);
        var serverConnection = server.Accepted[0];

        connection.Send(UdpChannel.Reliable1, [7, 8, 9]);
        connection.Send(UdpChannel.Reliable1, Enumerable.Repeat((byte)3, 2000).ToArray());

        network.Pump(20, server, client);

        Assert.AreEqual(Status.Connected, serverConnection.Status);
        CollectionAssert.AreEqual(new byte[] { 7, 8, 9 }, serverConnection.Received[0]);
        Assert.AreEqual(2000, serverConnection.Received[1].Length, "a message sent in fragments still reassembles");
    }

    // ---- clock-sync requests: one answer per interval, however many arrive ----

    private static byte[] ClockSyncRequests(int count)
    {
        // a Multi packet: 00 03, then per piece a length byte and the piece
        const int pieceLength = 40;

        var packet = new byte[2 + count * (1 + pieceLength)];

        packet[1] = (byte)UdpPacketType.Multi;

        for (var i = 0; i < count; i++)
        {
            var at = 2 + i * (1 + pieceLength);

            packet[at] = pieceLength;
            packet[at + 2] = (byte)UdpPacketType.ClockSync;
        }

        return packet;
    }

    private static int ClockReflects(FakeNetwork network, FakeManager server)
    {
        return network.Sent.Count(sent => sent.From.Equals(server.EndPoint)
            && sent.Data.Length > 2 && sent.Data[0] == 0 && sent.Data[1] == (byte)UdpPacketType.ClockReflect);
    }

    [TestMethod]
    public void ManyClockSyncRequestsInOneDatagramGetOneAnswer()
    {
        var network = new FakeNetwork();
        var (server, client, connection) = Connect(network, expanding: false);

        network.Deliver(client.EndPoint, server.EndPoint, connection.Craft(ClockSyncRequests(10)));

        network.Pump(5, server, client);

        Assert.AreEqual(1, ClockReflects(network, server));
        Assert.AreEqual(Status.Connected, server.Accepted[0].Status, "asking too often is ignored, not punished");
    }

    [TestMethod]
    public void AClockSyncRequestIsAnsweredAgainAfterTheInterval()
    {
        var network = new FakeNetwork();
        var (server, client, connection) = Connect(network, expanding: false);

        network.Deliver(client.EndPoint, server.EndPoint, connection.Craft(ClockSyncRequests(1)));
        network.Pump(5, server, client);

        Assert.AreEqual(1, ClockReflects(network, server));

        // too soon: half the interval
        network.Pump(UdpConnection.ClockReflectMinInterval / 20, server, client);
        network.Deliver(client.EndPoint, server.EndPoint, connection.Craft(ClockSyncRequests(1)));
        network.Pump(5, server, client);

        Assert.AreEqual(1, ClockReflects(network, server));

        // a real client asks every 10 seconds at most
        network.Pump(UdpConnection.ClockReflectMinInterval / 10, server, client);
        network.Deliver(client.EndPoint, server.EndPoint, connection.Craft(ClockSyncRequests(1)));
        network.Pump(5, server, client);

        Assert.AreEqual(2, ClockReflects(network, server));
    }
}
