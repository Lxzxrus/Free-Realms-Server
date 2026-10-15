using System;
using System.Numerics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The fishing packets (opcode 138, task 33). The layouts are edenfps' (upstream PR #31), reverse-engineered from the
/// client and played in game; these tests pin them so a refactor can't drift from what the client reads.
/// </summary>
[TestClass]
public sealed class FishingPacketTests
{
    private const ulong Guid = 0x0102030405060708;

    private static readonly byte[] Header138 = [0x8A, 0x00];

    private static void AssertHeader(byte[] wire, short subOpCode)
    {
        Assert.AreEqual(BaseFishingPacket.OpCode, BitConverter.ToInt16(wire, 0));
        Assert.AreEqual(subOpCode, BitConverter.ToInt16(wire, 2));
        CollectionAssert.AreEqual(Header138, wire[..2]);
    }

    private static byte[] Bump(byte[] wire, int offset)
    {
        var copy = (byte[])wire.Clone();
        copy[offset]++;
        return copy;
    }

    [TestMethod]
    public void CastAnimRequest_RoundTrips_AndKeepsToItsOwnOpCodes()
    {
        var wire = new FishingPacketCastAnimRequest { Guid = Guid, UnknownInt = 0x41A00000, Position = new Vector4(1, 2, 3, 4) }.Serialize();

        AssertHeader(wire, 5);
        Assert.AreEqual(4 + 8 + 4 + 16, wire.Length);

        Assert.IsTrue(FishingPacketCastAnimRequest.TryDeserialize(wire, out var packet));
        Assert.AreEqual(Guid, packet.Guid);
        Assert.AreEqual(0x41A00000, packet.UnknownInt);
        Assert.AreEqual(new Vector4(1, 2, 3, 4), packet.Position);

        Assert.IsFalse(FishingPacketCastAnimRequest.TryDeserialize(Bump(wire, 0), out _), "Another opcode.");
        Assert.IsFalse(FishingPacketCastAnimRequest.TryDeserialize(Bump(wire, 2), out _), "Another sub-opcode.");
        Assert.IsFalse(FishingPacketCastAnimRequest.TryDeserialize(wire[..^1], out _), "Cut short.");
    }

    [TestMethod]
    public void CastRequest_RoundTrips_AndKeepsToItsOwnOpCodes()
    {
        var wire = new FishingPacketCastRequest { Guid = Guid, Position = new Vector4(449, -65, 365, 1), Flag = true }.Serialize();

        AssertHeader(wire, 6);
        Assert.AreEqual(4 + 8 + 16 + 1, wire.Length);

        Assert.IsTrue(FishingPacketCastRequest.TryDeserialize(wire, out var packet));
        Assert.AreEqual(Guid, packet.Guid);
        Assert.AreEqual(new Vector4(449, -65, 365, 1), packet.Position);
        Assert.IsTrue(packet.Flag);

        Assert.IsFalse(FishingPacketCastRequest.TryDeserialize(Bump(wire, 0), out _));
        Assert.IsFalse(FishingPacketCastRequest.TryDeserialize(Bump(wire, 2), out _));
        Assert.IsFalse(FishingPacketCastRequest.TryDeserialize(wire[..^1], out _));
    }

    [TestMethod]
    public void ReelInRequest_RoundTrips_AndKeepsToItsOwnOpCodes()
    {
        var wire = new FishingPacketReelInRequest { Guid = Guid, Flag = true }.Serialize();

        AssertHeader(wire, 7);
        Assert.AreEqual(4 + 8 + 1, wire.Length);

        Assert.IsTrue(FishingPacketReelInRequest.TryDeserialize(wire, out var packet));
        Assert.AreEqual(Guid, packet.Guid);
        Assert.IsTrue(packet.Flag);

        Assert.IsFalse(FishingPacketReelInRequest.TryDeserialize(Bump(wire, 0), out _));
        Assert.IsFalse(FishingPacketReelInRequest.TryDeserialize(Bump(wire, 2), out _));
        Assert.IsFalse(FishingPacketReelInRequest.TryDeserialize(wire[..^1], out _));
    }

