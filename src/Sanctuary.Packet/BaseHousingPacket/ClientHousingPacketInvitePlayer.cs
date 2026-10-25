using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// "Invite to House" on a friend: <c>House.InviteToHouse(guid)</c> in the UI. The client sends it only while the player
/// is in a house.
/// </summary>
/// <remarks>
/// From FreeRealms.exe: the Lua binding at 0xc0beb0 calls 0xac4cd0, which builds the packet (constructor 0xaa7bf0,
/// sub-opcode 0x0F) and writes it at 0xac11f0: the guid it was given, then a second 8-byte field the client never sets.
/// </remarks>
public class ClientHousingPacketInvitePlayer : BaseHousingPacket, IDeserializable<ClientHousingPacketInvitePlayer>
{
    public new const short OpCode = 15;

    /// <summary>The player invited.</summary>
    public ulong PlayerGuid;

    /// <summary>Left at its default by the client.</summary>
    public ulong Unknown;

    public ClientHousingPacketInvitePlayer() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ClientHousingPacketInvitePlayer value)
    {
        value = new ClientHousingPacketInvitePlayer();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.PlayerGuid))
            return false;

        if (!reader.TryRead(out value.Unknown))
            return false;

        return reader.RemainingLength == 0;
    }
}
