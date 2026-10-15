using System;
using System.Numerics;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>The line lands: where it landed. The client sends it several times per cast.</summary>
public class FishingPacketCastRequest : BaseFishingPacket, ISerializablePacket, IDeserializable<FishingPacketCastRequest>
{
    public new const short OpCode = 6;

    public ulong Guid;
    public Vector4 Position;
    public bool Flag;

    public FishingPacketCastRequest() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(Position);
        writer.Write(Flag);

        return writer.Buffer;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out FishingPacketCastRequest value)
    {
        value = new FishingPacketCastRequest();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.Guid))
            return false;

        if (!reader.TryRead(out value.Position))
            return false;

        if (!reader.TryRead(out byte flag))
            return false;

        value.Flag = flag != 0;

        return true;
    }
}
