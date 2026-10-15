using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class FishingPacketSpecialRequest : BaseFishingPacket, ISerializablePacket, IDeserializable<FishingPacketSpecialRequest>
{
    public new const short OpCode = 15;

    public ulong Guid;
    public ulong Data;

    public FishingPacketSpecialRequest() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(Data);

        return writer.Buffer;
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out FishingPacketSpecialRequest value)
    {
        value = new FishingPacketSpecialRequest();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.Guid))
            return false;

        if (!reader.TryRead(out value.Data))
            return false;

        return true;
    }
}
