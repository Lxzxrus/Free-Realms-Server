using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Repeating activities (opcode 143): things a player may do a set number of times per period, such as the daily
/// wheel. Layout from FreeRealms.exe (dispatcher 0xadbf00, header reader 0xadae70): short opcode, byte sub-opcode.
/// </summary>
public class BaseRepeatingActivityPacket
{
    public const short OpCode = 143;

    private byte SubOpCode;

    public BaseRepeatingActivityPacket(byte subOpCode)
    {
        SubOpCode = subOpCode;
    }

    public virtual void Write(PacketWriter writer)
    {
        writer.Write(OpCode);
        writer.Write(SubOpCode);
    }
}
