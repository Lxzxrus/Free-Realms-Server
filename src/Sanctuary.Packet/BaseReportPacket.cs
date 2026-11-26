using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// A player reporting another player or a house as offensive, client to server only. Unlike the rating packets the
/// sub-op is a short. Ported from BubbaJoeX/Sanctuary 982a800 (AGPL-3.0).
/// <para>
/// <b>Not yet verified in the game client.</b> What is known: the decompiled UI Lua calls <c>Ui.ReportAsOffensive</c>
/// (the chat log's "Report"), <c>House.ReportOffensive</c> (the house panel) and <c>Ui.OnBugReportUiSubmit</c> (the bug
/// window), so the client has all three. The wire layouts (sub-ops 1 and 3 on op 68, the field orders, the ten-line
/// limit) are BubbaJoeX's reading; the Ghidra check of those natives has not been done. The scout plan calls the house
/// report "sub 2", the code uses 3 as he does. If a real client disagrees, every such report is refused (a Debug log
/// line only), so test a chat report, a house report and a bug report in the game and capture the bytes.
/// </para>
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
