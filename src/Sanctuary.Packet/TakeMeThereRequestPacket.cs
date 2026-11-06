using System.Diagnostics.CodeAnalysis;

namespace Sanctuary.Packet;

/// <summary>
/// The "TakeMeThere" counter in a <see cref="PacketClientLuaMetrics"/> report. The quest tracker's Take Me There button
/// runs <c>Ui.IncrementClientLuaMetric("TakeMeThere")</c>, then <c>Ui.TakeMeToObjective</c>, which walks the player
/// along the path a <see cref="ClientPathRequestPacket"/> asks for: that request is what makes the button work. This
/// counter arrives later, with the next metrics report.
/// </summary>
/// <remarks>
/// Before Evergrove read opcode 142 as upstream OSFR's list of counters (PR #165), this was read as a packet of its
/// own, recognised by its shape: a report holding one counter. Its first two ints were the report's count and the
/// counter's hash.
/// </remarks>
public class TakeMeThereRequestPacket
{
    public const string MetricName = "TakeMeThere";

    public string ActionName = MetricName;

    /// <summary>The counter's value: how many presses the report counts. Sent back as the reply's id.</summary>
    public int RequestId;

    /// <summary>
    /// Finds the Take Me There counter in a report. Only a report holding that counter alone counts, as before the
    /// list was read: the shape Evergrove has answered since task 27, now also checked by name.
    /// </summary>
    public static bool TryFrom(PacketClientLuaMetrics metrics, [MaybeNullWhen(false)] out TakeMeThereRequestPacket value)
    {
        value = null;

        if (metrics.Metrics is not [{ Name: MetricName } metric])
            return false;

        value = new TakeMeThereRequestPacket { ActionName = metric.Name, RequestId = metric.Value };
        return true;
    }
}
