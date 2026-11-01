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
        // Nor does a real client leave the world: pets and NPCs act on its position, and far out there a pet
        // following its owner could freeze the zone's tick inside the navmesh. Its rotation is a heading, each
        // component between -1 and 1. A far larger one, though finite, could put a boombox or cake the player sets
        // down at a NaN or absurd position, which other players were sent.
        if (!Positions.IsFinite(packet.Position) || !Positions.IsWithinRotationLimit(packet.Rotation) || !Positions.IsInWorld(packet.Position))
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