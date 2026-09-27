using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Packet;

namespace Sanctuary.Game.Tests;

/// <summary>
/// About 30 of upstream OSFR's packets guarded their opcode with <c>!TryRead(...) &amp;&amp; opCode != OpCode</c>,
/// which only fails when the read fails, so any opcode or sub-opcode was accepted. Each row is a packet family
/// whose guard was changed to ||, with a wire the family accepts. Every row must accept its own wire and refuse
/// the same wire with the opcode, or the sub-opcode, changed.
/// </summary>
[TestClass]
public sealed class PacketOpCodeGuardTests
{
    /// <param name="TryRead">Runs the guarded read on a wire.</param>
    /// <param name="Wire">A wire the packet accepts.</param>
    /// <param name="OpCodeSize">Bytes of opcode at the start of the wire.</param>
    /// <param name="HasSubOpCode">Whether a sub-opcode follows the opcode.</param>
    private sealed record Case(Func<byte[], bool> TryRead, byte[] Wire, int OpCodeSize, bool HasSubOpCode);

    private static byte[] Wire(Action<PacketWriter> write)
    {
        using var writer = new PacketWriter();

        write(writer);

        return writer.Buffer;
    }

    private delegate bool BaseTryRead(ref PacketReader reader);

    /// <summary>
    /// A family's base class reads the opcode and sub-opcode; the fields after them belong to each packet.
    /// </summary>
    private static Case Family(BaseTryRead tryRead, Action<PacketWriter> writeHeader) => new(
        data =>
        {
            var reader = new PacketReader(data);

            return tryRead(ref reader) && reader.RemainingLength == 0;
        },
        Wire(writeHeader),
        sizeof(short),
        true);

    private static Case Standalone<T>(TryDeserializeFunc<T> tryDeserialize, byte[] wire, int opCodeSize) => new(
        data => tryDeserialize(data, out _),
        wire,
        opCodeSize,
        false);

    private delegate bool TryDeserializeFunc<T>(ReadOnlySpan<byte> data, out T value);

