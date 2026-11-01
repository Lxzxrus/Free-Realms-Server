using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PetUtilityGroomPacketHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PetUtilityGroomPacketHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PetUtilityGroomPacket.TryDeserialize(data, out var petUtilityGroomPacket))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PetUtilityGroomPacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PetUtilityGroomPacket), petUtilityGroomPacket);

        // Evergrove: one pet request every PetHelper.RequestIntervalMs, as grooming runs two queries, and one that comes
        // sooner waits its turn. The client keeps its pet buttons locked until the grooming starts or is refused.
        if (PetHelper.TryStartRequest(connection.Player, () => Groom(connection, petUtilityGroomPacket)))
            Groom(connection, petUtilityGroomPacket);

        return true;
    }

    private static void Groom(GatewayConnection connection, PetUtilityGroomPacket petUtilityGroomPacket)
    {
        var player = connection.Player;

        if (!PetHelper.TryGetPetActionItems(player, petUtilityGroomPacket.PetId, _dbContextFactory, _resourceManager,
                PetHelper.PetShampooUtilitySlot, PetHelper.PetBrushUtilitySlot, out _, out _, out _, out _))
        {
            return;
        }

        player.PlayerPet.QueueAction(player.PlayerPet.StartPetGrooming);
    }
}
