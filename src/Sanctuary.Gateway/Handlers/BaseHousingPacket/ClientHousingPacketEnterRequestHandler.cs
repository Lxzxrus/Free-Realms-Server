using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ClientHousingPacketEnterRequestHandler
{
    private static ILogger _logger = null!;
    private static IHouseManager _houseManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ClientHousingPacketEnterRequestHandler));

        _houseManager = serviceProvider.GetRequiredService<IHouseManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientHousingPacketEnterRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(ClientHousingPacketEnterRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(ClientHousingPacketEnterRequest), packet);

        var player = connection.Player;
        var houseGuid = packet.HouseGuid;

        // A move to another zone: one that comes too soon after the last waits for its turn (ZoneTeleports), and the
        // client waits for it.
        ZoneTeleports.Request(player, Environment.TickCount64, () =>
        {
            var result = _houseManager.EnterHouse(player, houseGuid);

            if (result != EnterHouseResult.Success)
                ChatHelper.SendSystemMessage(player, "That house could not be entered.");
        });

        return true;
    }
}