    private static readonly Dictionary<string, Case> Cases = new()
    {
        // Families: base class TryRead, opcode then sub-opcode.

        [nameof(BaseAbilityPacket)] = Family(new BaseAbilityPacket(AbilityPacketClientRequestStartAbility.OpCode).TryRead,
            w => { w.Write(BaseAbilityPacket.OpCode); w.Write(AbilityPacketClientRequestStartAbility.OpCode); }),

        [nameof(BaseChatPacket)] = Family(new BaseChatPacket(PacketChat.OpCode).TryRead,
            w => { w.Write(BaseChatPacket.OpCode); w.Write(PacketChat.OpCode); }),

        [nameof(BaseCoinStorePacket)] = Family(new BaseCoinStorePacket(CoinStoreItemDefinitionsRequestPacket.OpCode).TryRead,
            w => { w.Write(BaseCoinStorePacket.OpCode); w.Write(CoinStoreItemDefinitionsRequestPacket.OpCode); }),

        [nameof(BaseCommandPacket)] = Family(new BaseCommandPacket(CommandPacketInteractRequest.OpCode).TryRead,
            w => { w.Write(BaseCommandPacket.OpCode); w.Write(CommandPacketInteractRequest.OpCode); }),

        [nameof(BaseFotomatPacket)] = Family(new BaseFotomatPacket(PacketPortraitDataRequest.OpCode).TryRead,
            w => { w.Write(BaseFotomatPacket.OpCode); w.Write(PacketPortraitDataRequest.OpCode); }),

        [nameof(BaseFriendPacket)] = Family(new BaseFriendPacket(FriendAddPacket.OpCode).TryRead,
            w => { w.Write(BaseFriendPacket.OpCode); w.Write(FriendAddPacket.OpCode); }),

        [nameof(BaseIgnorePacket)] = Family(new BaseIgnorePacket(IgnoreAddPacket.OpCode).TryRead,
            w => { w.Write(BaseIgnorePacket.OpCode); w.Write(IgnoreAddPacket.OpCode); }),

        [nameof(BaseInspectPacket)] = Family(new BaseInspectPacket(StopInspectPacket.OpCode).TryRead,
            w => { w.Write(BaseInspectPacket.OpCode); w.Write(StopInspectPacket.OpCode); }),

        [nameof(BaseInventoryPacket)] = Family(new BaseInventoryPacket(InventoryPacketEquipByGuid.OpCode).TryRead,
            w => { w.Write(BaseInventoryPacket.OpCode); w.Write(InventoryPacketEquipByGuid.OpCode); }),

        [nameof(BaseLobbyGameDefinitionPacket)] = Family(new BaseLobbyGameDefinitionPacket(LobbyGameDefinitionPacketDefinitionsRequest.OpCode).TryRead,
            w => { w.Write(BaseLobbyGameDefinitionPacket.OpCode); w.Write(LobbyGameDefinitionPacketDefinitionsRequest.OpCode); }),

        [nameof(BaseNameChangePacket)] = Family(new BaseNameChangePacket(CheckNamePacket.OpCode).TryRead,
            w => { w.Write(BaseNameChangePacket.OpCode); w.Write(CheckNamePacket.OpCode); }),

        // The title family's sub-opcode is an int on the wire.
        [nameof(BasePlayerTitlePacket)] = Family(new BasePlayerTitlePacket(PlayerTitleRequestSelectPacket.OpCode).TryRead,
            w => { w.Write(BasePlayerTitlePacket.OpCode); w.Write((int)PlayerTitleRequestSelectPacket.OpCode); }),

        [nameof(BasePlayerUpdatePacket)] = Family(new BasePlayerUpdatePacket(PlayerUpdatePacketItemDefinitionRequest.OpCode).TryRead,
            w => { w.Write(BasePlayerUpdatePacket.OpCode); w.Write(PlayerUpdatePacketItemDefinitionRequest.OpCode); }),

        [nameof(BaseQuickChatPacket)] = Family(new BaseQuickChatPacket(QuickChatSendTellPacket.OpCode).TryRead,
            w => { w.Write(BaseQuickChatPacket.OpCode); w.Write(QuickChatSendTellPacket.OpCode); }),

        [nameof(MountBasePacket)] = Family(new MountBasePacket(PacketMountSpawn.OpCode).TryRead,
            w => { w.Write(MountBasePacket.OpCode); w.Write(PacketMountSpawn.OpCode); }),

        [nameof(PacketBaseInGamePurchase)] = Family(new PacketBaseInGamePurchase(PacketInGamePurchasePreviewOrder.OpCode).TryRead,
            w => { w.Write(PacketBaseInGamePurchase.OpCode); w.Write(PacketInGamePurchasePreviewOrder.OpCode); }),

        [nameof(WallOfDataBasePacket)] = Family(new WallOfDataBasePacket(WallOfDataUIEventPacket.OpCode).TryRead,
            w => { w.Write(WallOfDataBasePacket.OpCode); w.Write(WallOfDataUIEventPacket.OpCode); }),

        // Packets with an opcode and no family: the client gateway, login server and gateway-to-login links.

        [nameof(PacketLogin)] = Standalone<PacketLogin>(PacketLogin.TryDeserialize,
            Wire(w => { w.Write(PacketLogin.OpCode); w.Write("ticket"); w.Write(1UL); w.Write("version"); w.Write("unknown"); }),
            sizeof(short)),

        [nameof(PacketTunneledClientPacket)] = Standalone<PacketTunneledClientPacket>(PacketTunneledClientPacket.TryDeserialize,
            new PacketTunneledClientPacket { Payload = [1, 2, 3] }.Serialize(),
            sizeof(short)),

        [nameof(PacketTunneledClientWorldPacket)] = Standalone<PacketTunneledClientWorldPacket>(PacketTunneledClientWorldPacket.TryDeserialize,
            new PacketTunneledClientWorldPacket { Payload = [1, 2, 3] }.Serialize(),
            sizeof(short)),

        [nameof(GatewayLoginRequest)] = Standalone<GatewayLoginRequest>(GatewayLoginRequest.TryDeserialize,
            new GatewayLoginRequest { Challenge = "challenge", ServerAddress = "127.0.0.1" }.Serialize(),
            sizeof(byte)),

        [nameof(GatewayLoginReply)] = Standalone<GatewayLoginReply>(GatewayLoginReply.TryDeserialize,
            new GatewayLoginReply { Success = true }.Serialize(),
            sizeof(byte)),

        [nameof(GatewayCharacterLogin)] = Standalone<GatewayCharacterLogin>(GatewayCharacterLogin.TryDeserialize,
            new GatewayCharacterLogin { Id = 1 }.Serialize(),
            sizeof(byte)),

        [nameof(GatewayCharacterLogout)] = Standalone<GatewayCharacterLogout>(GatewayCharacterLogout.TryDeserialize,
            new GatewayCharacterLogout { id = 1 }.Serialize(),
            sizeof(byte)),

        [nameof(LoginRequest)] = Standalone<LoginRequest>(LoginRequest.TryDeserialize,
            Wire(w => { w.Write(LoginRequest.OpCode); w.Write("session"); w.Write("fingerprint"); }),
            sizeof(byte)),

        [nameof(CharacterCreateRequest)] = Standalone<CharacterCreateRequest>(CharacterCreateRequest.TryDeserialize,
            Wire(w => { w.Write(CharacterCreateRequest.OpCode); w.Write(1); w.WritePayload([1, 2, 3]); }),
            sizeof(byte)),

        [nameof(CharacterLoginRequest)] = Standalone<CharacterLoginRequest>(CharacterLoginRequest.TryDeserialize,
            Wire(w => { w.Write(CharacterLoginRequest.OpCode); w.Write(1UL); w.Write(1); w.WritePayload([1, 2, 3]); }),
            sizeof(byte)),

        [nameof(CharacterDeleteRequest)] = Standalone<CharacterDeleteRequest>(CharacterDeleteRequest.TryDeserialize,
            Wire(w => { w.Write(CharacterDeleteRequest.OpCode); w.Write(1UL); }),
            sizeof(byte)),

        [nameof(TunnelAppPacketClientToServer)] = Standalone<TunnelAppPacketClientToServer>(TunnelAppPacketClientToServer.TryDeserialize,
            new TunnelAppPacketClientToServer { ServerId = 1, Payload = [1, 2, 3] }.Serialize(),
            sizeof(byte)),

        [nameof(PacketCheckNameRequest)] = Standalone<PacketCheckNameRequest>(PacketCheckNameRequest.TryDeserialize,
            Wire(w => { w.Write(PacketCheckNameRequest.OpCode); w.Write("First"); w.Write("Last"); w.Write("name"); w.Write("prefix"); w.Write("suffix"); }),
            sizeof(short)),

        [nameof(PacketClientInitializationDetails)] = Standalone<PacketClientInitializationDetails>(PacketClientInitializationDetails.TryDeserialize,
            Wire(w => { w.Write(PacketClientInitializationDetails.OpCode); w.Write(-300); }),
            sizeof(short)),

        [nameof(PacketClientLog)] = Standalone<PacketClientLog>(PacketClientLog.TryDeserialize,
            Wire(w => { w.Write(PacketClientLog.OpCode); w.Write("client.log"); w.Write("message"); }),
            sizeof(short)),

        // ClientMetrics is 18 ulongs and one int.
        [nameof(PacketClientMetrics)] = Standalone<PacketClientMetrics>(PacketClientMetrics.TryDeserialize,
            Wire(w => { w.Write(PacketClientMetrics.OpCode); w.Write(new byte[18 * sizeof(ulong) + sizeof(int)]); }),
            sizeof(short)),

        [nameof(PacketGameTimeSync)] = Standalone<PacketGameTimeSync>(PacketGameTimeSync.TryDeserialize,
            new PacketGameTimeSync { Time = 1, ServerRate = 1 }.Serialize(),
            sizeof(short)),

        [nameof(PacketSetLocale)] = Standalone<PacketSetLocale>(PacketSetLocale.TryDeserialize,
            Wire(w => { w.Write(PacketSetLocale.OpCode); w.Write("en_US"); }),
            sizeof(short)),

        [nameof(PacketWorldTeleportRequest)] = Standalone<PacketWorldTeleportRequest>(PacketWorldTeleportRequest.TryDeserialize,
            Wire(w => { w.Write(PacketWorldTeleportRequest.OpCode); w.Write(1UL); }),
            sizeof(short)),

        [nameof(PacketZoneSafeTeleportRequest)] = Standalone<PacketZoneSafeTeleportRequest>(PacketZoneSafeTeleportRequest.TryDeserialize,
            Wire(w => w.Write(PacketZoneSafeTeleportRequest.OpCode)),
            sizeof(short)),

        [nameof(PacketZoneTeleportRequest)] = Standalone<PacketZoneTeleportRequest>(PacketZoneTeleportRequest.TryDeserialize,
            Wire(w => { w.Write(PacketZoneTeleportRequest.OpCode); w.Write(1); }),
            sizeof(short)),

        [nameof(PlayerUpdatePacketCameraUpdate)] = Standalone<PlayerUpdatePacketCameraUpdate>(PlayerUpdatePacketCameraUpdate.TryDeserialize,
            Wire(w => { w.Write(PlayerUpdatePacketCameraUpdate.OpCode); w.Write(Vector4.One, true); w.Write(Quaternion.Identity, true); }),
            sizeof(short)),

        [nameof(PlayerUpdatePacketJump)] = Standalone<PlayerUpdatePacketJump>(PlayerUpdatePacketJump.TryDeserialize,
            new PlayerUpdatePacketJump { Guid = 1, Position = Vector4.One, Rotation = Quaternion.Identity }.Serialize(),
            sizeof(short)),

        [nameof(PlayerUpdatePacketUpdatePosition)] = Standalone<PlayerUpdatePacketUpdatePosition>(PlayerUpdatePacketUpdatePosition.TryDeserialize,
            new PlayerUpdatePacketUpdatePosition { Guid = 1, Position = Vector4.One, Rotation = Quaternion.Identity }.Serialize(),
            sizeof(short)),
    };

