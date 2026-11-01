using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Game.Store;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class CoinStoreSellToClientRequestPacketHandler
{
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
        if (!CoinStoreSellToClientRequestPacket.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CoinStoreSellToClientRequestPacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(CoinStoreSellToClientRequestPacket), packet);

        var coinStoreTransactionCompletePacket = new CoinStoreTransactionCompletePacket
        {
            TransactionRecord =
            {
                Type = 1 // Buy
            }
        };

        if (packet.Quantity <= 0 ||
            !_resourceManager.ClientItemDefinitions.TryGetValue(packet.ItemRecord.Definition, out var clientItemDefinition))
        {
            _logger.LogWarning("User tried to buy unknown item definition. {guid} {definition}", connection.Player.Guid, packet.ItemRecord.Definition);

            coinStoreTransactionCompletePacket.Result = 3;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        if (!StoreInventoryPurchasePolicy.IsSupported(clientItemDefinition))
        {
            coinStoreTransactionCompletePacket.Result = 8;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        // No more than the client's quantity picker allows. A free item could otherwise be had by the billion in one
        // request.
        if (!StorePurchase.IsQuantityAllowed(clientItemDefinition, packet.Quantity))
        {
            coinStoreTransactionCompletePacket.Result = 4; // Invalid quantity

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        var cost = connection.Player.MembershipStatus == 0
            ? clientItemDefinition.Cost
            : clientItemDefinition.GetMemberPurchasePrice();

        var totalCostValue = (long)cost * packet.Quantity;
        if (cost < 0 || totalCostValue > int.MaxValue)
        {
            coinStoreTransactionCompletePacket.Result = 8;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        if (!_resourceManager.CoinStoreItems.TryGetValue(clientItemDefinition.Id, out var coinStoreItem))
        {
            coinStoreTransactionCompletePacket.Result = 8;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        var totalCost = (int)totalCostValue;

        if (connection.Player.Coins < totalCost)
        {
            coinStoreTransactionCompletePacket.Result = 7;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        var isHousingInventory = StoreInventoryPurchasePolicy.IsHousingInventoryItem(clientItemDefinition);
        var tint = isHousingInventory
            ? StoreInventoryPurchasePolicy.ResolveHousingTint(clientItemDefinition, packet.ItemRecord.Tint)
            : clientItemDefinition.IsTintable
                ? packet.ItemRecord.Tint
                : clientItemDefinition.Icon.TintId;

        // Each colour of an item is an inventory row of its own, so an item comes only in a colour the store offers it in.
        // A made-up tint was a new row on every request, free or for a few coins, and every login loads them all. The
        // colour picker sends no other tint, whatever the price.
        if (!StorePurchase.IsOfferedTint(clientItemDefinition, coinStoreItem.TintGroupId, tint))
        {
            coinStoreTransactionCompletePacket.Result = 3; // Invalid item

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        // An item sold in several colours has its stack limit in each, as upstream's store counts it. The client counts
        // every colour together, so a real request is within it either way.
        var hasTintGroup = coinStoreItem.TintGroupId != 0;

        using var dbContext = _dbContextFactory.CreateDbContext();

        var dbQuery = dbContext.Characters
            .Where(x => x.Id == GuidHelper.GetPlayerId(connection.Player.Guid))
            .Select(x => new
            {
                Character = x,
                Item = x.Items.SingleOrDefault(i => i.Definition == clientItemDefinition.Id && i.Tint == tint),
                OwnedCount = x.Items.Where(i => i.Definition == clientItemDefinition.Id && (!hasTintGroup || i.Tint == tint)).Sum(i => (long?)i.Count) ?? 0,
                NextId = x.Items.Select(i => (int?)i.Id).Max() ?? 0
            })
            .SingleOrDefault();

        if (dbQuery is null)
        {
            coinStoreTransactionCompletePacket.Result = 8;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        // A stack limit counts what the character already owns. It held for one request only, so a limited item (the
        // free Domo Hat, say) could be bought one request at a time without end.
        if (!StorePurchase.IsWithinStackLimit(clientItemDefinition, dbQuery.OwnedCount, packet.Quantity))
        {
            coinStoreTransactionCompletePacket.Result = 4; // Invalid quantity

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        var dbItem = dbQuery.Item;

        if (dbItem is not null)
        {
            if (dbItem.Count < 0 || dbItem.Count > int.MaxValue - packet.Quantity)
            {
                coinStoreTransactionCompletePacket.Result = 8;

                connection.SendTunneled(coinStoreTransactionCompletePacket);

                return true;
            }

            dbItem.Count += packet.Quantity;
        }
        else
        {
            if (dbQuery.NextId == int.MaxValue)
            {
                coinStoreTransactionCompletePacket.Result = 8;

                connection.SendTunneled(coinStoreTransactionCompletePacket);

                return true;
            }

            dbItem = new DbItem
            {
                Id = dbQuery.NextId + 1,
                Definition = clientItemDefinition.Id,
                Tint = tint,

                Count = packet.Quantity
            };

            dbQuery.Character.Items.Add(dbItem);
        }

        if (dbQuery.Character.Coins < totalCost)
        {
            coinStoreTransactionCompletePacket.Result = 7;

            connection.SendTunneled(coinStoreTransactionCompletePacket);

            return true;
        }

        dbQuery.Character.Coins -= totalCost;

        if (dbContext.SaveChanges() <= 0)
        {
            coinStoreTransactionCompletePacket.Result = 8;

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

        coinStoreTransactionCompletePacket.Result = 1;

        coinStoreTransactionCompletePacket.ItemGuid = clientItem.Id;

        coinStoreTransactionCompletePacket.TransactionRecord.Type = 1; // Buy

        coinStoreTransactionCompletePacket.TransactionRecord.Id = connection.Player.CoinStoreTransactions.Count + 1;

        coinStoreTransactionCompletePacket.TransactionRecord.Timestamp = DateTimeOffset.UtcNow;

        coinStoreTransactionCompletePacket.TransactionRecord.ItemRecord.Definition = clientItem.Definition;
        coinStoreTransactionCompletePacket.TransactionRecord.ItemRecord.Tint = clientItem.Tint;

        coinStoreTransactionCompletePacket.TransactionRecord.Quantity = packet.Quantity;

        connection.SendTunneled(coinStoreTransactionCompletePacket);

        connection.Player.CoinStoreTransactions.Add(coinStoreTransactionCompletePacket.TransactionRecord);

        return true;
    }
}
