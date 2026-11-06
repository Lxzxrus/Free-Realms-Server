using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ClientHousingPacketLeaveHouseHandler
{
    private static ILogger _logger = null!;
    private static IHouseManager _houseManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ClientHousingPacketLeaveHouseHandler));
        _houseManager = serviceProvider.GetRequiredService<IHouseManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientHousingPacketLeaveHouse.TryDeserialize(data, out _))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(ClientHousingPacketLeaveHouse));
            return false;
        }

        _logger.LogTrace("Received {name} packet.", nameof(ClientHousingPacketLeaveHouse));

        var player = connection.Player;

        // A move to another zone: one that comes too soon after the last waits for its turn (ZoneTeleports), and the
        // client waits for it.
        ZoneTeleports.Request(player, Environment.TickCount64, () => _houseManager.LeaveHouse(player));
        return true;
    }
}
