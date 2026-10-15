using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class FishingPacketSpecialResponse : BaseFishingPacket, ISerializablePacket
{
    public new const short OpCode = 16;

    public ulong Guid;
    public int Unknown;
    public bool Flag;

    public FishingPacketSpecialResponse() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(Unknown);
        writer.Write(Flag);

        return writer.Buffer;
    }
}
