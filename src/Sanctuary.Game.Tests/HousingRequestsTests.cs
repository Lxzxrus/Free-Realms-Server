using System;
using System.Linq;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Housing;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// "Go to House" on another player, and the house directory's metrics event that used to reach Take Me There.
/// </summary>
[TestClass]
public sealed class HousingRequestsTests
{
    private const ulong OwnerId = 7;
    private const ulong VisitorId = 8;

    private static byte[] Wire(Action<PacketWriter> write)
    {
        using var writer = new PacketWriter();

        write(writer);

        return writer.Buffer;
    }

    [TestMethod]
    public void RequestPlayerHouses_EmptyForm_RoundTrips()
    {
        var wire = new ClientHousingPacketRequestPlayerHouses().Serialize();

        CollectionAssert.AreEqual(new byte[] { 0x7F, 0x00, 0x0E, 0x00 }, wire);
        Assert.IsTrue(ClientHousingPacketRequestPlayerHouses.TryDeserialize(wire, out var packet));
        Assert.IsFalse(packet.IsTargeted);
    }

    [TestMethod]
    public void RequestPlayerHouses_TargetedForm_RoundTrips()
    {
        var wire = new ClientHousingPacketRequestPlayerHouses
        {
            PlayerGuid = GuidHelper.GetPlayerGuid(OwnerId),
            PlayerName = "Nate"
        }.Serialize();

        Assert.IsTrue(ClientHousingPacketRequestPlayerHouses.TryDeserialize(wire, out var packet));
        Assert.IsTrue(packet.IsTargeted);
        Assert.AreEqual(GuidHelper.GetPlayerGuid(OwnerId), packet.PlayerGuid);
        Assert.AreEqual("Nate", packet.PlayerName);
    }

    /// <summary>
    /// The request as the live client sent it in 2014 (<c>p12.pcap</c> in the archived OpenSourceFreeRealms repository).
    /// </summary>
    [TestMethod]
    public void RequestPlayerHouses_ReadsTheLiveCapture()
    {
        var wire = Convert.FromHexString("7F000E003132FA7576AA524B090000004D65676E657369756D");

        Assert.IsTrue(ClientHousingPacketRequestPlayerHouses.TryDeserialize(wire, out var packet));
        Assert.AreEqual(0x4B52AA7675FA3231UL, packet.PlayerGuid);
        Assert.AreEqual("Megnesium", packet.PlayerName);
    }

    [TestMethod]
    public void RequestPlayerHouses_RefusesOtherShapes()
    {
        var guidOnly = Wire(w =>
        {
            w.Write(BaseHousingPacket.OpCode);
            w.Write(ClientHousingPacketRequestPlayerHouses.OpCode);
            w.Write(GuidHelper.GetPlayerGuid(OwnerId));
        });
        var trailing = Wire(w =>
        {
            w.Write(BaseHousingPacket.OpCode);
            w.Write(ClientHousingPacketRequestPlayerHouses.OpCode);
            w.Write(GuidHelper.GetPlayerGuid(OwnerId));
            w.Write("Nate");
            w.Write((byte)0);
        });
        var otherSubOpCode = Wire(w =>
        {
            w.Write(BaseHousingPacket.OpCode);
            w.Write(ClientHousingPacketEnterRequest.OpCode);
        });

        Assert.IsFalse(ClientHousingPacketRequestPlayerHouses.TryDeserialize(guidOnly, out _));
        Assert.IsFalse(ClientHousingPacketRequestPlayerHouses.TryDeserialize(trailing, out _));
        Assert.IsFalse(ClientHousingPacketRequestPlayerHouses.TryDeserialize(otherSubOpCode, out _));
    }

    private static DbHouse House(ulong id, bool published = true, bool locked = false, bool membersOnly = false) => new()
    {
        Id = id,
        CharacterId = OwnerId,
        IsPublished = published,
        IsLocked = locked,
        IsMembersOnly = membersOnly
    };

    private static readonly DbHouse[] OwnersHouses =
    [
        House(1),
        House(2, locked: true),
        House(3, published: false),
        House(4, membersOnly: true)
    ];

    private static ulong[] Listed(ulong playerId, FriendData[] friends, bool isMember)
    {
        return OwnersHouses
            .Where(house => HouseAccess.MayEnter(GuidHelper.GetPlayerGuid(playerId), friends, isMember, house))
            .Select(house => house.Id)
            .ToArray();
    }

    private static readonly FriendData[] OwnerAsFriend = [new() { Guid = GuidHelper.GetPlayerGuid(OwnerId) }];

    [TestMethod]
    public void TargetList_NonFriendSeesOnlyPublishedUnlockedHouses()
    {
        CollectionAssert.AreEqual(new ulong[] { 1 }, Listed(VisitorId, [], isMember: false));
        CollectionAssert.AreEqual(new ulong[] { 1, 4 }, Listed(VisitorId, [], isMember: true));
    }

    [TestMethod]
    public void TargetList_FriendAlsoSeesLockedAndUnpublishedHouses()
    {
        CollectionAssert.AreEqual(new ulong[] { 1, 2, 3 }, Listed(VisitorId, OwnerAsFriend, isMember: false));
        CollectionAssert.AreEqual(new ulong[] { 1, 2, 3, 4 }, Listed(VisitorId, OwnerAsFriend, isMember: true));
    }

    [TestMethod]
    public void TargetList_OwnerSeesEveryHouse()
    {
        CollectionAssert.AreEqual(new ulong[] { 1, 2, 3, 4 }, Listed(OwnerId, [], isMember: false));
    }

    /// <summary>
    /// The start of the event from the 2026-10-12 playtest log, sent every 30 seconds while the directory is open.
    /// </summary>
    private static byte[] BrowserShown(byte[] tail) =>
    [
        .. Convert.FromHexString("8E0007000000711D823E1A000000"),
        .. Encoding.ASCII.GetBytes("HousingRating_BrowserShown"),
        .. tail
    ];

    [TestMethod]
    public void LuaMetrics_ReadsTheDirectoryEvent()
    {
        Assert.IsTrue(PacketClientLuaMetrics.TryDeserialize(BrowserShown([1, 2, 3]), out var metric));
        Assert.AreEqual(7, metric.Unknown1);
        Assert.AreEqual(0x3E821D71, metric.Unknown2);
        Assert.AreEqual("HousingRating_BrowserShown", metric.Name);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, metric.Data);
    }

    /// <summary>
    /// <c>PacketClientLuaMetricsHandler</c> passes an event to Take Me There only when this read succeeds.
    /// </summary>
    [TestMethod]
    public void LuaMetrics_DirectoryEventDoesNotReachTakeMeThere()
    {
        Assert.IsFalse(TakeMeThereRequestPacket.TryDeserialize(BrowserShown([]), out _));
        Assert.IsFalse(TakeMeThereRequestPacket.TryDeserialize(BrowserShown([1, 2, 3, 4, 5, 6, 7, 8]), out _));
    }

    [TestMethod]
    public void LuaMetrics_RefusesAnotherOpCode()
    {
        var wire = BrowserShown([]);
        wire[0] = 0x8F;

        Assert.IsFalse(PacketClientLuaMetrics.TryDeserialize(wire, out _));
    }
}
