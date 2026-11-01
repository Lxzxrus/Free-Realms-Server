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

    // The accounts the owner and the visitor play on.
    private const ulong OwnersAccount = 1;
    private const ulong VisitorsAccount = 2;

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

    private static ulong[] Listed(ulong playerUserId, FriendData[] friends, bool isMember)
    {
        return OwnersHouses
            .Where(house => HouseAccess.MayEnter(playerUserId, friends, isMember, house, OwnersAccount))
            .Select(house => house.Id)
            .ToArray();
    }

    private static readonly FriendData[] OwnerAsFriend = [new() { Guid = GuidHelper.GetPlayerGuid(OwnerId) }];

    [TestMethod]
    public void TargetList_NonFriendSeesOnlyPublishedUnlockedHouses()
    {
        CollectionAssert.AreEqual(new ulong[] { 1 }, Listed(VisitorsAccount, [], isMember: false));
        CollectionAssert.AreEqual(new ulong[] { 1, 4 }, Listed(VisitorsAccount, [], isMember: true));
    }

    [TestMethod]
    public void TargetList_FriendAlsoSeesLockedAndUnpublishedHouses()
    {
        CollectionAssert.AreEqual(new ulong[] { 1, 2, 3 }, Listed(VisitorsAccount, OwnerAsFriend, isMember: false));
        CollectionAssert.AreEqual(new ulong[] { 1, 2, 3, 4 }, Listed(VisitorsAccount, OwnerAsFriend, isMember: true));
    }

    [TestMethod]
    public void TargetList_OwnerSeesEveryHouse()
    {
        CollectionAssert.AreEqual(new ulong[] { 1, 2, 3, 4 }, Listed(OwnersAccount, [], isMember: false));
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

    /// <summary>
    /// Upstream OSFR reads opcode 142 as a list of (hash, name, value) metrics. The playtest capture's head fits it: a
    /// count of 7, then the first metric's hash and name. One whole metric reads back field by field.
    /// </summary>
    [TestMethod]
    public void LuaMetrics_ReadsTheDirectoryEvent()
    {
        byte[] wire =
        [
            .. Convert.FromHexString("8E0001000000711D823E1A000000"),
            .. Encoding.ASCII.GetBytes("HousingRating_BrowserShown"),
            .. BitConverter.GetBytes(5)
        ];

        Assert.IsTrue(PacketClientLuaMetrics.TryDeserialize(wire, out var packet));
        Assert.AreEqual(1, packet.Metrics.Count);
        Assert.AreEqual(0x3E821D71u, packet.Metrics[0].Hash);
        Assert.AreEqual("HousingRating_BrowserShown", packet.Metrics[0].Name);
        Assert.AreEqual(5, packet.Metrics[0].Value);

        // The captured packet says seven metrics follow; its head alone is refused, not misread.
        Assert.IsFalse(PacketClientLuaMetrics.TryDeserialize(BrowserShown([]), out _));
    }

    /// <summary>A metrics report holding these counters (hash 0: the server doesn't read it).</summary>
    private static PacketClientLuaMetrics Report(params (string Name, int Value)[] metrics)
    {
        var wire = Wire(writer =>
        {
            writer.Write(PacketClientLuaMetrics.OpCode);
            writer.Write(metrics.Length);

            foreach (var (name, value) in metrics)
            {
                writer.Write(0u);
                writer.Write(name);
                writer.Write(value);
            }
        });

        Assert.IsTrue(PacketClientLuaMetrics.TryDeserialize(wire, out var packet));
        return packet;
    }

    /// <summary>
    /// <c>PacketClientLuaMetricsHandler</c> passes a report on to Evergrove's Take Me There only when it holds the
    /// TakeMeThere counter alone, the shape answered before the list was read; the directory's counter never reaches it.
    /// </summary>
    [TestMethod]
    public void LuaMetrics_OnlyTheTakeMeThereCounterReachesTakeMeThere()
    {
        Assert.IsTrue(TakeMeThereRequestPacket.TryFrom(Report(("TakeMeThere", 3)), out var request));
        Assert.AreEqual(3, request.RequestId);

        Assert.IsFalse(TakeMeThereRequestPacket.TryFrom(Report(("HousingRating_BrowserShown", 1)), out _));
        Assert.IsFalse(TakeMeThereRequestPacket.TryFrom(Report(("HousingRating_BrowserShown", 1), ("TakeMeThere", 1)), out _));
        Assert.IsFalse(TakeMeThereRequestPacket.TryFrom(Report(), out _));
    }

    [TestMethod]
    public void LuaMetrics_RefusesAnotherOpCode()
    {
        var wire = BrowserShown([]);
        wire[0] = 0x8F;

        Assert.IsFalse(PacketClientLuaMetrics.TryDeserialize(wire, out _));
    }
}
