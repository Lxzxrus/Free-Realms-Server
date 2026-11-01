using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

/// <summary>
/// Opcode 142, the client's Lua UI metrics. The client expects no reply, so the counters are only logged, apart from
/// the "TakeMeThere" one, which is passed on to <see cref="TakeMeThereRequestPacketHandler"/>.
/// </summary>
[PacketHandler]
public static class PacketClientLuaMetricsHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketClientLuaMetricsHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
    {
        // Telemetry only (Evergrove, task 27): the client expects no reply, and an event we can't read is not worth
        // more than a debug line, nor a dropped connection.
        if (!PacketClientLuaMetrics.TryDeserialize(data, out var packet))
        {
            _logger.LogDebug("Unreadable {packet}. ( {data} )", nameof(PacketClientLuaMetrics), PacketLog.Hex(data));
            return true;
        }

        foreach (var metric in packet.Metrics)
            _logger.LogTrace("{connection} sent client Lua metric {name} = {value}.", connection, metric.Name, metric.Value);

        if (TakeMeThereRequestPacket.TryFrom(packet, out var takeMeThere))
            return TakeMeThereRequestPacketHandler.HandlePacket(connection, takeMeThere);

        return true;
    }
}
