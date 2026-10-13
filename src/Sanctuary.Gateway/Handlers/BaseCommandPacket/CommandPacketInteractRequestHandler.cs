using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game;
using Sanctuary.Game.Collections;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class CommandPacketInteractRequestHandler
{
    private static ILogger _logger = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;
    private static IResourceManager _resourceManager = null!;
    private static IQuestManager _questManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(CommandPacketInteractRequestHandler));
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _questManager = serviceProvider.GetRequiredService<IQuestManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!CommandPacketInteractRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(CommandPacketInteractRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(CommandPacketInteractRequest), packet);

        if (!connection.Player.Zone.TryGetEntity(packet.Guid, out var entity))
            return true;

        if (entity is CollectionNode collectionNode)
            return HandleCollectionNode(connection, collectionNode);


        if (entity is Npc npc && _questManager.IsQuestNpc(npc.Guid))
        {
            connection.Player.LastInteractNpcGuid = npc.Guid;
            connection.Player.LastInteractAt = DateTime.UtcNow;
            _questManager.OnNpcInteract(connection.Player, npc);
            return true;
        }

        entity.OnInteract(connection.Player);
        return true;
    }

    private static bool HandleCollectionNode(GatewayConnection connection, CollectionNode node)
    {
        var playerPosition = connection.Player.Position;
        var nodePosition = node.Position;
        var distanceSquared = Vector3.DistanceSquared(
            new Vector3(playerPosition.X, playerPosition.Y, playerPosition.Z),
            new Vector3(nodePosition.X, nodePosition.Y, nodePosition.Z));

        if (distanceSquared > node.InteractRange * node.InteractRange)
            return true;

        if (node.SpawnDefinition.HasCollectionEntry)
            return HandleCollectionEntryNode(connection, node);

        if (!node.TryReserve())
            return true;

        if (!node.TypeDefinition.HasDrop)
        {
            // A node that exists for a quest's Collect goal (homework, ore): nothing to give, so just gather it.
            node.CompleteCollection();
            _questManager.OnCollectionNodeGathered(connection.Player, node);
            return true;
        }

        return HandleItemDropNode(connection, node);
    }

    /// <summary>
    /// An exploration coin: ticks one entry of its collection for this character, once, and then disappears for
    /// this character only. Nothing is paid; finishing the collection grants its reward items.
    /// </summary>
    private static bool HandleCollectionEntryNode(GatewayConnection connection, CollectionNode node)
    {
        var player = connection.Player;
        var entryId = node.SpawnDefinition.CollectionEntryId;

        if (!_resourceManager.Collections.TryGetEntry(entryId, out var match))
        {
            _logger.LogWarning("Collection node {nodeId} ({type}) names unknown collection entry {entryId}.",
                node.SpawnDefinition.Id, node.TypeDefinition.Key, entryId);
            return true;
        }

        var definition = match.Definition;
        var progress = CollectionProgress.From(player);

        // The set is the live one on the player, so adding here also claims the entry against a second click
        // before the database answers.
        if (!player.CollectedCollectionEntries.TryAdd(entryId))
            return true;

        var grantedRewards = new List<GrantedItem>();
        var completed = false;

        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var dbCharacter = dbContext.Characters
                .Include(character => character.Items)
                .SingleOrDefault(character => character.Id == player.CharacterId);

            if (dbCharacter is null)
            {
                player.CollectedCollectionEntries.TryRemove(entryId);
                return true;
            }

            dbCharacter.CollectionEntries.Add(new DbCharacterCollectionEntry { EntryId = entryId });

            // The progress now includes the entry, so this is "complete after this pickup".
            completed = definition.IsComplete(progress);

            if (completed)
                grantedRewards = GrantRewardItems(dbCharacter, definition);

            dbContext.SaveChanges();
        }
        catch (DbUpdateException ex)
        {
            // Already stored for this character (a relog raced the click, or two clicks landed together). The
            // in-memory set is right either way; just stop showing the coin.
            _logger.LogDebug(ex, "Collection entry {entryId} was already stored for character {characterId}.",
                entryId, player.CharacterId);
            node.HideFromCollector(player);
            return true;
        }
        catch (Exception ex)
        {
            player.CollectedCollectionEntries.TryRemove(entryId);
            _logger.LogError(ex, "Failed to collect node {nodeId} ({type}) for entry {entryId}.",
                node.SpawnDefinition.Id, node.TypeDefinition.Key, entryId);
            return false;
        }

        foreach (var granted in grantedRewards)
            SendItemToClient(connection, granted);

        node.HideFromCollector(player);

        _questManager.OnCollectionNodeGathered(player, node);

        // The client has every collection since login (SendSelfToClient), so this only ticks the entry.
        SendCollectionEntryUpdate(connection, definition, match.Entry, match.Index);

        if (completed)
            SendCollectionCompleted(connection, definition, grantedRewards, node.Guid);

        return true;
    }

    private static bool HandleItemDropNode(GatewayConnection connection, CollectionNode node)
    {
        var player = connection.Player;
        var rewardPersisted = false;
        var nodeCompleted = false;

        try
        {
            var drop = node.TypeDefinition.Table.SelectRandom();
            var itemDefinitionId = drop.ItemDefinitionId;

            if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
            {
                _logger.LogError("Collection node type {type} references unknown item definition {itemDefinitionId}.",
                    node.TypeDefinition.Key, itemDefinitionId);
                node.Release();
                return true;
            }

            var progress = CollectionProgress.From(player);
            var hasEntry = _resourceManager.Collections.TryGetEntryByItem(itemDefinitionId, out var match);
            var collectionEntryWasCollected = hasEntry && match.Entry.IsCollected(progress);

            using var dbContext = _dbContextFactory.CreateDbContext();
            var dbCharacter = dbContext.Characters
                .Include(character => character.Items)
                .SingleOrDefault(character => character.Id == player.CharacterId);

            if (dbCharacter is null)
            {
                node.Release();
                return true;
            }

            var dropped = AddItemToCharacter(dbCharacter, itemDefinition, tint: 0);

            // The owned set is the live one from the player, but the client item is only added below; count the
            // drop in by hand to see whether it finishes the collection.
            var ownedAfter = new HashSet<int>(progress.OwnedItemDefinitionIds) { itemDefinitionId };
            var progressAfter = progress with { OwnedItemDefinitionIds = ownedAfter };
            var completed = hasEntry && !collectionEntryWasCollected && match.Definition.IsComplete(progressAfter);
            var grantedRewards = completed ? GrantRewardItems(dbCharacter, match.Definition) : [];

            if (dbContext.SaveChanges() <= 0)
            {
                node.Release();
                return true;
            }

            rewardPersisted = true;

            var clientItem = SendItemToClient(connection, dropped);

            foreach (var granted in grantedRewards)
                SendItemToClient(connection, granted);

            node.CompleteCollection();
            nodeCompleted = true;

            _questManager.OnCollectionNodeGathered(player, node);

            if (hasEntry && !collectionEntryWasCollected)
            {
                SendCollectionEntryUpdate(connection, match.Definition, match.Entry, match.Index);

                if (completed)
                    SendCollectionCompleted(connection, match.Definition, grantedRewards, node.Guid);
            }
            else
            {
                SendCollectionRewardToast(connection, clientItem, itemDefinition, node);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect node {nodeId} ({type}).", node.SpawnDefinition.Id, node.TypeDefinition.Key);

            if (rewardPersisted && !nodeCompleted)
                node.CompleteCollection();
            else if (!rewardPersisted)
                node.Release();

            return false;
        }
    }

    private readonly record struct GrantedItem(DbItem Item, ClientItemDefinition Definition, bool Created);

    /// <summary>
    /// Adds one of the item to the character's rows (not yet saved): a new row, or one more on the stack.
    /// </summary>
    private static GrantedItem AddItemToCharacter(DbCharacter dbCharacter, ClientItemDefinition itemDefinition, int tint)
    {
        var dbItem = dbCharacter.Items.SingleOrDefault(item => item.Definition == itemDefinition.Id && item.Tint == tint);

        if (dbItem is not null)
        {
            dbItem.Count++;
            return new GrantedItem(dbItem, itemDefinition, Created: false);
        }

        dbItem = new DbItem
        {
            Id = dbCharacter.Items.Select(item => item.Id).DefaultIfEmpty(0).Max() + 1,
            Definition = itemDefinition.Id,
            Count = 1,
            Tint = tint
        };

        dbCharacter.Items.Add(dbItem);

        return new GrantedItem(dbItem, itemDefinition, Created: true);
    }

    /// <summary>
    /// The collection's reward items, added to the character's rows (not yet saved). Coins and experience in a
    /// collection's reward are not paid: the economy is a later task.
    /// </summary>
    private static List<GrantedItem> GrantRewardItems(DbCharacter dbCharacter, CollectionDefinition definition)
    {
        var granted = new List<GrantedItem>();

        foreach (var itemDefinitionId in definition.RewardItemIds)
        {
            if (!_resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var itemDefinition))
            {
                _logger.LogError("Collection {collectionId} rewards unknown item definition {itemDefinitionId}.",
                    definition.Id, itemDefinitionId);
                continue;
            }

            // The same tint rule as a quest reward.
            var tint = itemDefinition.IsTintable ? 0 : itemDefinition.Icon.TintId;
            granted.Add(AddItemToCharacter(dbCharacter, itemDefinition, tint));
        }

        return granted;
    }

    /// <summary>
    /// Mirrors a saved item row into the player's inventory and tells the client.
    /// </summary>
    private static ClientItem SendItemToClient(GatewayConnection connection, GrantedItem granted)
    {
        var dbItem = granted.Item;
        var clientItem = connection.Player.Items.SingleOrDefault(item =>
            item.Definition == dbItem.Definition && item.Tint == dbItem.Tint);

        if (clientItem is null)
        {
            clientItem = new ClientItem
            {
                Id = dbItem.Id,
                Definition = dbItem.Definition,
                Count = dbItem.Count,
                Tint = dbItem.Tint
            };

            connection.Player.Items.Add(clientItem);

            using var writer = new PacketWriter();
            clientItem.Serialize(writer);
            granted.Definition.Serialize(writer);

            connection.SendTunneled(new ClientUpdatePacketItemAdd { Payload = writer.Buffer });
        }
        else
        {
            clientItem.Count = dbItem.Count;
            connection.SendTunneled(new ClientUpdatePacketItemUpdate
            {
                ItemGuid = clientItem.Id,
                Count = clientItem.Count
            });
        }

        return clientItem;
    }

    private static ClientItemDefinition? LookupItemDefinition(int itemDefinitionId)
    {
        return _resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var definition) ? definition : null;
    }

    private static void SendCollectionEntryUpdate(GatewayConnection connection, CollectionDefinition definition,
        CollectionEntryDefinition entryDefinition, int index)
    {
        var entry = definition.CreateClientCollectionEntry(entryDefinition, index, true);

        connection.SendTunneled(new ClientUpdatePacketCollectionAddEntry
        {
            DefinitionId = entry.DefinitionId,
            IconId = entry.IconId,
            IconTintId = entry.IconTintId,
            NameId = entry.NameId,
            CollectionId = entry.CollectionId,
            Index = entry.Index,
            Unknown = entry.Unknown,
            Collected = entry.Collected
        });
    }

    /// <summary>
    /// The reward celebration for a finished collection: the collection's reward bundle, with the item guids the
    /// character just received, the way the quest turn-in and the pickup toast use the same packet.
    /// </summary>
    private static void SendCollectionCompleted(GatewayConnection connection, CollectionDefinition definition,
        IReadOnlyList<GrantedItem> grantedRewards, ulong sourceGuid)
    {
        var packet = new RewardBundlePacket();
        var bundle = packet.RewardBundle;

        bundle.Success = true;
        bundle.SourceGuid = sourceGuid;
        bundle.PlayerGuid = connection.Player.Guid;

        definition.FillRewardBundle(bundle, LookupItemDefinition);

        foreach (var entry in bundle.Entries.OfType<RewardBundleEntryItem>())
        {
            var granted = grantedRewards.FirstOrDefault(candidate => candidate.Item.Definition == entry.DefinitionId);

            if (granted.Item is null)
                continue;

            entry.Tint = granted.Item.Tint;
            entry.ItemGuid = granted.Item.Id;
        }

        connection.SendTunneled(packet);
    }

    private static void SendCollectionRewardToast(GatewayConnection connection, ClientItem clientItem,
        ClientItemDefinition itemDefinition, CollectionNode node)
    {
        var notificationId = Environment.TickCount & int.MaxValue;

        var packet = new RewardBundlePacket
        {
            Unknown8 = itemDefinition.DescriptionId
        };
        packet.RewardBundle.SourceGuid = node.Guid ^ (uint)notificationId;
        packet.RewardBundle.PlayerGuid = connection.Player.Guid;
        packet.RewardBundle.IconId = itemDefinition.Icon.Id;
        packet.RewardBundle.NameId = itemDefinition.NameId;
        packet.RewardBundle.Entries.Add(new RewardBundleEntryItem
        {
            IconId = itemDefinition.Icon.Id,
            NameId = itemDefinition.NameId,
            DefinitionId = clientItem.Definition,
            Tint = clientItem.Tint,
            ItemGuid = clientItem.Id
        });

        connection.SendTunneled(packet);
    }
}
