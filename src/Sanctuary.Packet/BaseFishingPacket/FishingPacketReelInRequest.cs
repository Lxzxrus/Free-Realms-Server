using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>The player reels in.</summary>
public class FishingPacketReelInRequest : BaseFishingPacket, ISerializablePacket, IDeserializable<FishingPacketReelInRequest>
{
    public new const short OpCode = 7;

    public ulong Guid;
    public bool Flag;

    public FishingPacketReelInRequest() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(Flag);

        return writer.Buffer;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out FishingPacketReelInRequest value)
    {
        value = new FishingPacketReelInRequest();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.Guid))
            return false;

        if (!reader.TryRead(out byte flag))
            return false;

        value.Flag = flag != 0;

        return true;
    }
}
