using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketWorldTeleportRequestHandler
{
    private static ILogger _logger = null!;
    private static IZoneManager _zoneManager = null!;
    private static IHouseManager _houseManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketWorldTeleportRequestHandler));

        _zoneManager = serviceProvider.GetRequiredService<IZoneManager>();
        _houseManager = serviceProvider.GetRequiredService<IHouseManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
    {
        if (!PacketWorldTeleportRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketWorldTeleportRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PacketWorldTeleportRequest), packet);

        if (!connection.Player.Friends.Any(friend => friend.Guid == packet.Guid))
            return true;

        if (!_zoneManager.TryGetPlayer(packet.Guid, out var player) || !player.Visible)
            return true;

        var position = player.Position;
        var rotation = player.Rotation;

        if (!ReferenceEquals(connection.Player.Zone, player.Zone))
        {
            // A move to another zone: one that comes too soon after the last waits for its turn (ZoneTeleports).
            var self = connection.Player;
            var zone = player.Zone;

            if (zone is HousingZone housingZone)
            {
                var houseGuid = GuidHelper.GetHouseGuid(housingZone.HouseId);

                ZoneTeleports.Request(self, Environment.TickCount64, () =>
                {
                    if (_houseManager.EnterHouse(self, houseGuid) != EnterHouseResult.Success)
                        ChatHelper.SendSystemMessage(self, "That house could not be entered.");
                });

                return true;
            }

            if (zone is not WorldZone)
            {
                ChatHelper.SendSystemMessage(self, "You can't join that player where they are.");
                return true;
            }

            ZoneTeleports.Request(self, Environment.TickCount64, () =>
            {
                if (!self.TeleportToZone(zone, position, rotation))
                    ChatHelper.SendSystemMessage(self, "The teleport failed. Please try again.");
            });

            return true;
        }

        connection.Player.UpdatePosition(position, rotation);

        connection.Player.UpdatePointOfInterest();

        var clientUpdatePacketUpdateLocation = new ClientUpdatePacketUpdateLocation
        {
            Position = position,
            Rotation = rotation,
            Teleport = true
        };

        connection.SendTunneled(clientUpdatePacketUpdateLocation);

        return true;
    }
}
