using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.UdpLibrary.Tests;

/// <summary>
/// Searching the Coin Shop makes the client ask for the definition of every item in the store in one packet, about
/// 68 KiB, which the 64 KiB caps from the security pass (finding F5) refused, disconnecting the player. The caps stay at
/// 64 KiB until the Gateway accepts a player's login, which raises them for that connection only.
/// </summary>
[TestClass]
public class LoggedInPacketLimitsTests
{
    private const string ServerIp = "10.0.0.1";

    // Seen 2026-11-25 from the real client: PacketTunneledClientPacket (short 5, a byte, an int length) around
    // CoinStoreItemDefinitionsRequest (short 165, short 2, a list of 17,023 item ids).
    private const int SeenSearchRequestLength = 68_107;

    private static byte[] CoinStoreItemDefinitionsRequest(int itemCount)
    {
        var packet = new byte[15 + 4 * itemCount];
        var span = packet.AsSpan();

        BinaryPrimitives.WriteInt16LittleEndian(span, 5);
        span[2] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(span[3..], packet.Length - 7);
        BinaryPrimitives.WriteInt16LittleEndian(span[7..], 165);
        BinaryPrimitives.WriteInt16LittleEndian(span[9..], 2);
        BinaryPrimitives.WriteInt32LittleEndian(span[11..], itemCount);

        for (var i = 0; i < itemCount; i++)
            BinaryPrimitives.WriteInt32LittleEndian(span[(15 + 4 * i)..], i + 8);

        return packet;
    }

    private static (FakeNetwork Network, FakeManager Server, FakeManager Client, FakeConnection Sender, FakeConnection Accepted) Connect()
    {
        var network = new FakeNetwork();

        var udpParams = FakeManager.ServerParams();
        new PlayerUdpOptions().ApplyTo(udpParams);

        var server = FakeManager.Create(network, ServerIp, udpParams);
        var client = FakeManager.Create(network, "10.0.0.2", FakeManager.ClientParams());
        var sender = client.EstablishConnection(ServerIp, server.EndPoint.Port);

        Assert.IsNotNull(sender);

        network.Pump(5, server, client);

        return (network, server, client, sender, server.Accepted.Single());
    }

    private static void PumpUntilSettled(FakeNetwork network, FakeConnection accepted, params IGiveTime[] managers)
    {
        // a 68 KiB packet goes as well over a hundred fragments, a window at a time
        for (var i = 0; i < 2000 && accepted.Received.Count == 0 && accepted.Status == Status.Connected; i++)
            network.Pump(1, managers);
    }

    [TestMethod]
    public void TheSeenRequestIsTheOneBuiltHere()
    {
        Assert.AreEqual(SeenSearchRequestLength, CoinStoreItemDefinitionsRequest(17_023).Length);
    }

    [TestMethod]
    public void CoinShopSearchRequestIsRefusedBeforeLogin()
    {
        var (network, server, client, sender, accepted) = Connect();

        sender.Send(UdpChannel.Reliable1, CoinStoreItemDefinitionsRequest(17_023));
        PumpUntilSettled(network, accepted, server, client);

        Assert.AreEqual(Status.Disconnected, accepted.Status, "a connection nobody has logged in on keeps the 64 KiB cap");
        Assert.AreEqual(DisconnectReason.CorruptPacket, accepted.DisconnectReason);
        Assert.AreEqual(0, accepted.Received.Count);
    }

    [TestMethod]
    public void CoinShopSearchRequestIsAcceptedAfterLogin()
    {
        var (network, server, client, sender, accepted) = Connect();

        accepted.RaiseIncomingPacketLimits();

        var request = CoinStoreItemDefinitionsRequest(17_023);
        sender.Send(UdpChannel.Reliable1, request);
        PumpUntilSettled(network, accepted, server, client);

        Assert.AreEqual(Status.Connected, accepted.Status);
        CollectionAssert.AreEqual(request, accepted.Received.Single());
        Assert.AreEqual(0, accepted.IncomingFragmentBytes, "the reassembly buffer is given back");
    }

    [TestMethod]
    public void PacketOverTheLoggedInCapIsStillRefused()
    {
        var (network, server, client, sender, accepted) = Connect();

        accepted.RaiseIncomingPacketLimits();

        sender.Send(UdpChannel.Reliable1, new byte[new PlayerUdpOptions().LoggedInLogicalPacketMax + 1]);
        PumpUntilSettled(network, accepted, server, client);

        Assert.AreEqual(Status.Disconnected, accepted.Status);
        Assert.AreEqual(DisconnectReason.CorruptPacket, accepted.DisconnectReason);
        Assert.AreEqual(0, accepted.Received.Count);
    }

    [TestMethod]
    public void PlayerUdpOptionsRaiseOnlyAfterLogin()
    {
        var options = new PlayerUdpOptions();
        var udpParams = new UdpParams();

        options.ApplyTo(udpParams);

        Assert.AreEqual(64 * 1024, udpParams.IncomingLogicalPacketMax, "unchanged before login");
        Assert.AreEqual(64 * 1024, udpParams.IncomingFragmentBytesMax, "unchanged before login");
        Assert.AreEqual(96 * 1024, udpParams.RaisedIncomingLogicalPacketMax);
        Assert.AreEqual(96 * 1024, udpParams.RaisedIncomingFragmentBytesMax);
    }

    [TestMethod]
    public void RaisingNeverAddsAFragmentCap()
    {
        var network = new FakeNetwork();
        var server = FakeManager.Create(network, ServerIp, FakeManager.ServerParams());
        var client = FakeManager.Create(network, "10.0.0.2", FakeManager.ClientParams());
        var sender = client.EstablishConnection(ServerIp, server.EndPoint.Port);

        Assert.IsNotNull(sender);
        network.Pump(5, server, client);

        var accepted = server.Accepted.Single();
        accepted.RaiseIncomingPacketLimits();

        Assert.AreEqual(0, accepted.IncomingFragmentBytesMax, "0 means no cap, and raising leaves it so");
        Assert.AreEqual(server.Params.IncomingLogicalPacketMax, accepted.IncomingLogicalPacketMax, "never lowered");
    }

    /// <summary>
    /// The client asks for every item the store lists, so the store's size decides the largest request. Fails when the
    /// store grows past what <see cref="PlayerUdpOptions.LoggedInLogicalPacketMax"/> fits: raise it then, or searching
    /// the Coin Shop disconnects again.
    /// </summary>
    [TestMethod]
    public void ARequestForEveryShippedStoreItemFitsTheLoggedInCap()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindShippedFile("CoinStoreItems.json")));
        var itemCount = document.RootElement.GetArrayLength();

        var options = new PlayerUdpOptions();
        var requestLength = CoinStoreItemDefinitionsRequest(itemCount).Length;

        Assert.IsTrue(itemCount > 17_000, $"the shipped store has {itemCount} items");
        Assert.IsTrue(requestLength <= options.LoggedInLogicalPacketMax, $"{requestLength} bytes for {itemCount} items");
        Assert.IsTrue(requestLength <= options.LoggedInFragmentBytesMax, $"{requestLength} bytes for {itemCount} items");
    }

    private static string FindShippedFile(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", name);

            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"src/Resources/{name} not found above the test output.");
    }
}
