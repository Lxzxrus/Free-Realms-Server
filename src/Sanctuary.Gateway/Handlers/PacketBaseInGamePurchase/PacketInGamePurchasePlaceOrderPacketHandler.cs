using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketInGamePurchasePlaceOrderPacketHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;
    private static HousingOptions _housingOptions = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketBaseInGamePurchaseHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
        _housingOptions = serviceProvider.GetRequiredService<IOptions<HousingOptions>>().Value;
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PacketInGamePurchasePlaceOrderPacket.TryDeserialize(data, out var packetInGamePurchasePlaceOrderPacket))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketInGamePurchasePlaceOrderPacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(PacketInGamePurchasePlaceOrderPacket), packetInGamePurchasePlaceOrderPacket);

        var packetInGamePurchasePlaceOrderResponse = new PacketInGamePurchasePlaceOrderResponse
        {
            OrderTrackingId = packetInGamePurchasePlaceOrderPacket.Order.OrderTrackingId,
            OrderId = packetInGamePurchasePlaceOrderPacket.Order.OrderTrackingId.ToString()
        };

        var orderDetail = packetInGamePurchasePlaceOrderPacket.Order.Details.FirstOrDefault();

        if (orderDetail is null || orderDetail.Quantity <= 0)
        {
            return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
        }

        if (!_resourceManager.Stores.TryGetValue(orderDetail.StoreId, out var storeDefinition))
        {
            return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
        }

        if (!storeDefinition.Bundles.TryGetValue(orderDetail.StoreBundleId, out var appStoreBundleDefinition))
        {
            return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
        }

        var housingZoneDefinition = _resourceManager.Zones.Values
            .OfType<HousingZoneDefinition>()
            .FirstOrDefault(definition => definition.StoreBundleId == appStoreBundleDefinition.Id);
        var containsHouseItem = appStoreBundleDefinition.Entries.Any(entry =>
            _resourceManager.ClientItemDefinitions.TryGetValue(entry.MarketingItemId, out var definition) &&
            definition.Type == 16);

        if ((containsHouseItem && housingZoneDefinition is null) ||
            (housingZoneDefinition is not null &&
                (orderDetail.Quantity != 1 ||
                 appStoreBundleDefinition.Entries.Count != 1 ||
                 appStoreBundleDefinition.Entries[0].MarketingItemId != housingZoneDefinition.ItemDefinitionId ||
                 !_resourceManager.ClientItemDefinitions.TryGetValue(housingZoneDefinition.ItemDefinitionId, out var houseItemDefinition) ||
                 houseItemDefinition.Type != 16)))
        {
            packetInGamePurchasePlaceOrderResponse.Result = InGamePurchaseOrderResult.OrderFailure;

            connection.SendTunneled(packetInGamePurchasePlaceOrderResponse);

            return true;
        }

        if (!int.TryParse(orderDetail.Tint, out var orderDetailTint))
        {
            return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
        }

        if (housingZoneDefinition is not null)
        {
            var houseCost = _housingOptions.FreeLots
                ? 0
                : connection.Player.MembershipStatus == 0
                    ? appStoreBundleDefinition.Price
                    : appStoreBundleDefinition.MembersOnlyPrice;

            return PurchaseHouse(
                connection,
                packetInGamePurchasePlaceOrderPacket,
                housingZoneDefinition,
                houseCost,
                packetInGamePurchasePlaceOrderResponse);
        }

        var containsHousingInventory = appStoreBundleDefinition.Entries.Any(entry =>
            _resourceManager.ClientItemDefinitions.TryGetValue(entry.MarketingItemId, out var definition) &&
            StoreInventoryPurchasePolicy.IsHousingInventoryItem(definition));
        var cost = connection.Player.MembershipStatus == 0
            ? appStoreBundleDefinition.Price
            : containsHousingInventory
                ? appStoreBundleDefinition.MembersOnlyPrice
                : appStoreBundleDefinition.MemberDiscount;

        var totalCostValue = (long)cost * orderDetail.Quantity;

        if (cost < 0 || totalCostValue < 0 || totalCostValue > int.MaxValue)
        {
            return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
        }

        var totalCost = (int)totalCostValue;

        if (connection.Player.StationCash < totalCost)
        {
            return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.BalanceNotEnough);
        }

        using var dbContext = _dbContextFactory.CreateDbContext();

        var dbCharacter = dbContext.Characters
            .Include(x => x.Items)
            .Include(x => x.Mounts)
            .Include(x => x.Pets)
            .AsSplitQuery()
            .SingleOrDefault(x => x.Id == GuidHelper.GetPlayerId(connection.Player.Guid));

        if (dbCharacter is null)
        {
            return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
        }

        var lastItemId = dbCharacter.Items.Count > 0 ? dbCharacter.Items.Max(i => i.Id) : 0;
        var lastMountId = dbCharacter.Mounts.Count > 0 ? dbCharacter.Mounts.Max(x => x.Id) : 0;
        var lastPetId = dbCharacter.Pets.Count > 0 ? dbCharacter.Pets.Max(x => x.Id) : 0;

        var pendingUpdates = new List<Action>();
        var purchasedHousingInventory = false;

        foreach (var bundleEntry in appStoreBundleDefinition.Entries)
        {
            var totalQuantityValue = (long)orderDetail.Quantity * bundleEntry.Quantity;
            if (bundleEntry.Quantity <= 0 || totalQuantityValue > int.MaxValue)
            {
                packetInGamePurchasePlaceOrderResponse.Result = InGamePurchaseOrderResult.OrderFailure;

                connection.SendTunneled(packetInGamePurchasePlaceOrderResponse);

                return true;
            }

            if (!_resourceManager.ClientItemDefinitions.TryGetValue(bundleEntry.MarketingItemId, out var clientItemDefinition))
            {
                return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
            }

            if (StoreInventoryPurchasePolicy.IsSupported(clientItemDefinition) ||
                clientItemDefinition.Type == ItemDefinitionHelper.PetClothingItemType ||
                clientItemDefinition.Type == ItemDefinitionHelper.PetUtilityItemType)
            {
                var totalQuantity = (int)totalQuantityValue;
                var isHousingInventory = StoreInventoryPurchasePolicy.IsHousingInventoryItem(clientItemDefinition);
                var itemTint = isHousingInventory
                    ? StoreInventoryPurchasePolicy.ResolveHousingTint(clientItemDefinition, orderDetailTint)
                    : orderDetailTint;

                if (clientItemDefinition.MaxStackSize > 0
                    && dbCharacter.Items.Where(i => i.Definition == clientItemDefinition.Id && (!appStoreBundleDefinition.IsTintable || i.Tint == orderDetailTint))
                        .Sum(i => i.Count) + totalQuantity > clientItemDefinition.MaxStackSize)
                {
                    return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
                }

                var dbItem = dbCharacter.Items.SingleOrDefault(i =>
                    i.Definition == clientItemDefinition.Id && i.Tint == itemTint);

                if (dbItem is not null)
                {
                    if (dbItem.Count < 0 || dbItem.Count > int.MaxValue - totalQuantity)
                    {
                        packetInGamePurchasePlaceOrderResponse.Result = InGamePurchaseOrderResult.OrderFailure;

                        connection.SendTunneled(packetInGamePurchasePlaceOrderResponse);

                        return true;
                    }

                    dbItem.Count += totalQuantity;
                }
                else
                {
                    if (lastItemId == int.MaxValue)
                    {
                        packetInGamePurchasePlaceOrderResponse.Result = InGamePurchaseOrderResult.OrderFailure;

                        connection.SendTunneled(packetInGamePurchasePlaceOrderResponse);

                        return true;
                    }

                    dbItem = new DbItem
                    {
                        Id = ++lastItemId,
                        Definition = clientItemDefinition.Id,
                        Tint = itemTint,

                        Count = totalQuantity
                    };

                    dbCharacter.Items.Add(dbItem);
                }

                var itemDefinition = clientItemDefinition;
                var savedItem = dbItem;
                purchasedHousingInventory |= isHousingInventory;

                pendingUpdates.Add(() =>
                {
                    var clientItem = connection.Player.Items.SingleOrDefault(x =>
                        x.Definition == itemDefinition.Id && x.Tint == savedItem.Tint);

                    var addItem = false;

                    if (clientItem is not null)
                    {
                        clientItem.Count = savedItem.Count;
                    }
                    else
                    {
                        addItem = true;

                        clientItem = new ClientItem
                        {
                            Id = savedItem.Id,
                            Tint = savedItem.Tint,
                            Count = savedItem.Count,
                            Definition = savedItem.Definition
                        };

                        connection.Player.Items.Add(clientItem);
                    }

                    if (addItem)
                    {
                        using var writer = new PacketWriter();

                        clientItem.Serialize(writer);

                        itemDefinition.Serialize(writer);

                        var clientUpdatePacketItemAdd = new ClientUpdatePacketItemAdd();

                        clientUpdatePacketItemAdd.Payload = writer.Buffer;

                        connection.SendTunneled(clientUpdatePacketItemAdd);
                    }
                    else
                    {
                        var clientUpdatePacketItemUpdate = new ClientUpdatePacketItemUpdate
                        {
                            ItemGuid = clientItem.Id,
                            Count = clientItem.Count,
                        };

                        connection.SendTunneled(clientUpdatePacketItemUpdate);
                    }
                });
            }
            else if (clientItemDefinition.Type == ItemDefinitionHelper.PetItemType)
            {
                if (!PetHelper.TryGetModel(_resourceManager, clientItemDefinition, out var modelDefinition))
                {
                    return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
                }

                // totalQuantityValue is the entry's checked quantity from the top of the loop (Evergrove).
                if (totalQuantityValue > int.MaxValue - lastPetId)
                {
                    return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
                }

                var totalQuantity = (int)totalQuantityValue;

                if (clientItemDefinition.MaxStackSize > 0
                    && dbCharacter.Pets.Count(pet => pet.Definition == clientItemDefinition.Id && (!appStoreBundleDefinition.IsTintable || pet.Tint == orderDetailTint))
                        + totalQuantity > clientItemDefinition.MaxStackSize)
                {
                    return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
                }

                var petTint = appStoreBundleDefinition.IsTintable ? orderDetailTint : clientItemDefinition.Icon.TintId;
                var dbPets = new List<DbPet>();

                for (var i = 0; i < totalQuantity; i++)
                {
                    var dbPet = PetHelper.CreatePet(++lastPetId, clientItemDefinition.Id, petTint);

                    dbCharacter.Pets.Add(dbPet);
                    dbPets.Add(dbPet);
                }

                pendingUpdates.Add(() =>
                {
                    foreach (var dbPet in dbPets)
                        PetHelper.AddPet(connection.Player, _resourceManager, dbPet, clientItemDefinition, modelDefinition);

                    connection.Player.PlayerPet.SendPetList();
                });
            }
            else if (clientItemDefinition.Type == ItemDefinitionHelper.MountItemType)
            {
                if (lastMountId == int.MaxValue)
                {
                    packetInGamePurchasePlaceOrderResponse.Result = InGamePurchaseOrderResult.OrderFailure;

                    connection.SendTunneled(packetInGamePurchasePlaceOrderResponse);

                    return true;
                }

                if (!_resourceManager.Mounts.TryGetValue(clientItemDefinition.Param1, out var mountDefinition))
                {
                    return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
                }

                var mountTint = appStoreBundleDefinition.IsTintable ? orderDetailTint : mountDefinition.TintId;

                if (dbCharacter.Mounts.Any(x => x.Definition == mountDefinition.Id && x.Tint == mountTint))
                {
                    return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
                }

                var dbMount = new DbMount
                {
                    Id = ++lastMountId,

                    Tint = mountTint,
                    Definition = mountDefinition.Id,
                    IsUpgraded = mountDefinition.IsUpgradable // TODO: Implement training
                };

                dbCharacter.Mounts.Add(dbMount);

                var mountDef = mountDefinition;
                var savedMount = dbMount;

                pendingUpdates.Add(() =>
                {
                    if (!connection.Player.Mounts.Any(x => x.Definition == mountDef.Id && x.TintId == mountTint))
                    {
                        connection.Player.Mounts.Add(new PacketMountInfo
                        {
                            Id = savedMount.Id,
                            Definition = mountDef.Id,
                            NameId = mountDef.NameId,
                            ImageSetId = mountDef.ImageSetId,
                            TintId = mountTint,
                            MembersOnly = mountDef.MembersOnly,
                            IsUpgradable = mountDef.IsUpgradable,
                            IsUpgraded = savedMount.IsUpgraded
                        });
                    }

                    var packetMountList = new PacketMountList
                    {
                        Mounts = connection.Player.Mounts
                    };

                    connection.Player.SendTunneled(packetMountList);

                    ItemDefinitionHelper.SendItemDefinition(connection.Player, _resourceManager, clientItemDefinition);
                });
            }
            else
            {
                // TODO: Implement other item types

                return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
            }
        }

        if (dbCharacter.StationCash < totalCost)
        {
            packetInGamePurchasePlaceOrderResponse.Result = InGamePurchaseOrderResult.BalanceNotEnough;

            connection.SendTunneled(packetInGamePurchasePlaceOrderResponse);

            return true;
        }

        dbCharacter.StationCash -= totalCost;

        if (dbContext.SaveChanges() <= 0)
        {
            return SendOrderResult(connection, packetInGamePurchasePlaceOrderResponse, InGamePurchaseOrderResult.OrderFailure);
        }

        connection.Player.StationCash = dbCharacter.StationCash;

        foreach (var pendingUpdate in pendingUpdates)
            pendingUpdate();

        if (purchasedHousingInventory &&
            connection.Player.Zone is HousingZone housingZone &&
            housingZone.IsOwner(connection.Player))
        {
            housingZone.Runtime.RefreshFixtureItemList(connection.Player);
        }

        packetInGamePurchasePlaceOrderResponse.Result = InGamePurchaseOrderResult.Success;

        packetInGamePurchasePlaceOrderResponse.Total = totalCost;

        connection.SendTunneled(packetInGamePurchasePlaceOrderResponse);

        return true;
    }

    private static bool PurchaseHouse(
        GatewayConnection connection,
        PacketInGamePurchasePlaceOrderPacket packet,
        HousingZoneDefinition housingZoneDefinition,
        int totalCost,
        PacketInGamePurchasePlaceOrderResponse response)
    {
        if (totalCost < 0)
        {
            response.Result = InGamePurchaseOrderResult.OrderFailure;
            connection.SendTunneled(response);
            return true;
        }

        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
        var stationCash = 0;
        var result = 2;

        using var strategyContext = _dbContextFactory.CreateDbContext();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        try
        {
            strategy.ExecuteInTransaction(
                () =>
                {
                    strategyContext.ChangeTracker.Clear();

                    // Houses belong to the account, so a lot any of its characters has is already this character's.
                    if (HouseOwnership.OwnedByAccountOf(strategyContext, characterId).Any(house =>
                            house.ZoneDefinitionId == housingZoneDefinition.Id))
                    {
                        result = 2;
                        return;
                    }

                    // A free lot changes no balance, so only check the character is there.
                    var updated = totalCost == 0
                        ? strategyContext.Characters.Count(character => character.Id == characterId)
                        : strategyContext.Characters
                            .Where(character =>
                                character.Id == characterId &&
                                character.StationCash >= totalCost)
                            .ExecuteUpdate(setters => setters.SetProperty(
                                character => character.StationCash,
                                character => character.StationCash - totalCost));

                    if (updated != 1)
                    {
                        result = strategyContext.Characters.Any(character => character.Id == characterId) ? 5 : 2;
                        return;
                    }

                    strategyContext.Houses.Add(new DbHouse
                    {
                        CharacterId = characterId,
                        ZoneDefinitionId = housingZoneDefinition.Id
                    });

                    if (strategyContext.SaveChanges() <= 0)
                    {
                        result = 2;
                        return;
                    }

                    result = 1;
                },
                () =>
                {
                    if (result != 1)
                        return false;

                    using var verificationContext = _dbContextFactory.CreateDbContext();
                    return verificationContext.Houses.AsNoTracking().Any(house =>
                        house.CharacterId == characterId &&
                        house.ZoneDefinitionId == housingZoneDefinition.Id);
                },
                IsolationLevel.Serializable);

            if (result == 1)
            {
                using var resultContext = _dbContextFactory.CreateDbContext();
                stationCash = resultContext.Characters
                    .Where(character => character.Id == characterId)
                    .Select(character => character.StationCash)
                    .Single();
            }
        }
        catch (Exception exception) when (exception is DbUpdateException or RetryLimitExceededException)
        {
            _logger.LogWarning(exception, "Unable to purchase house {ZoneDefinitionId} for character {CharacterId}.", housingZoneDefinition.Id, characterId);
            result = 2;
        }

        if (result != 1)
        {
            response.Result = (InGamePurchaseOrderResult)result;
            connection.SendTunneled(response);

            // Most often refused because the lot is already owned; resend the list so "My Houses" shows it.
            ClientHousingPacketRequestPlayerHousesHandler.SendHouseList(connection);
            return true;
        }

        connection.Player.StationCash = stationCash;

        response.Result = InGamePurchaseOrderResult.Success;
        response.OrderTrackingId = packet.Order.OrderTrackingId;
        response.OrderId = packet.Order.OrderTrackingId.ToString();
        response.Total = totalCost;

        connection.SendTunneled(response);
        ClientHousingPacketRequestPlayerHousesHandler.SendHouseList(connection);

        return true;
    }

    private static bool SendOrderResult(GatewayConnection connection, PacketInGamePurchasePlaceOrderResponse packetInGamePurchasePlaceOrderResponse,
        InGamePurchaseOrderResult inGamePurchaseOrderResult)
    {
        packetInGamePurchasePlaceOrderResponse.Result = inGamePurchaseOrderResult;
        connection.SendTunneled(packetInGamePurchasePlaceOrderResponse);
        return true;
    }
}
