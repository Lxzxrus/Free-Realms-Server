using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class MiniGameStartGamePacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(MiniGameStartGamePacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!MiniGameStartGamePacket.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(MiniGameStartGamePacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(MiniGameStartGamePacket), packet);

        if (DailyWheelHandler.TryStart(connection, packet.StateId))
            return true;

        if (FlashMinigameHandler.TryStart(connection, packet.StateId))
            return true;

        if (FishingHandler.TryStart(connection, packet.StateId, packet.GroupId, packet.GameId))
            return true;

        connection.SendTunneled(new MiniGameGameStartPacket(packet.StateId, packet.GroupId, packet.GameId));

        return true;
    }
}