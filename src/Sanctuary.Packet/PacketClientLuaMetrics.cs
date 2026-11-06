using System;
using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// The client's Lua UI metrics: opcode 142, the client's own name for it (see
/// <c>PacketReaderExtensions.ReadTunneledPacketName</c>), and pure telemetry: the client expects no reply.
/// </summary>
/// <remarks>
/// A list of (hash, name, value) counters, read as upstream OSFR reads it (PR #165). The client's
/// <c>Ui.IncrementClientLuaMetric(name)</c> only counts up a named counter and marks it changed; the changed counters
/// are sent later, together (the house directory's <c>HousingRating_BrowserShown</c> arrived every 30 seconds in the
/// 2026-10-12 playtest, starting <c>8E 00 07 00 00 00 71 1D 82 3E 1A 00 00 00</c>: seven counters, then the first
/// one's hash and name).
/// </remarks>
public class PacketClientLuaMetrics : IDeserializable<PacketClientLuaMetrics>
{
    public const short OpCode = 142;

    public class Metric : IDeserializableType
    {
        public uint Hash;
        public string Name = string.Empty;
        public int Value;

        public bool TryRead(ref PacketReader reader)
        {
            return reader.TryRead(out Hash)
                && reader.TryRead(out Name)
                && reader.TryRead(out Value);
        }
    }

    public List<Metric> Metrics = new();

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out PacketClientLuaMetrics value)
    {
        value = new PacketClientLuaMetrics();

        var reader = new PacketReader(data);

        if (!reader.TryRead(out short opCode) || opCode != OpCode)
            return false;

        if (!reader.TryReadList(out value.Metrics))
            return false;

        return reader.RemainingLength == 0;
    }
}