    [TestMethod]
    public void SpecialRequest_RoundTrips_AndKeepsToItsOwnOpCodes()
    {
        var wire = new FishingPacketSpecialRequest { Guid = Guid, Data = 99 }.Serialize();

        AssertHeader(wire, 15);
        Assert.AreEqual(4 + 8 + 8, wire.Length);

        Assert.IsTrue(FishingPacketSpecialRequest.TryDeserialize(wire, out var packet));
        Assert.AreEqual(Guid, packet.Guid);
        Assert.AreEqual(99UL, packet.Data);

        Assert.IsFalse(FishingPacketSpecialRequest.TryDeserialize(Bump(wire, 0), out _));
        Assert.IsFalse(FishingPacketSpecialRequest.TryDeserialize(Bump(wire, 2), out _));
    }

    [TestMethod]
    public void SpawnBobber_IsGuidModelPositionAndDirection()
    {
        var wire = new FishingPacketSpawnProxiedFishingBobber
        {
            Guid = Guid,
            Unknown = 1063,
            Position = new Vector4(1, 2, 3, 1),
            Rotation = new Vector4(0, 0, 1, 0)
        }.Serialize();

        AssertHeader(wire, 8);
        Assert.AreEqual(4 + 8 + 4 + 16 + 16, wire.Length);

        var reader = new PacketReader(wire[4..]);
        Assert.IsTrue(reader.TryRead(out ulong guid) && guid == Guid);
        Assert.IsTrue(reader.TryRead(out int model) && model == 1063);
        Assert.IsTrue(reader.TryRead(out Vector4 position) && position == new Vector4(1, 2, 3, 1));
        Assert.IsTrue(reader.TryRead(out Vector4 direction) && direction == new Vector4(0, 0, 1, 0));
    }

    [TestMethod]
    public void UpdateBobber_IsGuidFishHookedInterested()
    {
        var wire = new FishingPacketUpdateProxiedFishingBobber { Guid = Guid, Unknown = 2, Flag1 = true, Flag2 = false }.Serialize();

        AssertHeader(wire, 10);
        CollectionAssert.AreEqual(new byte[] { 2, 0, 0, 0, 1, 0 }, wire[12..]);
        Assert.AreEqual(4 + 8 + 4 + 1 + 1, wire.Length);
    }

    [TestMethod]
    public void FishingResult_IsInTheClientsOrder_WithSizeLast()
    {
        var wire = new FishingPacketFishingResult
        {
            Guid = Guid,
            ResultType = FishingPacketFishingResult.ResultScoredCatch,
            Caught = false,
            FishId = 99924,
            Unknown1 = 4276,
            Unknown2 = 3.5f,
            Unknown3 = 2,
            Unknown4 = 3.5f,
            Unknown5 = 1,
            Unknown6 = 1,
            Unknown7 = 20,
            Unknown8 = 1670,
            Unknown9 = 15880,
            Unknown12 = 2
        }.Serialize();

        AssertHeader(wire, 14);

        var reader = new PacketReader(wire[4..]);
        Assert.IsTrue(reader.TryRead(out ulong guid) && guid == Guid);
        Assert.IsTrue(reader.TryRead(out int resultType) && resultType == 5);
        Assert.IsTrue(reader.TryRead(out byte caught) && caught == 0);
        Assert.IsTrue(reader.TryRead(out int nameId) && nameId == 99924);
        Assert.IsTrue(reader.TryRead(out int iconId) && iconId == 4276);
        Assert.IsTrue(reader.TryRead(out string name) && name == string.Empty);
        Assert.IsTrue(reader.TryRead(out float weight) && weight == 3.5f);
        Assert.IsTrue(reader.TryRead(out int size) && size == 2);
        Assert.IsTrue(reader.TryRead(out float time) && time == 3.5f);
        Assert.IsTrue(reader.TryRead(out int difficulty) && difficulty == 1);
        Assert.IsTrue(reader.TryRead(out int rarity) && rarity == 1);
        Assert.IsTrue(reader.TryRead(out int score) && score == 20);
        Assert.IsTrue(reader.TryRead(out int heldModel) && heldModel == 1670);
        Assert.IsTrue(reader.TryRead(out string tint) && tint == string.Empty);
        Assert.IsTrue(reader.TryRead(out string texture) && texture == string.Empty);
        Assert.IsTrue(reader.TryRead(out int effect) && effect == 15880);
        Assert.IsTrue(reader.TryRead(out int unknown10) && unknown10 == 0);
        Assert.IsTrue(reader.TryRead(out byte unknown11) && unknown11 == 0);
        Assert.IsTrue(reader.TryRead(out int heldSize) && heldSize == 2);
        Assert.AreEqual(0, reader.RemainingLength);
    }

