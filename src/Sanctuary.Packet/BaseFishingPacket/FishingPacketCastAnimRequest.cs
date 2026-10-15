using System;
using System.Numerics;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// The cast animation starting. Sent by the client, and relayed by the server to the caster and to the players who
/// can see them, which puts the fishing player in the casting pose (and attaches the rod the line hangs from).
/// </summary>
public class FishingPacketCastAnimRequest : BaseFishingPacket, ISerializablePacket, IDeserializable<FishingPacketCastAnimRequest>
{
    public new const short OpCode = 5;

    public ulong Guid;

    /// <summary>The cast distance, as a float's bits.</summary>
    public int UnknownInt;

    /// <summary>The cast direction.</summary>
    public Vector4 Position;

    public FishingPacketCastAnimRequest() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(UnknownInt);
        writer.Write(Position);

        return writer.Buffer;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out FishingPacketCastAnimRequest value)
    {
        value = new FishingPacketCastAnimRequest();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.Guid))
            return false;

        if (!reader.TryRead(out value.UnknownInt))
            return false;

        if (!reader.TryRead(out value.Position))
            return false;

        return true;
    }
}
