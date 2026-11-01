using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketClientIsReadyHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IHouseManager _houseManager = null!;
    private static HousingOptions _housingOptions = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        _logger = loggerFactory.CreateLogger(nameof(PacketClientIsReadyHandler));
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _houseManager = serviceProvider.GetRequiredService<IHouseManager>();
        _housingOptions = serviceProvider.GetRequiredService<IOptions<HousingOptions>>().Value;
    }

    public static bool HandlePacket(GatewayConnection connection)
    {
        _logger.LogTrace("Received {name} packet.", nameof(PacketClientIsReady));

        // The client says it's ready once per zone it loads, so a repeat for the same entry into the zone soon after is
        // dropped: each answer sends the zone's whole welcome (map points, journal, shop, quests, every quest NPC's
        // marker) and, out of a house, reads the house list from the database.
        if (!connection.Player.ClientIsReadyReplies.TryAnswer(connection.Player.ZoneEntry, Environment.TickCount64))
            return true;

        connection.Player.Zone.OnClientIsReady(connection.Player);

        // The client never asks for its house list; "My Houses" shows whatever the server last sent. Send it on
        // entering the world so a relog, or a lot bought earlier, shows up. Not inside a house: the house sends
        // its own instance list, and a second one after it may override what the client needs to decorate.
        if (connection.Player.Zone is not Sanctuary.Game.Zones.HousingZone)
        {
            // Evergrove: every account owns a free Apartment (docs/design/evergrove-housing.md, section 10). The session's
            // first world login gives one to an account that has none, before the list goes out, so My Houses shows it
            // without a relog. A login always starts in the world.
            if (connection.Player.SeenOneTimeNotifications.TryAdd(OneTimeNotification.StarterHouse))
                GiveStarterHouse(connection.Player);

            ClientHousingPacketRequestPlayerHousesHandler.SendHouseList(connection);
        }

        return true;
    }

    private static void GiveStarterHouse(Player player)
    {
        if (_houseManager.EnsureStarterHouse(player) != StarterHouseResult.Granted)
            return;

        if (StarterHouse.Lot(_housingOptions, _resourceManager.Zones) is { } lot && StarterHouse.LineOnGrant(player, lot) is { } line)
            ChatHelper.SendSystemMessage(player, line);
    }
}
