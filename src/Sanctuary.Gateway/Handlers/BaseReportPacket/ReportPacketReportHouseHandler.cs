using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Reports;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ReportPacketReportHouseHandler
{
    private static ILogger _logger = null!;
    private static ReportDesk _reportDesk = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ReportPacketReportHouseHandler));

        _reportDesk = serviceProvider.GetRequiredService<ReportDesk>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ReportPacketReportHouse.TryDeserialize(data, out var packet))
        {
            ReportRefusalLog.Refused(_logger, nameof(ReportPacketReportHouse), data);
            return false;
        }

        if (connection.Player is null)
            return false;

        _reportDesk.ReportHouse(connection.Player, packet.HouseGuid, packet.OwnerGuid, packet.HouseName, packet.OwnerName);

        return true;
    }
}