    [TestMethod]
    public void UnderwaterFish_SpeedsAreFloats()
    {
        using var writer = new PacketWriter();

        new UnderwaterFishSpawnInfo { Unknown = 2, ModelId = 1671, Unknown5 = 1, Unknown7 = true, Unknown8 = 1.5f, Unknown18 = 1.2f }.Serialize(writer);

        var wire = writer.Buffer;

        // id, model, two empty strings, size, unknown, catchable, eleven floats.
        Assert.AreEqual(4 + 4 + 4 + 4 + 4 + 4 + 1 + 11 * 4, wire.Length);
        Assert.AreEqual(1, wire[24], "Catchable.");
        Assert.AreEqual(1.5f, BitConverter.ToSingle(wire, 25), "An int 1 here is about 0 as a float, and freezes the fish.");
        Assert.AreEqual(1.2f, BitConverter.ToSingle(wire, wire.Length - 4));
    }

    [TestMethod]
    public void RegisterPlayerResponse_IsPlayerConfigZoneConfigModelsFish()
    {
        var response = new FishingPacketRegisterPlayerResponse
        {
            FishingPlayerConfig = new FishingPlayerConfig { Unknown = 4, Unknown2 = 3f, Unknown3 = 20f, Unknown10 = 6f, Unknown16 = 1 },
            FishingZoneConfig = new FishingZoneConfig { Unknown = "sg_fishing_medpond", Unknown3 = 68f, Unknown6 = -64.5f },
            FishModelIds = [1670, 1671, 1672],
            ClientFishEntries = [new ClientFishEntryInfo { Type = 68049, NameId = 99924, IconId = 4276, FishCatchable = true }]
        };

        var wire = response.Serialize();

        AssertHeader(wire, 3);

        var reader = new PacketReader(wire[4..]);

        // Player config: an int, the cast range as floats, six ints, the camera's four floats, three ints.
        Assert.IsTrue(reader.TryRead(out int unknown) && unknown == 4);
        Assert.IsTrue(reader.TryRead(out float minCast) && minCast == 3f);
        Assert.IsTrue(reader.TryRead(out float maxCast) && maxCast == 20f);
        Assert.IsTrue(reader.TryReadExact(6 * 4, out _));
        Assert.IsTrue(reader.TryRead(out float cameraDistance) && cameraDistance == 6f);
        Assert.IsTrue(reader.TryReadExact(3 * 4 + 2 * 4, out _));
        Assert.IsTrue(reader.TryRead(out int unknown16) && unknown16 == 1);

        // Zone config: the scene, an int, the bed's X, two ints, the water's height.
        Assert.IsTrue(reader.TryRead(out string scene) && scene == "sg_fishing_medpond");
        Assert.IsTrue(reader.TryRead(out int _));
        Assert.IsTrue(reader.TryRead(out float bedX) && bedX == 68f);
        Assert.IsTrue(reader.TryReadExact(2 * 4, out _));
        Assert.IsTrue(reader.TryRead(out float waterY) && waterY == -64.5f);

        Assert.IsTrue(reader.TryRead(out int modelCount) && modelCount == 3);
        Assert.IsTrue(reader.TryReadExact(3 * 4, out _));

        Assert.IsTrue(reader.TryRead(out int fishCount) && fishCount == 1);
        Assert.IsTrue(reader.TryRead(out int type) && type == 68049);
        Assert.IsTrue(reader.TryRead(out int fishName) && fishName == 99924);
        Assert.IsTrue(reader.TryRead(out int fishIcon) && fishIcon == 4276);

        // Two bools, the lure, an empty string, an int, catchable, an int.
        Assert.AreEqual(1 + 1 + 4 + 4 + 4 + 1 + 4, reader.RemainingLength);
    }

