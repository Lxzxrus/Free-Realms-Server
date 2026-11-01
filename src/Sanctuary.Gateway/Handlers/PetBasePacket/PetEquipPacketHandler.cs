using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PetEquipPacketHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PetEquipPacketHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PetEquipPacket.TryDeserialize(data, out var petEquipPacket))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PetEquipPacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PetEquipPacket), petEquipPacket);

        // Evergrove: one pet request every PetHelper.RequestIntervalMs, and one that comes sooner waits its turn. An equip
        // saves and is shown to everyone nearby.
        if (PetHelper.TryStartRequest(connection.Player, () => Equip(connection, petEquipPacket)))
            Equip(connection, petEquipPacket);

        return true;
    }

    private static void Equip(GatewayConnection connection, PetEquipPacket petEquipPacket)
    {
        var player = connection.Player;
        var pet = player.Pet;
        var petClientInfo = player.GetActivePetClientInfo();

        if (pet is null || petClientInfo is null || pet.Guid != player.ActivePetGuid || !PetHelper.PetClothingSlots.Contains(petEquipPacket.Slot))
            return;

        var characterId = GuidHelper.GetPlayerId(player.Guid);
        using var dbContext = _dbContextFactory.CreateDbContext();
        var dbPet = dbContext.Pets.Include(ownedPet => ownedPet.Items)
            .SingleOrDefault(ownedPet => ownedPet.CharacterId == characterId && ownedPet.Id == player.ActivePetId);

        if (dbPet is null)
            return;

        DbItem? dbItem = null;
        ClientItemDefinition? clientItemDefinition = null;

        if (!petEquipPacket.Unequip)
        {
            dbItem = dbContext.Items.SingleOrDefault(item => item.CharacterId == characterId
                && item.Id == petEquipPacket.ItemGuid && item.Count > 0);

            if (dbItem is null || !_resourceManager.ClientItemDefinitions.TryGetValue(dbItem.Definition, out clientItemDefinition)
                || !PetHelper.CanWearClothing(petClientInfo, clientItemDefinition, petEquipPacket.Slot))
            {
                return;
            }
        }

        foreach (var equippedItem in dbPet.Items.Where(item =>
            _resourceManager.ClientItemDefinitions.TryGetValue(item.Definition, out var definition)
                && definition.Slot == petEquipPacket.Slot).ToArray())
        {
            dbPet.Items.Remove(equippedItem);
        }

        if (dbItem is not null)
            dbPet.Items.Add(dbItem);

        dbContext.SaveChanges();

        var itemGuids = dbPet.Items.OrderBy(item => item.Id).Select(item => item.Id).ToArray();
        var equippedItemGuid = dbItem?.Id ?? PetHelper.NoItemGuid;
        var attachment = dbItem is null ? null : PetHelper.GetAttachment(dbItem.Tint, clientItemDefinition!);

        // Evergrove: the pet's item list changes on the zone's tick with its attachments. The zone reads the list to
        // summon the pet and send the pet list, and changing it from here could break the tick for the whole zone.
        player.PlayerPet.QueueAction(() =>
        {
            petClientInfo.ItemGuids.Clear();
            petClientInfo.ItemGuids.AddRange(itemGuids);

            if (attachment is null)
                PetHelper.UnequipItem(player, pet, petEquipPacket.Slot);
            else
                PetHelper.EquipItem(player, pet, equippedItemGuid, attachment);

            player.PlayerPet.SendPetList();
        });

        connection.SendTunneled(new PetEquipPacket
        {
            Unequip = petEquipPacket.Unequip,
            Slot = petEquipPacket.Slot,
            ItemGuid = equippedItemGuid
        });
    }
}
