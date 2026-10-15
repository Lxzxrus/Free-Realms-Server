using System;
using System.Diagnostics;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class MiniGamePayloadPacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(MiniGamePayloadPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!MiniGamePayloadPacket.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(MiniGamePayloadPacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(MiniGamePayloadPacket), packet);

        if (DailyWheelHandler.TryHandle(connection, packet))
            return true;

        if (FlashMinigameHandler.TryHandle(connection, packet))
            return true;

        return true;
    }
}