    [TestMethod]
    public void SpawnFishRun_IsFlagDecoyTextureFish()
    {
        var wire = new FishingPacketSpawnFishRun
        {
            Unknown = true,
            Unknown2 = 69,
            UnderwaterFishSpawns = [new UnderwaterFishSpawnInfo(), new UnderwaterFishSpawnInfo()]
        }.Serialize();

        AssertHeader(wire, 18);
        Assert.AreEqual(1, wire[4]);
        Assert.AreEqual(69, BitConverter.ToInt32(wire, 5));
        Assert.AreEqual(0, BitConverter.ToInt32(wire, 9), "An empty texture.");
        Assert.AreEqual(2, BitConverter.ToInt32(wire, 13));
    }

    [TestMethod]
    public void UpdateCharacterState_IsPlayerUpdate20_GuidState()
    {
        var wire = new PlayerUpdatePacketUpdateCharacterState { Guid = Guid, State = PlayerUpdatePacketUpdateCharacterState.FishingState }.Serialize();

        Assert.AreEqual(BasePlayerUpdatePacket.OpCode, BitConverter.ToInt16(wire, 0));
        Assert.AreEqual(20, BitConverter.ToInt16(wire, 2));
        Assert.AreEqual(Guid, BitConverter.ToUInt64(wire, 4));
        Assert.AreEqual(0x400000, BitConverter.ToInt32(wire, 12));
        Assert.AreEqual(16, wire.Length);
    }

    [TestMethod]
    public void EverySubOpCode_IsTheClientsNameForIt()
    {
        // The opcode table holds the client's own names for the fishing family.
        ISerializablePacket[] packets =
        [
            new FishingPacketUpdateData(), new FishingPacketRegisterPlayerResponse(), new FishingPacketFishInfoUpdate(),
            new FishingPacketCastAnimRequest(), new FishingPacketCastRequest(), new FishingPacketReelInRequest(),
            new FishingPacketSpawnProxiedFishingBobber(), new FishingPacketUpdateProxiedFishingBobber(),
            new FishingPacketSpawnProxiedFishingSchool(), new FishingPacketDespawnProxiedFishingSchool(),
            new FishingPacketUpdateProxiedFishingSchool(), new FishingPacketFishingResult(),
            new FishingPacketSpecialRequest(), new FishingPacketSpecialResponse(), new FishingPacketSpawnFishRun(),
            new PlayerUpdatePacketUpdateCharacterState()
        ];

        foreach (var packet in packets)
        {
            var reader = new PacketReader(packet.Serialize());
            Assert.AreEqual(packet.GetType().Name, reader.ReadTunneledPacketName());
        }
    }

    [TestMethod]
    public void TheServerOnlyPackets_HaveTheirSubOpCodes()
    {
        AssertHeader(new FishingPacketUpdateData().Serialize(), 1);
        AssertHeader(new FishingPacketRegisterPlayerResponse().Serialize(), 3);
        AssertHeader(new FishingPacketFishInfoUpdate().Serialize(), 4);
        AssertHeader(new FishingPacketSpawnProxiedFishingSchool().Serialize(), 11);
        AssertHeader(new FishingPacketDespawnProxiedFishingSchool().Serialize(), 12);
        AssertHeader(new FishingPacketUpdateProxiedFishingSchool().Serialize(), 13);
        AssertHeader(new FishingPacketSpecialResponse().Serialize(), 16);

        Assert.AreEqual(4 + 8 + 16, new FishingPacketUpdateData().Serialize().Length);
        Assert.AreEqual(4 + 8 + 4 + 1, new FishingPacketSpecialResponse().Serialize().Length);
    }
}
