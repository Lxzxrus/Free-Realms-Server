using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Reports;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ReportPacketReportPlayerHandler
{
    private static ILogger _logger = null!;
    private static ReportDesk _reportDesk = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ReportPacketReportPlayerHandler));

        _reportDesk = serviceProvider.GetRequiredService<ReportDesk>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ReportPacketReportPlayer.TryDeserialize(data, out var packet))
        {
            ReportRefusalLog.Refused(_logger, nameof(ReportPacketReportPlayer), data);
            return false;
        }

        // A connection with no player has nothing to report from. The tunneled dispatcher already refuses one.
        if (connection.Player is null)
            return false;

        _reportDesk.ReportPlayer(connection.Player, packet.ReportedName, packet.Lines);

        // The client waits for no answer, so whatever the outcome (kept, limited, a repeat) it is handled.
        return true;
    }
}
