using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The Rare and Elite Exploration collections, taken from the 2014 SendSelf capture, and the coins that fill them.
/// Every coin spawn names the entry it ticks; a character collects each entry once and pays nothing.
/// </summary>
[TestClass]
public sealed class ExplorationCollectionTests
{
    // Collection id -> (coin pool region, entry count, reward item ids). Ids, counts and rewards are the capture's;
    // the regions are the wiki's and match the capture's region icons. The four Wilds quarters share Sanctuary's
    // coin model, so their coins live in the sanctuary pool.
    private static readonly Dictionary<int, (string Region, int Entries, int[] Rewards)> Rare = new()
    {
        [265] = ("snowhill", 5, [18040]),
        [266] = ("sanctuary", 6, [17460]),
        [267] = ("seaside", 5, [17300]),
        [268] = ("blackspore", 5, [17370]),
        [269] = ("briarwood", 5, [18219]),
        [270] = ("wugachug", 3, [18267]),
        [271] = ("merry-vale", 3, [17210]),
        [272] = ("shrouded-glade", 3, [15322, 17820]),
        [273] = ("sanctuary", 5, [1496]),
        [274] = ("sanctuary", 5, [17271]),
        [275] = ("sanctuary", 5, [15248, 17786]),
        [276] = ("sanctuary", 5, [15281, 17932]),

        // Not in the capture: names from a video of a completed collections window (2026-10-12). Its reward is
        // shown only as a picture, so none is granted yet.
        [9001] = ("sunstone-valley", 8, [])
    };

    private static readonly Dictionary<int, (string Region, int Entries, int[] Rewards)> Elite = new()
    {
        [284] = ("snowhill", 8, [70341]),
        [285] = ("wugachug", 5, [70342]),
        [286] = ("shrouded-glade", 5, [70343]),
        [287] = ("briarwood", 8, [70344]),
        [288] = ("blackspore", 8, [70345]),
        [289] = ("seaside", 8, [70346]),
        [290] = ("merry-vale", 5, [70347]),
        [291] = ("sanctuary", 8, [70348]),
        [309] = ("sacred-grove", 8, [70349]),
        [9002] = ("sunstone-valley", 8, [])
    };

    // Entries the 2014 npc list has no coin for: two in Snowhill, two in Sanctuary, one in the Wilds Northeast,
    // one Blackspore Elite, all eight Sacred Grove Elite (that hub isn't in our world), and one of each Sunstone
    // Valley collection (eight entries, seven coins).
    private static readonly int[] EntriesWithoutACoin =
        [1949, 1950, 1954, 1955, 1985, 2045, 2170, 2171, 2172, 2173, 2174, 2175, 2176, 2177, 90008, 90018];

