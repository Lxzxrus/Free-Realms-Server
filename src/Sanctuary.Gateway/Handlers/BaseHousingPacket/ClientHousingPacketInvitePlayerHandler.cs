using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ClientHousingPacketInvitePlayerHandler
{
    private static ILogger _logger = null!;
    private static IHouseManager _houseManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ClientHousingPacketInvitePlayerHandler));
        _houseManager = serviceProvider.GetRequiredService<IHouseManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!ClientHousingPacketInvitePlayer.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(ClientHousingPacketInvitePlayer));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(ClientHousingPacketInvitePlayer), packet);

        var result = _houseManager.InviteToHouse(connection.Player, packet.PlayerGuid, out var invitee);

        // The client has already said "House invite sent to player.", so only a refusal needs a message. An ignored
        // invite stays quiet, as a tell to someone ignoring you does.
        var message = result switch
        {
            HouseInviteResult.NotInHouse => "Stand in your house to invite someone into it.",
            HouseInviteResult.NotOwner => "Only the house's owner can invite people into it.",
            HouseInviteResult.NotOnline => "That player isn't online, so the invite wasn't sent.",
            HouseInviteResult.NoInviteNeeded => "Your own characters can always come into your houses.",
            HouseInviteResult.AlreadyThere => $"{invitee?.Name.FullName} is already here.",
            HouseInviteResult.TooSoon => $"You've just invited {invitee?.Name.FullName}. Give them a moment to answer.",
            _ => null
        };

        if (message is not null)
            ChatHelper.SendSystemMessage(connection.Player, message);

        return true;
    }
}
