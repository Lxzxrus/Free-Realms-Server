using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PlayerUpdatePacketJumpHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PlayerUpdatePacketJumpHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
    {
        if (!PlayerUpdatePacketJump.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PlayerUpdatePacketJump));
            return false;
        }

        // _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PlayerUpdatePacketJump), packet);

        // Evergrove: a jump moves and turns the player too, so it is held to the same standard as
        // PlayerUpdatePacketUpdatePosition: a position in the world (pets and NPCs act on it, and far outside the world a
        // pet following its owner could freeze the zone's tick inside the navmesh) and a rotation within
        // Positions.RotationLimit, which is relayed to everyone nearby and saved with the character. NaN and infinities
        // fail both.
        if (!Positions.IsInWorld(packet.Position) || !Positions.IsWithinRotationLimit(packet.Rotation))
        {
            connection.ReportFault();
            return true;
        }

        packet.Guid = connection.Player.Guid;

        connection.Player.UpdatePosition(packet.Position, packet.Rotation);

        connection.Player.SendTunneledToVisible(packet);

        return true;
    }
}