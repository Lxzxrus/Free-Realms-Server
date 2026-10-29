using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class CoinStoreSellToClientRequestPacketHandler
{
    private const int NoTintGroupId = 0;

    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(CoinStoreSellToClientRequestPacketHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!CoinStoreSellToClientRequestPacket.TryDeserialize(data, out var coinStoreSellToClientRequestPacket))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CoinStoreSellToClientRequestPacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(CoinStoreSellToClientRequestPacket), coinStoreSellToClientRequestPacket);

        var coinStoreTransactionCompletePacket = new CoinStoreTransactionCompletePacket
        {
            TransactionRecord =
            {
                Type = CoinStoreTransactionType.Buy
            }
        };

        if (coinStoreSellToClientRequestPacket.Quantity <= 0 ||
            !_resourceManager.ClientItemDefinitions.TryGetValue(coinStoreSellToClientRequestPacket.ItemRecord.Definition, out var clientItemDefinition))
        {
            _logger.LogWarning("User tried to buy unknown item definition. {guid} {definition}", connection.Player.Guid, coinStoreSellToClientRequestPacket.ItemRecord.Definition);

            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.InvalidItem;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        // Evergrove's store policy (task 21: clothing, consumables and housing inventory), plus upstream's pets.
        if (!StoreInventoryPurchasePolicy.IsSupported(clientItemDefinition) &&
            clientItemDefinition.Type != ItemDefinitionHelper.PetItemType &&
            clientItemDefinition.Type != ItemDefinitionHelper.PetClothingItemType &&
            clientItemDefinition.Type != ItemDefinitionHelper.PetUtilityItemType)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Error;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        var cost = connection.Player.MembershipStatus == 0
            ? clientItemDefinition.Cost
            : clientItemDefinition.GetMemberPurchasePrice();

        var totalCostValue = (long)cost * coinStoreSellToClientRequestPacket.Quantity;
        if (cost < 0 || totalCostValue > int.MaxValue)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Error;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        // Only what the coin store sells can be bought, whatever item the client names.
        if (!_resourceManager.CoinStoreItems.ContainsKey(clientItemDefinition.Id))
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Error;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        var totalCost = (int)totalCostValue;

        if (connection.Player.Coins < totalCost)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.NotEnoughCoins;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        var isHousingInventory = StoreInventoryPurchasePolicy.IsHousingInventoryItem(clientItemDefinition);

        var tint = coinStoreSellToClientRequestPacket.ItemRecord.Tint;

        var hasTintGroup = _resourceManager.CoinStoreItems.TryGetValue(clientItemDefinition.Id, out var coinStoreItem)
            && coinStoreItem.TintGroupId != NoTintGroupId;

        if (isHousingInventory)
            tint = StoreInventoryPurchasePolicy.ResolveHousingTint(clientItemDefinition, coinStoreSellToClientRequestPacket.ItemRecord.Tint);
        else if (!hasTintGroup)
            tint = clientItemDefinition.Icon.TintId;

        if (clientItemDefinition.Type == ItemDefinitionHelper.PetItemType)
            return BuyPet(connection, coinStoreSellToClientRequestPacket, coinStoreTransactionCompletePacket, clientItemDefinition, tint, hasTintGroup, totalCost);

        using var dbContext = _dbContextFactory.CreateDbContext();

        var dbQuery = dbContext.Characters
            .Where(x => x.Id == GuidHelper.GetPlayerId(connection.Player.Guid))
            .Select(x => new
            {
                Character = x,
                Item = x.Items.SingleOrDefault(i => i.Definition == clientItemDefinition.Id && i.Tint == tint),
                OwnedCount = x.Items.Where(i => i.Definition == clientItemDefinition.Id && (!hasTintGroup || i.Tint == tint)).Sum(i => (int?)i.Count) ?? 0,
                NextId = x.Items.Select(i => (int?)i.Id).Max() ?? 0
            })
            .SingleOrDefault();

        if (dbQuery is null)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Error;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        if (clientItemDefinition.MaxStackSize > 0
            && dbQuery.OwnedCount + coinStoreSellToClientRequestPacket.Quantity > clientItemDefinition.MaxStackSize)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.InvalidQuantity;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        var dbItem = dbQuery.Item;

        if (dbItem is not null)
        {
            if (dbItem.Count < 0 || dbItem.Count > int.MaxValue - coinStoreSellToClientRequestPacket.Quantity)
            {
                coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Error;

                connection.SendTunneled(coinStoreTransactionCompletePacket);

                return true;
            }

            dbItem.Count += coinStoreSellToClientRequestPacket.Quantity;
        }
        else
        {
            if (dbQuery.NextId == int.MaxValue)
            {
                coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Error;

                connection.SendTunneled(coinStoreTransactionCompletePacket);

                return true;
            }

            dbItem = new DbItem
            {
                Id = dbQuery.NextId + 1,
                Definition = clientItemDefinition.Id,
                Tint = tint,

                Count = coinStoreSellToClientRequestPacket.Quantity
            };

            dbQuery.Character.Items.Add(dbItem);
        }

        if (dbQuery.Character.Coins < totalCost)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.NotEnoughCoins;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        dbQuery.Character.Coins -= totalCost;

        if (dbContext.SaveChanges() <= 0)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Error;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        var clientItem = connection.Player.Items.SingleOrDefault(x => x.Definition == clientItemDefinition.Id && x.Tint == tint);

        var addItem = false;

        if (clientItem is not null)
        {
            clientItem.Tint = dbItem.Tint;
            clientItem.Count = dbItem.Count;
        }
        else
        {
            addItem = true;

            clientItem = new ClientItem
            {
                Id = dbItem.Id,
                Tint = dbItem.Tint,
                Count = dbItem.Count,
                Definition = dbItem.Definition
            };

            connection.Player.Items.Add(clientItem);
        }

        connection.Player.Coins = dbQuery.Character.Coins;

        if (addItem)
        {
            using var writer = new PacketWriter();

            clientItem.Serialize(writer);

            clientItemDefinition.Serialize(writer);

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

        var clientUpdatePacketCoinCount = new ClientUpdatePacketCoinCount
        {
            Coins = connection.Player.Coins
        };

        connection.SendTunneled(clientUpdatePacketCoinCount);

        if (isHousingInventory &&
            connection.Player.Zone is HousingZone housingZone &&
            housingZone.IsOwner(connection.Player))
        {
            housingZone.Runtime.RefreshFixtureItemList(connection.Player);
        }

        coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Success;

        coinStoreTransactionCompletePacket.ItemGuid = clientItem.Id;

        coinStoreTransactionCompletePacket.TransactionRecord.Type = CoinStoreTransactionType.Buy;

        coinStoreTransactionCompletePacket.TransactionRecord.Id = connection.Player.CoinStoreTransactions.Count + 1;

        coinStoreTransactionCompletePacket.TransactionRecord.Timestamp = DateTimeOffset.UtcNow;

        coinStoreTransactionCompletePacket.TransactionRecord.ItemRecord.Definition = clientItem.Definition;
        coinStoreTransactionCompletePacket.TransactionRecord.ItemRecord.Tint = clientItem.Tint;

        coinStoreTransactionCompletePacket.TransactionRecord.Quantity = coinStoreSellToClientRequestPacket.Quantity;

        connection.SendTunneled(coinStoreTransactionCompletePacket);

        connection.Player.CoinStoreTransactions.Add(coinStoreTransactionCompletePacket.TransactionRecord);

        return true;
    }

    private static bool BuyPet(GatewayConnection connection, CoinStoreSellToClientRequestPacket coinStoreSellToClientRequestPacket,
        CoinStoreTransactionCompletePacket coinStoreTransactionCompletePacket, ClientItemDefinition clientItemDefinition, int tint,
        bool hasTintGroup, int totalCost)
    {
        if (coinStoreSellToClientRequestPacket.Quantity <= 0)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.InvalidQuantity;
            connection.SendTunneled(coinStoreTransactionCompletePacket);
            return true;
        }

        // Evergrove: a pet with no coin price (29 in the coin store, among them Domo, the Marshmallows, Landshark and the
        // pigs) isn't sold for coins. Free, it would hand out any number of pets, a database row each.
        if (totalCost <= 0)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.InvalidItem;
            connection.SendTunneled(coinStoreTransactionCompletePacket);
            return true;
        }

        if (!PetHelper.TryGetModel(_resourceManager, clientItemDefinition, out var modelDefinition))
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.InvalidItem;
            connection.SendTunneled(coinStoreTransactionCompletePacket);
            return true;
        }

        using var dbContext = _dbContextFactory.CreateDbContext();
        var characterId = GuidHelper.GetPlayerId(connection.Player.Guid);
        var dbCharacter = dbContext.Characters.SingleOrDefault(character => character.Id == characterId);

        if (dbCharacter is null)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Error;
            connection.SendTunneled(coinStoreTransactionCompletePacket);
            return true;
        }

        if (clientItemDefinition.MaxStackSize > 0
            && dbContext.Pets.Count(pet => pet.CharacterId == characterId && pet.Definition == clientItemDefinition.Id && (!hasTintGroup || pet.Tint == tint))
                + coinStoreSellToClientRequestPacket.Quantity > clientItemDefinition.MaxStackSize)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.InvalidQuantity;
            connection.SendTunneled(coinStoreTransactionCompletePacket);
            return true;
        }

        var lastPetId = dbContext.Pets.Where(pet => pet.CharacterId == characterId)
            .Max(pet => (int?)pet.Id) ?? 0;

        if (coinStoreSellToClientRequestPacket.Quantity > int.MaxValue - lastPetId)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Error;
            connection.SendTunneled(coinStoreTransactionCompletePacket);
            return true;
        }

        var pets = new List<DbPet>();

        for (var i = 0; i < coinStoreSellToClientRequestPacket.Quantity; i++)
        {
            var dbPet = PetHelper.CreatePet(++lastPetId, clientItemDefinition.Id, tint);
            dbCharacter.Pets.Add(dbPet);
            pets.Add(dbPet);
        }

        dbCharacter.Coins -= totalCost;

        if (dbContext.SaveChanges() <= 0)
        {
            coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Error;
            connection.SendTunneled(coinStoreTransactionCompletePacket);
            return true;
        }

        foreach (var dbPet in pets)
            PetHelper.AddPet(connection.Player, _resourceManager, dbPet, clientItemDefinition, modelDefinition);

        connection.Player.Coins = dbCharacter.Coins;
        connection.Player.PlayerPet.SendPetList();
        connection.SendTunneled(new ClientUpdatePacketCoinCount { Coins = dbCharacter.Coins });

        coinStoreTransactionCompletePacket.Result = CoinStoreTransactionResult.Success;
        coinStoreTransactionCompletePacket.ItemGuid = pets[0].Id;
        coinStoreTransactionCompletePacket.TransactionRecord.Id = connection.Player.CoinStoreTransactions.Count + 1;
        coinStoreTransactionCompletePacket.TransactionRecord.Timestamp = DateTimeOffset.UtcNow;
        coinStoreTransactionCompletePacket.TransactionRecord.ItemRecord.Definition = clientItemDefinition.Id;
        coinStoreTransactionCompletePacket.TransactionRecord.ItemRecord.Tint = tint;
        coinStoreTransactionCompletePacket.TransactionRecord.Quantity = coinStoreSellToClientRequestPacket.Quantity;
        coinStoreTransactionCompletePacket.TransactionRecord.Price = totalCost;
        connection.SendTunneled(coinStoreTransactionCompletePacket);
        connection.Player.CoinStoreTransactions.Add(coinStoreTransactionCompletePacket.TransactionRecord);

        return true;
    }
}
