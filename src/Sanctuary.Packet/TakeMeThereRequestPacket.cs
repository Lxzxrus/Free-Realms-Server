using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

// Fires when the client's "Take Me There" quest-tracker button is clicked. Wire shape confirmed
// against real captured traffic; Unknown1 and Unknown2 are constant across every capture, but
// their meaning is not verified (not a CRC32/FNV1a of the action name).
//
// Opcode 142 is not this packet's own: it is PacketClientLuaMetrics, which every Lua UI metrics
// event uses (the house directory sends "HousingRating_BrowserShown" on it every 30 seconds).
// Which event name Take Me There sends is not recorded, so it is recognised by its shape: a name
// followed by exactly one int. PacketClientLuaMetricsHandler tries this read first and drops
// every event of any other shape.
public class TakeMeThereRequestPacket : IDeserializable<TakeMeThereRequestPacket>
{
    public const short OpCode = PacketClientLuaMetrics.OpCode;

    public int Unknown1;
    public int Unknown2;
    public string ActionName = string.Empty;
    public int RequestId;

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out TakeMeThereRequestPacket value)
    {
        value = new TakeMeThereRequestPacket();

        var reader = new PacketReader(data);

        if (!reader.TryRead(out short opCode) || opCode != OpCode)
            return false;

        if (!reader.TryRead(out value.Unknown1))
            return false;

        if (!reader.TryRead(out value.Unknown2))
            return false;

        if (!reader.TryRead(out value.ActionName))
            return false;

        if (!reader.TryRead(out value.RequestId))
            return false;

        return reader.RemainingLength == 0;
    }
}
