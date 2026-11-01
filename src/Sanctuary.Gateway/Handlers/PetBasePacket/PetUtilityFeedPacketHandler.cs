using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PetUtilityFeedPacketHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PetUtilityFeedPacketHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PetUtilityFeedPacket.TryDeserialize(data, out var petUtilityFeedPacket))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PetUtilityFeedPacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PetUtilityFeedPacket), petUtilityFeedPacket);

        // Evergrove: one pet request every PetHelper.RequestIntervalMs, as feeding runs two queries, and one that comes
        // sooner waits its turn. The client keeps its pet buttons locked until the feed starts or is refused.
        if (PetHelper.TryStartRequest(connection.Player, () => Feed(connection, petUtilityFeedPacket)))
            Feed(connection, petUtilityFeedPacket);

        return true;
    }

    private static void Feed(GatewayConnection connection, PetUtilityFeedPacket petUtilityFeedPacket)
    {
        var player = connection.Player;

        if (!PetHelper.TryGetPetActionItems(player, petUtilityFeedPacket.PetId, _dbContextFactory, _resourceManager,
                PetHelper.PetFoodBowlUtilitySlot, PetHelper.PetFoodUtilitySlot, out var petClientInfo, out var foodBowlItem,
                out var foodBowlDefinition, out var foodDefinition))
        {
            return;
        }

        var hunger = Math.Min(DbPet.MaxNeed, petClientInfo.Hunger + foodDefinition.Param2);
        var foodBowlAttachment = PetHelper.GetAttachment(foodBowlItem.Tint, foodBowlDefinition);

        player.PlayerPet.QueueAction(() => player.PlayerPet.FeedPet(foodBowlItem.Id, foodBowlAttachment, hunger));
    }
}
