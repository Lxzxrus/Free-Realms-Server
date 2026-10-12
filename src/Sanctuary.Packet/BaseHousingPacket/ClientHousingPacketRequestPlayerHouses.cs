using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Asks for a list of houses. Empty, it asks for the player's own. "Go to House" on another player sends that
/// player's guid and name.
/// </summary>
/// <remarks>
/// The targeted layout is from a 2014 capture of the live game (<c>ofrserver/Packets/p12.pcap</c> in the archived
/// <c>OpenSourceFreeRealms</c> repository): <c>7F 00 0E 00</c>, the guid <c>31 32 FA 75 76 AA 52 4B</c>, then
/// <c>09 00 00 00 "Megnesium"</c>, a friend of the capturing player. The live server answered with Megnesium's
/// houses.
/// </remarks>
public class ClientHousingPacketRequestPlayerHouses : BaseHousingPacket, ISerializablePacket, IDeserializable<ClientHousingPacketRequestPlayerHouses>
{
    public new const short OpCode = 14;

    /// <summary>Whose houses to list, or 0 for the player's own.</summary>
    public ulong PlayerGuid;

    /// <summary>The name the client shows for <see cref="PlayerGuid"/>. Not trusted.</summary>
    public string PlayerName = string.Empty;

    public bool IsTargeted => PlayerGuid != 0;

    public ClientHousingPacketRequestPlayerHouses() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        if (IsTargeted)
        {
            writer.Write(PlayerGuid);
            writer.Write(PlayerName);
        }

        return writer.Buffer;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ClientHousingPacketRequestPlayerHouses value)
    {
        value = new ClientHousingPacketRequestPlayerHouses();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (reader.RemainingLength == 0)
            return true;

        if (!reader.TryRead(out value.PlayerGuid))
            return false;

        if (!reader.TryRead(out value.PlayerName))
            return false;

        return reader.RemainingLength == 0;
    }
}
