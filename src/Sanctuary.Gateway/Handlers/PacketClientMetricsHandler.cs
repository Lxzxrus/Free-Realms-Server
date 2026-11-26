using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketClientMetricsHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketClientMetricsHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
    {
        if (!PacketClientMetrics.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketClientMetrics));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PacketClientMetrics), packet);

        // The client sends these every 15 s, flagged idle for as long as the player is away. Idle players aren't disconnected
        // (the community wants to AFK), so it's logged once when they go idle, not every 15 s with a "disconnected" that
        // never happened.
        if (packet.Metrics.Idle == 1)
        {
            if (!connection.IdleLogged)
            {
                connection.IdleLogged = true;

                _logger.LogInformation("{connection} is idle; idle players aren't disconnected. ( Guid: {guid}, IdleTime: {idleTime} )", connection, connection.Player?.Guid, packet.Metrics.IdleTimeout);
            }

            // Disabled for now as community wants to AFK without being disconnected. This will be re-enabled in the future.
            // connection.Disconnect();
        }
        else
        {
            connection.IdleLogged = false;
        }

        return true;
    }
}