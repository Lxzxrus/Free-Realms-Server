using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ClientHousingPacketToggleLockedHandler
{
    private static ILogger _logger = null!;
    private static IHouseManager _houseManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ClientHousingPacketToggleLockedHandler));
        _houseManager = serviceProvider.GetRequiredService<IHouseManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientHousingPacketToggleLocked.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(ClientHousingPacketToggleLocked));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(ClientHousingPacketToggleLocked), packet);

        if (connection.Player.Zone is not HousingZone zone)
            return true;

        // Locking also sends out visitors who couldn't come in now, so a lock gets rid of an unwanted guest.
        if (zone.Runtime.ToggleSetting(connection.Player, HouseSetting.Locked) == true)
        {
            var sentOut = _houseManager.SendOutLockedOutVisitors(zone);
            if (sentOut > 0)
            {
                ChatHelper.SendSystemMessage(connection.Player, sentOut == 1
                    ? "House locked. 1 visitor who isn't on your friend list was sent outside."
                    : $"House locked. {sentOut} visitors who aren't on your friend list were sent outside.");
            }
        }

        return true;
    }
}
