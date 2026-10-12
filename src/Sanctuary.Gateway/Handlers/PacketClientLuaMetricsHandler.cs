using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

/// <summary>
/// Opcode 142, the client's Lua UI metrics. The client expects no reply, so events are read and dropped, apart from
/// the "Take Me There" one, which is passed on to <see cref="TakeMeThereRequestPacketHandler"/>.
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

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (TakeMeThereRequestPacket.TryDeserialize(data, out var takeMeThere))
            return TakeMeThereRequestPacketHandler.HandlePacket(connection, takeMeThere);

        // Telemetry only: an event we can't read is not worth more than a debug line.
        if (!PacketClientLuaMetrics.TryDeserialize(data, out var metric))
        {
            _logger.LogDebug("Unreadable {packet}. ( {data} )", nameof(PacketClientLuaMetrics),
                Convert.ToHexString(data.Length > 256 ? data[..256] : data));
            return true;
        }

        _logger.LogTrace("Received {packet} {name}. ( {data} )", nameof(PacketClientLuaMetrics), metric.Name,
            Convert.ToHexString(metric.Data));

        return true;
    }
}