    public static IEnumerable<object[]> AllCases => Cases.Keys.Select(name => new object[] { name });

    public static IEnumerable<object[]> FamilyCases => Cases.Where(x => x.Value.HasSubOpCode).Select(x => new object[] { x.Key });

    /// <summary>
    /// Adds one to the low byte of the little-endian value at <paramref name="offset"/>. No opcode or
    /// sub-opcode in the table has a low byte of 0xFF, so this always makes a different value.
    /// </summary>
    private static byte[] Bump(byte[] wire, int offset)
    {
        var copy = (byte[])wire.Clone();

        Assert.AreNotEqual(byte.MaxValue, copy[offset]);
        copy[offset]++;

        return copy;
    }

    [TestMethod]
    public void EveryChangedGuardHasARow()
    {
        // 17 families with a sub-opcode and 24 packets with only an opcode: 41 files, 58 guard lines.
        Assert.AreEqual(17, FamilyCases.Count());
        Assert.AreEqual(41, Cases.Count);
    }

    [TestMethod]
    [DynamicData(nameof(AllCases))]
    public void AcceptsItsOwnOpCode(string packet)
    {
        var testCase = Cases[packet];

        Assert.IsTrue(testCase.TryRead(testCase.Wire));
    }

    [TestMethod]
    [DynamicData(nameof(AllCases))]
    public void RefusesAnotherOpCode(string packet)
    {
        var testCase = Cases[packet];

        Assert.IsFalse(testCase.TryRead(Bump(testCase.Wire, 0)));
    }

    [TestMethod]
    [DynamicData(nameof(FamilyCases))]
    public void RefusesAnotherSubOpCode(string packet)
    {
        var testCase = Cases[packet];

        Assert.IsFalse(testCase.TryRead(Bump(testCase.Wire, testCase.OpCodeSize)));
    }
}
