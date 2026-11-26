using System;
using System.Collections.Generic;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// "Report" on a name in the chat log, or /report: the name and the last chat lines (up to ten) the reporter's client
/// has from them. With no chat history for that name the client sends nothing. Every field comes from the client and
/// can be forged, so the server keeps the lines as the client's words, beside its own record of what it relayed, for
/// staff to compare. The time is the record's first 8 bytes, as the exe writes them; its unit is not known (BubbaJoeX's
/// test sample, 1,700,000,000, is a round number from his own unit test, not a capture), so it is kept as sent.
/// </summary>
public class ReportPacketReportPlayer : BaseReportPacket, IDeserializable<ReportPacketReportPlayer>
{
    public new const short OpCode = 1;

    /// <summary>The most chat lines the client sends (it keeps the last ten of its history).</summary>
    public const int MaxLines = 10;

    public string ReportedName = string.Empty;

    public List<(int Channel, string Message, ulong Time)> Lines = [];

    public ReportPacketReportPlayer() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ReportPacketReportPlayer value)
    {
        value = new ReportPacketReportPlayer();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader) || !reader.TryRead(out value.ReportedName) || !reader.TryRead(out int count) || count is < 0 or > MaxLines)
            return false;

        for (var i = 0; i < count; i++)
        {
            // The client's order is channel, message, time.
            if (!reader.TryRead(out int channel) || !reader.TryRead(out string message) || !reader.TryRead(out ulong time))
                return false;

            value.Lines.Add((channel, message, time));
        }

        return reader.RemainingLength == 0;
    }
}
