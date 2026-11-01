using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PetRequestNamePacketHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PetRequestNamePacketHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PetRequestNamePacket.TryDeserialize(data, out var petRequestNamePacket))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PetRequestNamePacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PetRequestNamePacket), petRequestNamePacket);

        var player = connection.Player;
        var pet = player.Pet;
        var petClientInfo = player.GetActivePetClientInfo();

        if (pet is null || petClientInfo is null || pet.Guid != player.ActivePetGuid)
            return true;

        var petSetNamePacket = new PetSetNamePacket
        {
            PetGuid = pet.Guid,
            Name = petRequestNamePacket.Name
        };

        // Evergrove: the character name rules and name filter (PetHelper.IsValidName).
        if (!PetHelper.IsValidName(petRequestNamePacket.Name, _resourceManager.NameFilter))
        {
            connection.SendTunneled(petSetNamePacket);
            return true;
        }

        // Evergrove: one pet request every PetHelper.RequestIntervalMs. A rename saves and is shown to everyone nearby.
        if (!PetHelper.TryStartRequest(player))
        {
            connection.SendTunneled(petSetNamePacket);
            return true;
        }

        var characterId = GuidHelper.GetPlayerId(player.Guid);
        using var dbContext = _dbContextFactory.CreateDbContext();

        if (!PetHelper.TryGetDbPet(dbContext, characterId, player.ActivePetId, out var dbPet))
            return true;

        dbPet.Name = petRequestNamePacket.Name;
        dbContext.SaveChanges();
        petClientInfo.Name = dbPet.Name;
        petSetNamePacket.Accepted = true;

        // Evergrove: the pet list is sent from the zone's tick, which is where the pets' item lists in it change.
        player.PlayerPet.QueueAction(() =>
        {
            pet.Name = petClientInfo.Name;
            player.PlayerPet.SendPetList();
        });
        player.SendTunneledToVisible(petSetNamePacket, true);

        return true;
    }
}
