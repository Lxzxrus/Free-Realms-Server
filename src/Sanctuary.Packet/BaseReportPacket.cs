using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// A player reporting another player or a house as offensive, client to server only. Unlike the rating packets the
/// sub-op is a short. Ported from BubbaJoeX/Sanctuary 982a800 (AGPL-3.0); the layouts were checked against the
/// client's code (docs/research/reports.md).
/// </summary>
public class BaseReportPacket
{
    public const short OpCode = 68;

    private readonly short SubOpCode;

    public BaseReportPacket(short subOpCode)
    {
        SubOpCode = subOpCode;
    }

    public bool TryRead(ref PacketReader reader)
    {
        // Either one being wrong refuses the packet.
        if (!reader.TryRead(out short opCode) || opCode != OpCode)
            return false;

        if (!reader.TryRead(out short subOpCode) || subOpCode != SubOpCode)
            return false;

        return true;
    }
}
