using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// A player reporting another player or a house as offensive, client to server only. Unlike the rating packets the
/// sub-op is a short. Ported from BubbaJoeX/Sanctuary 982a800 (AGPL-3.0).
/// <para>
/// <b>Layouts read from FreeRealms.exe (Ghidra); not yet tested in the game.</b> The decompiled UI Lua calls
/// <c>Ui.ReportAsOffensive</c> (the chat log's "Report"), <c>House.ReportOffensive</c> (the house panel) and
/// <c>Ui.OnBugReportUiSubmit</c> (the bug window). The serializers in the exe write:
/// player report, op 68 sub 1 (0x0091b9d0): the name, an int32 count (at most 10 records), then per record an int32
/// (the channel), a string (the message) and 8 bytes (the time, whose unit is not known);
/// house report, op 68 sub 3 (0x00ac19a0 with 0x00ac71a0): a u64 (the house's instance guid), a u64 (the owner's
/// character guid), then the house name and the owner name, in the order <c>PlayerHousingInstanceInfo.Serialize</c>
/// writes them (the scout plan calls the house report "sub 2"; the exe says 3);
/// bug report, op 83 sub 1 (0x009fc2e0 with 0x009fc9c0 and 0x009fad80): int32, string, int32, then the XML string, with
/// root <c>BugSubmission</c> and the attributes Category, SubCategory, Severity and BugDescription, and a child
/// <c>PlayerData</c> element.
/// All three travel inside <c>PacketTunneledClientPacket</c> (op 5), which is where they are routed. If a real client
/// disagrees, every such report is refused, and the refusal is logged at Warning with the payload's bytes (rate
/// limited, see <c>ReportRefusalLog</c>), so test a chat report, a house report and a bug report in the game.
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
