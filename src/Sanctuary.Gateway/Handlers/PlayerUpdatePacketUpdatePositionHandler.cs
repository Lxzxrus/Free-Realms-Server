using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Quests;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PlayerUpdatePacketUpdatePositionHandler
{
    private static ILogger _logger = null!;
    private static IQuestManager _questManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PlayerUpdatePacketUpdatePositionHandler));

        _questManager = serviceProvider.GetRequiredService<IQuestManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
    {
        if (!PlayerUpdatePacketUpdatePosition.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PlayerUpdatePacketUpdatePosition));
            return false;
        }

        // _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PlayerUpdatePacketUpdatePosition), packet);

        // A real client sends finite numbers. NaN compares false with everything, so it would pass every
        // "too far away" check; a connection that sends it is counted as faulty and dropped if it keeps on.
        if (!Positions.IsFinite(packet.Position) || !Positions.IsFinite(packet.Rotation))
        {
            connection.ReportFault();
            return true;
        }

        packet.Guid = connection.Player.Guid;

        connection.Player.PositionState = packet.State;
        connection.Player.UpdatePosition(packet.Position, packet.Rotation);

        _questManager.OnEvent(connection.Player, new PlayerMovedEvent(connection.Player.Position, connection.Player.Zone.DefinitionId));

        connection.Player.RelayPosition(packet);

        return true;
    }
}