using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Reports;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

/// <summary>
/// The client's bug report window. The XML is read safely by <see cref="BugReportXml"/> and kept as a report staff read
/// with <c>!mod reports</c>. The client expects nothing back. Ported from BubbaJoeX/Sanctuary 982a800 (AGPL-3.0), whose
/// attribute regex never matched.
/// </summary>
[PacketHandler]
public static class BugSubmissionPacketAddBugHandler
{
    private static ILogger _logger = null!;
    private static ReportDesk _reportDesk = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BugSubmissionPacketAddBugHandler));

        _reportDesk = serviceProvider.GetRequiredService<ReportDesk>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        // The client's XML can be cut short (it writes a long text into a fixed buffer), which shows up as a packet that
        // does not parse. That is a bad packet, not an error worth a line each.
        if (!BugSubmissionPacketAddBug.TryDeserialize(data, out var packet))
        {
            _logger.LogDebug("Failed to deserialize {packet}.", nameof(BugSubmissionPacketAddBug));
            return false;
        }

        if (connection.Player is null)
            return false;

        _reportDesk.ReportBug(connection.Player, packet.BugXml);

        return true;
    }
}
