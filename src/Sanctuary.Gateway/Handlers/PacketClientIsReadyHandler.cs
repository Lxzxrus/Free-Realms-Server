using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketClientIsReadyHandler
{
    /// <summary>
    /// A repeat in the same zone sooner than this is dropped. The client says it's ready once per zone it loads, and
    /// loading takes seconds; each answer sends the zone's whole welcome (map points, journal, shop, quests, every
    /// quest NPC's marker) and reads the house list from the database, so a repeated packet multiplied it.
    /// </summary>
    private const int MinRepeatIntervalMs = 2000;

    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        _logger = loggerFactory.CreateLogger(nameof(PacketClientIsReadyHandler));
    }

    public static bool HandlePacket(GatewayConnection connection)
    {
        _logger.LogTrace("Received {name} packet.", nameof(PacketClientIsReady));

        var player = connection.Player;

        if (player.ReadyZone == player.Zone && Environment.TickCount64 < player.NextClientIsReadyAt)
            return true;

        player.ReadyZone = player.Zone;
        player.NextClientIsReadyAt = Environment.TickCount64 + MinRepeatIntervalMs;

        connection.Player.Zone.OnClientIsReady(connection.Player);

        // The client never asks for its house list; "My Houses" shows whatever the server last sent. Send it on
        // entering the world so a relog, or a lot bought earlier, shows up. Not inside a house: the house sends
        // its own instance list, and a second one after it may override what the client needs to decorate.
        if (connection.Player.Zone is not Sanctuary.Game.Zones.HousingZone)
            ClientHousingPacketRequestPlayerHousesHandler.SendHouseList(connection);

        return true;
    }
}