    private static string FindSourcePath(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);

            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"src/{string.Join('/', relativePath)} not found above the test output.");
    }

    private static CollectionDefinitionCollection LoadCollections()
    {
        var collections = new CollectionDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(collections.Load(FindSourcePath("Resources", "Collections.json")));
        return collections;
    }

    private static CollectionNodeSpawnDefinitionCollection LoadSpawns()
    {
        var spawns = new CollectionNodeSpawnDefinitionCollection(NullLogger.Instance);
        var resources = Path.GetDirectoryName(FindSourcePath("Resources", "CollectionNodePools.json"))!;
        Assert.IsTrue(spawns.Load(Path.Combine(resources, "CollectionNodeSpawns")));
        return spawns;
    }

    private static IEnumerable<CollectionNodeSpawnDefinition> CoinSpawns(CollectionNodeSpawnDefinitionCollection spawns)
    {
        return spawns.Values.Where(spawn => spawn.Pool.Contains("-exploration-coins-", StringComparison.Ordinal));
    }

    private static (string Region, string Kind) PoolOf(CollectionNodeSpawnDefinition spawn)
    {
        var separator = spawn.Pool.LastIndexOf("-exploration-coins-", StringComparison.Ordinal);
        return (spawn.Pool[..separator], spawn.Pool[(separator + "-exploration-coins-".Length)..]);
    }

    [TestMethod]
    public void Collections_HoldEveryExplorationCollectionFromTheCapture()
    {
        var collections = LoadCollections();
        var items = new ClientItemDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(items.Load(FindSourcePath("Resources", "ClientItemDefinitions.json")));

        foreach (var (id, expected) in Rare.Concat(Elite))
        {
            Assert.IsTrue(collections.TryGetValue(id, out var collection), $"Collection {id} is missing.");
            Assert.AreEqual(1, collection.CategoryId, $"Collection {id} isn't in the exploration category.");
            Assert.AreEqual(expected.Entries, collection.Entries.Count, $"Collection {id} has the wrong entry count.");
            Assert.IsTrue(collection.Entries.All(entry => entry.IsDirect), $"Collection {id} has an item-based entry.");
            CollectionAssert.AreEqual(expected.Rewards, collection.RewardItemIds, $"Collection {id} rewards the wrong items.");
            Assert.AreEqual(0, collection.RewardCoins, $"Collection {id} pays coins.");

            foreach (var itemId in collection.RewardItemIds)
                Assert.IsTrue(items.ContainsKey(itemId), $"Collection {id} rewards unknown item {itemId}.");
        }
    }

    [TestMethod]
    public void EveryCoin_TicksOneEntryOfItsRegionsCollection()
    {
        var collections = LoadCollections();
        var spawns = LoadSpawns();
        var seen = new Dictionary<int, int>();

        foreach (var spawn in CoinSpawns(spawns).Where(spawn => spawn.HasCollectionEntry))
        {
            var (region, kind) = PoolOf(spawn);

            Assert.IsTrue(collections.TryGetEntry(spawn.CollectionEntryId, out var match),
                $"Coin {spawn.Id} names unknown entry {spawn.CollectionEntryId}.");
            Assert.IsTrue(match.Entry.IsDirect, $"Coin {spawn.Id} names an item-based entry.");
            Assert.IsFalse(seen.TryGetValue(spawn.CollectionEntryId, out var other),
                $"Coins {other} and {spawn.Id} both tick entry {spawn.CollectionEntryId}.");
            seen[spawn.CollectionEntryId] = spawn.Id;

            var table = kind == "rare" ? Rare : Elite;
            Assert.IsTrue(table.TryGetValue(match.Definition.Id, out var expected),
                $"Coin {spawn.Id} ({kind}) ticks collection {match.Definition.Id}, which is not a {kind} collection.");
            Assert.AreEqual(expected.Region, region,
                $"Coin {spawn.Id} in {region} ticks collection {match.Definition.Id}, which belongs to {expected.Region}.");
        }

        Assert.AreEqual(118, seen.Count, "The number of coins with an entry changed.");
    }

    [TestMethod]
    public void EveryEntry_HasACoinExceptTheKnownGaps()
    {
        var collections = LoadCollections();
        var spawns = LoadSpawns();
        var entriesWithCoins = CoinSpawns(spawns).Select(spawn => spawn.CollectionEntryId).ToHashSet();

        var missing = Rare.Keys.Concat(Elite.Keys)
            .SelectMany(id => collections[id].Entries)
            .Where(entry => !entriesWithCoins.Contains(entry.Id))
            .Select(entry => entry.Id)
            .Order()
            .ToArray();

        CollectionAssert.AreEqual(EntriesWithoutACoin, missing,
            "Entries without a coin: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void EveryCoin_HasAnEntry()
    {
        var withoutEntry = CoinSpawns(LoadSpawns()).Where(spawn => !spawn.HasCollectionEntry).Select(spawn => spawn.Id);

        Assert.AreEqual(0, withoutEntry.Count(), "Coins without a collection entry: " + string.Join(", ", withoutEntry));
    }

    [TestMethod]
    public void SpawnFiles_KeepTheEntryIdWhenRewritten()
    {
        var spawns = LoadSpawns();
        var coin = CoinSpawns(spawns).First(spawn => spawn.HasCollectionEntry);

        var json = System.Text.Json.JsonSerializer.Serialize(coin);
        var roundTrip = System.Text.Json.JsonSerializer.Deserialize<CollectionNodeSpawnDefinition>(json)!;

        Assert.AreEqual(coin.CollectionEntryId, roundTrip.CollectionEntryId);
        Assert.IsFalse(System.Text.Json.JsonSerializer.Serialize(new CollectionNodeSpawnDefinition { Id = 1 })
            .Contains("CollectionEntryId", StringComparison.Ordinal), "Nodes without an entry shouldn't gain the field on save.");
    }

    [TestMethod]
    public void Loader_RejectsAnEntryIdSharedByTwoCollections()
    {
        var path = Path.Combine(Path.GetTempPath(), $"collections-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            "[{\"Id\":1,\"NameId\":1,\"CategoryId\":1,\"Entries\":[{\"Id\":7}]}," +
            "{\"Id\":2,\"NameId\":2,\"CategoryId\":1,\"Entries\":[{\"Id\":7}]}]");

        try
        {
            var collections = new CollectionDefinitionCollection(NullLogger.Instance);
            Assert.IsFalse(collections.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
