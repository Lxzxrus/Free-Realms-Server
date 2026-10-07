using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketClientIsReadyHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        _logger = loggerFactory.CreateLogger(nameof(PacketClientIsReadyHandler));
    }

    public static bool HandlePacket(GatewayConnection connection)
    {
        _logger.LogTrace("Received {name} packet.", nameof(PacketClientIsReady));

        connection.Player.Zone.OnClientIsReady(connection.Player);

        // The client never asks for its house list; "My Houses" shows whatever the server last sent. Send it on
        // entering the world so a relog, or a lot bought earlier, shows up. Not inside a house: the house sends
        // its own instance list, and a second one after it may override what the client needs to decorate.
        if (connection.Player.Zone is not Sanctuary.Game.Zones.HousingZone)
            ClientHousingPacketRequestPlayerHousesHandler.SendHouseList(connection);

        return true;
    }
}