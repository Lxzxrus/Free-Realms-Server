using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Fishing (opcode 138, a short sub-opcode). Layouts reverse-engineered from the 2014 client by edenfps (upstream
/// PR #31); see <c>docs/research/fishing/FISHING_RE_NOTES.md</c>. Every guid the server sends is the fishing
/// player's own: the client looks the proxied fishing player up by it.
/// </summary>
public class BaseFishingPacket
{
    public const short OpCode = 138;

    private short SubOpCode;

    public BaseFishingPacket(short subOpCode)
    {
        SubOpCode = subOpCode;
    }

    public virtual void Write(PacketWriter writer)
    {
        writer.Write(OpCode);
        writer.Write(SubOpCode);
    }

    public bool TryRead(ref PacketReader reader)
    {
        if (!reader.TryRead(out short opCode) || opCode != OpCode)
            return false;

        if (!reader.TryRead(out short subOpCode) || subOpCode != SubOpCode)
            return false;

        return true;
    }
}
