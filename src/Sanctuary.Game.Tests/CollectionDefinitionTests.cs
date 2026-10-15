using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Game.Collections;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class CollectionDefinitionTests
{
    // The character in the 2014 SendSelf capture (possible_character_data.pcapng).
    private const ulong CapturedPlayerGuid = 0x4B52CC6A9573CA91;

    private static CollectionProgress Progress(IEnumerable<int>? ownedItems = null, IEnumerable<int>? collectedEntries = null)
    {
        return new CollectionProgress(
            new HashSet<int>(ownedItems ?? []),
            new HashSet<int>(collectedEntries ?? []));
    }

    private static ClientItemDefinition? NoItems(int itemDefinitionId) => null;

    [TestMethod]
    public void CreateClientCollection_DerivesProgressFromOwnedItems()
    {
        var definition = new CollectionDefinition
        {
            Id = 17054,
            NameId = 17055,
            CategoryId = 10,
            Entries =
            [
                new CollectionEntryDefinition { Id = 41, ItemDefinitionId = 11081 },
                new CollectionEntryDefinition { Id = 42, ItemDefinitionId = 11082 }
            ]
        };
        var progress = Progress(ownedItems: [11082]);

        var clientCollection = definition.CreateClientCollection(123, progress, NoItems);

        Assert.IsTrue(definition.IsStarted(progress));
        Assert.IsFalse(definition.IsComplete(progress));
        Assert.IsFalse(clientCollection.Entries[0].Collected);
        Assert.IsTrue(clientCollection.Entries[1].Collected);
        Assert.AreEqual(123ul, clientCollection.PlayerGuid);
    }

    [TestMethod]
    public void DirectEntries_AreCollectedByEntryIdNotByItem()
    {
        var definition = new CollectionDefinition
        {
            Id = 267,
            NameId = 386336,
            CategoryId = 1,
            Entries =
            [
                new CollectionEntryDefinition { Id = 1937 },
                new CollectionEntryDefinition { Id = 1956 }
            ]
        };

        Assert.IsTrue(definition.Entries.All(entry => entry.IsDirect));
        Assert.IsFalse(definition.IsStarted(Progress(ownedItems: [1937])), "An item id must not count as an entry id.");

        var oneCoin = Progress(collectedEntries: [1937]);
        Assert.IsTrue(definition.IsStarted(oneCoin));
        Assert.IsFalse(definition.IsComplete(oneCoin));
        Assert.IsTrue(definition.IsComplete(Progress(collectedEntries: [1937, 1956])));

        Assert.IsTrue(definition.TryGetEntry(1956, out var entry, out var index));
        Assert.AreEqual(1956, entry.Id);
        Assert.AreEqual(1, index);
        Assert.IsFalse(definition.TryGetEntry(1, out _, out _));
    }

    [TestMethod]
    public void CreateClientCollectionEntry_UsesCollectionMetadata()
    {
        var definition = new CollectionDefinition
        {
            Id = 17054,
            NameId = 17055,
            CategoryId = 10
        };
        var entry = new CollectionEntryDefinition
        {
            Id = 41,
            NameId = 17056,
            IconId = 2124,
            IconTintId = 99
        };

        var clientEntry = definition.CreateClientCollectionEntry(entry, 2, true);

        Assert.AreEqual(entry.Id, clientEntry.Id);
        Assert.AreEqual(entry.Id, clientEntry.DefinitionId);
        Assert.AreEqual(3, clientEntry.Index);
        Assert.AreEqual(definition.Id, clientEntry.CollectionId);
        Assert.AreEqual(entry.NameId, clientEntry.NameId);
        Assert.AreEqual(entry.IconId, clientEntry.IconId);
        Assert.AreEqual(entry.IconTintId, clientEntry.IconTintId);
        Assert.IsTrue(clientEntry.Collected);
    }

    [TestMethod]
    public void FillRewardBundle_ListsExperienceThenItems()
    {
        var definition = new CollectionDefinition
        {
            Id = 1,
            RewardCoins = 9,
            RewardExperience = 50,
            RewardProfileId = 11,
            RewardItemIds = [17820, 15322]
        };
        var items = new Dictionary<int, ClientItemDefinition>
        {
            [17820] = new() { Id = 17820, NameId = 2630, Icon = new IconData { Id = 430, TintId = 235 } },
            [15322] = new() { Id = 15322, NameId = 29137, Icon = new IconData { Id = 3516, TintId = 235 } }
        };

        var bundle = new RewardBundleBase();
        definition.FillRewardBundle(bundle, id => items.GetValueOrDefault(id));

        Assert.AreEqual(9, bundle.Coins, "Coins travel in the bundle's first int.");
        Assert.AreEqual(430, bundle.IconId, "The bundle shows the first reward item.");
        Assert.AreEqual(2630, bundle.NameId);
        Assert.AreEqual(3, bundle.Entries.Count);

        var experience = (RewardBundleEntryExperience)bundle.Entries[0];
        Assert.AreEqual(50, experience.Quantity);
        Assert.AreEqual(11, experience.DefinitionId);

        var hat = (RewardBundleEntryItem)bundle.Entries[1];
        Assert.AreEqual(17820, hat.DefinitionId);
        Assert.AreEqual(430, hat.IconId);
        Assert.AreEqual(235, hat.TintId);
        Assert.AreEqual(2630, hat.NameId);
        Assert.AreEqual(1, hat.Quantity);
        Assert.AreEqual(15322, ((RewardBundleEntryItem)bundle.Entries[2]).DefinitionId);
    }

    /// <summary>
    /// The Briarwood mushroom collection, serialized for the captured character with the four entries that
    /// character had, must match the capture's row byte for byte. Its reward is 9 coins and 50 experience.
    /// </summary>
    [TestMethod]
    public void ClientCollection_MatchesTheCapturedBriarwoodRow()
    {
        const string captured =
            "0a0000009e4200009f420000030000004c0800005c00000001000000010900000000000000000000000f000000000000000000000000" +
            "00803f0000000000000000000000000000000091ca73956acc524b3d0f0000700900000100000003000000003d0f000000000000" +
            "70090000320000000b00000000000000000000000000000000e4150000080000002900000029000000010000000a000000a0420000" +
            "4c0800006300000000000000012a0000002a000000020000000a000000a14200004c0800006000000000000000002b0000002b0000" +
            "00030000000a000000a24200004c0800007000000000000000012c0000002c000000040000000a000000a34200004c080000640000" +
            "0000000000002d0000002d000000050000000a000000a44200004c0800005c00000000000000012e0000002e000000060000000a00" +
            "0000a54200004c0800006800000000000000002f0000002f000000070000000a000000a64200004c0800006d000000000000000130" +
            "00000030000000080000000a000000a74200004c080000650000000000000000";

        var definition = new CollectionDefinition
        {
            Id = 10,
            NameId = 17054,
            CategoryId = 3,
            DescriptionId = 17055,
            IconId = 2124,
            IconTintId = 92,
            RewardCoins = 9,
            RewardExperience = 50,
            RewardProfileId = 11,
            RewardTextId = 5604,
            Entries =
            [
                new() { Id = 41, NameId = 17056, IconId = 2124, IconTintId = 99, ItemDefinitionId = 11081 },
                new() { Id = 42, NameId = 17057, IconId = 2124, IconTintId = 96, ItemDefinitionId = 11082 },
                new() { Id = 43, NameId = 17058, IconId = 2124, IconTintId = 112, ItemDefinitionId = 11083 },
                new() { Id = 44, NameId = 17059, IconId = 2124, IconTintId = 100, ItemDefinitionId = 11084 },
                new() { Id = 45, NameId = 17060, IconId = 2124, IconTintId = 92, ItemDefinitionId = 11085 },
                new() { Id = 46, NameId = 17061, IconId = 2124, IconTintId = 104, ItemDefinitionId = 11086 },
                new() { Id = 47, NameId = 17062, IconId = 2124, IconTintId = 109, ItemDefinitionId = 11087 },
                new() { Id = 48, NameId = 17063, IconId = 2124, IconTintId = 101, ItemDefinitionId = 11088 }
            ]
        };

        var progress = Progress(ownedItems: [11081, 11083, 11085, 11087]);
        var collection = definition.CreateClientCollection(CapturedPlayerGuid, progress, NoItems);

        Assert.AreEqual(captured, Serialize(collection));
    }

    /// <summary>
    /// The Seaside Rare Exploration collection: five direct entries, all collected, and Traveler's Boots as the
    /// reward, must match the capture's row byte for byte.
    /// </summary>
    [TestMethod]
    public void ClientCollection_MatchesTheCapturedSeasideRareExplorationRow()
    {
        const string captured =
            "0b01000020e505001ee5050001000000bd0800000000000001000000010000000000000000000000000f00000000000000000000000000" +
            "803f0000000000000000000000000000000091ca73956acc524b3c0e00003c0a00000100000001000000003c0e0000ea0000003c0a0000" +
            "01000000944300000000000000000000000000000000000000bb140000050000009107000091070000010000000b01000022e50500bd08" +
            "0000000000000000000001a4070000a4070000020000000b0100009be50500bd080000000000000000000001a5070000a5070000030000" +
            "000b0100009ce50500bd080000000000000000000001a6070000a6070000040000000b0100009de50500bd080000000000000000000001" +
            "a7070000a7070000050000000b0100009ee50500bd080000000000000000000001";

        var definition = new CollectionDefinition
        {
            Id = 267,
            NameId = 386336,
            CategoryId = 1,
            DescriptionId = 386334,
            IconId = 2237,
            IconTintId = 0,
            RewardTextId = 5307,
            RewardItemIds = [17300],
            Entries =
            [
                new() { Id = 1937, NameId = 386338, IconId = 2237 },
                new() { Id = 1956, NameId = 386459, IconId = 2237 },
                new() { Id = 1957, NameId = 386460, IconId = 2237 },
                new() { Id = 1958, NameId = 386461, IconId = 2237 },
                new() { Id = 1959, NameId = 386462, IconId = 2237 }
            ]
        };
        var boots = new ClientItemDefinition { Id = 17300, NameId = 2620, Icon = new IconData { Id = 3644, TintId = 234 } };

        var progress = Progress(collectedEntries: definition.Entries.Select(entry => entry.Id));
        var collection = definition.CreateClientCollection(CapturedPlayerGuid, progress,
            id => id == 17300 ? boots : null);

        Assert.AreEqual(captured, Serialize(collection));
    }

    private static string Serialize(ClientCollection collection)
    {
        using var writer = new PacketWriter();
        collection.Serialize(writer);
        return Convert.ToHexString(writer.Buffer).ToLowerInvariant();
    }
}
