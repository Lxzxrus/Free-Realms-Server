using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// A metrics event from the client's Lua UI. The client's own name for opcode 142 (see
/// <c>PacketReaderExtensions.ReadTunneledPacketName</c>), and pure telemetry: the client expects no reply.
/// </summary>
/// <remarks>
/// Every event starts with two ints and the event's name. What follows the name depends on the event, so it is kept
/// as raw bytes. The house directory sends <c>HousingRating_BrowserShown</c> every 30 seconds while it is open
/// (2026-10-12 playtest), starting <c>8E 00 07 00 00 00 71 1D 82 3E 1A 00 00 00</c>. The "Take Me There" event that
/// <see cref="TakeMeThereRequestPacket"/> reads is another of these events.
/// </remarks>
public class PacketClientLuaMetrics : IDeserializable<PacketClientLuaMetrics>
{
    public const short OpCode = 142;

    public int Unknown1;
    public int Unknown2;
    public string Name = string.Empty;

    /// <summary>The bytes after <see cref="Name"/>, whose layout depends on the event.</summary>
    public byte[] Data = [];

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out PacketClientLuaMetrics value)
    {
        value = new PacketClientLuaMetrics();

        var reader = new PacketReader(data);

        if (!reader.TryRead(out short opCode) || opCode != OpCode)
            return false;

        if (!reader.TryRead(out value.Unknown1))
            return false;

        if (!reader.TryRead(out value.Unknown2))
            return false;

        if (!reader.TryRead(out value.Name))
            return false;

        value.Data = reader.RemainingSpan.ToArray();

        return true;
    }
}
