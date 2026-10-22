using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Packet.Common;

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
        [1947, 1954, 1955, 1985, 2170, 2171, 2172, 2173, 2174, 2175, 2176, 2177, 90008, 90018];

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
    public void EveryClothingReward_HasAVersionForBoysAndGirls()
    {
        // The capture's rewards are the boys' items; a girl gets the twin with the same name, colour and trade rules
        // (Ariel, 2026-10-13: the boys-only Traveler's Boots and Seeker's Hat couldn't be worn).
        var items = new ClientItemDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(items.Load(FindSourcePath("Resources", "ClientItemDefinitions.json")));

        foreach (var collection in LoadCollections().Values)
        {
            foreach (var gender in new[] { 1, 2 })
            {
                foreach (var itemId in collection.RewardItemIdsFor(gender))
                {
                    Assert.IsTrue(items.TryGetValue(itemId, out var item), $"Collection {collection.Id} rewards unknown item {itemId}.");

                    var fitted = items.ForGender(item, gender);

                    Assert.IsTrue(fitted.GenderUsage is 0 || fitted.GenderUsage == gender,
                        $"Collection {collection.Id}'s reward {itemId} has no version for gender {gender}.");
                }
            }
        }

        Assert.AreEqual(15783, items.ForGender(items[17300], 2).Id, "Traveler's Boots, girls' version in the same colour.");
        Assert.AreEqual(17300, items.ForGender(items[17300], 1).Id);
    }

    [TestMethod]
    public void BookPreview_ShowsTheRewardForThePlayersGender()
    {
        // Ariel's book still showed the boys' Traveler's Boots after the grant was fixed (2026-10-13).
        var items = new ClientItemDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(items.Load(FindSourcePath("Resources", "ClientItemDefinitions.json")));
        var seaside = LoadCollections()[267];

        ClientItemDefinition? Fitted(int id, int gender) => items.TryGetValue(id, out var item) ? items.ForGender(item, gender) : null;

        var girls = seaside.CreateClientCollection(1, Sanctuary.Game.Collections.CollectionProgress.Empty, id => Fitted(id, 2), 2);
        var boys = seaside.CreateClientCollection(1, Sanctuary.Game.Collections.CollectionProgress.Empty, id => Fitted(id, 1), 1);

        Assert.AreEqual(items[15783].NameId, girls.Reward.NameId);
        Assert.AreEqual(items[15783].Icon.Id, girls.Reward.IconId, "The girls' Traveler's Boots.");
        Assert.AreEqual(items[17300].Icon.Id, boys.Reward.IconId, "The boys' Traveler's Boots.");

        // The tooltip reads the entry's item id, not its picture.
        Assert.AreEqual(15783, girls.Reward.Entries.OfType<RewardBundleEntryItem>().Single().DefinitionId);
        Assert.AreEqual(17300, boys.Reward.Entries.OfType<RewardBundleEntryItem>().Single().DefinitionId);
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

        Assert.AreEqual(120, seen.Count, "The number of coins with an entry changed.");
    }

    [TestMethod]
    [DataRow(2112, 2041, "Hollow Hiding Hole")]
    [DataRow(2115, 2039, "Between Ninjas And Graveyards")]
    [DataRow(2116, 2040, "Top Tier Mining Excavation")]
    [DataRow(2118, 2043, "A Relic View")]
    [DataRow(2119, 2042, "Upon Shattered History")]
    [DataRow(2120, 2044, "Branching Out")]
    [DataRow(2121, 2045, "Splortata's Timeout Spot")]
    [DataRow(4493, 2038, "Oberyn's Broken Home")]
    [DataRow(4436, 1950, "Smitty's Hiding Place")]
    [DataRow(4439, 1935, "Beyond the Springs")]
    [DataRow(4435, 1948, "Perry's Lookout")]
    [DataRow(4443, 1949, "Penguin Point")]
    [DataRow(4434, 2018, "Perry's Hideaway")]
    [DataRow(4437, 2017, "Miner Difficulties")]
    [DataRow(4438, 2016, "Hot Hangout")]
    [DataRow(4440, 2019, "Between Rocks And Trees")]
    [DataRow(4441, 2014, "Penguin Path")]
    [DataRow(4442, 2013, "Beyond the Nameless Peak")]
    [DataRow(4444, 2015, "Leafy Retreat")]
    [DataRow(4494, 2012, "Melted Corner")]
    [DataRow(4421, 1966, "A Place to Pond-er")]
    [DataRow(4422, 1939, "Gather Round the Campfire")]
    [DataRow(4423, 1965, "Scouting the Pathway")]
    [DataRow(4424, 1967, "Spore Falls")]
    [DataRow(4425, 1964, "Xandril's Nook")]
    public void CoinsCheckedInGame_TickTheEntryNamedForTheirPlace(int coinId, int entryId, string place)
    {
        // Ariel collected every Blackspore Elite and Briarwood Rare coin on 2026-10-20 (Snowhill on 2026-10-21) and named each coin's place from
        // the original game's guides. The coins had been paired with entries by id order, which put five of them one
        // place off and left Oberyn's Broken Home without a coin.
        Assert.IsTrue(LoadSpawns().TryGetValue(coinId, out var spawn), $"No coin {coinId} ({place}).");
        Assert.AreEqual(entryId, spawn.CollectionEntryId, $"Coin {coinId} is at {place}.");
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
    public void NewPlayer_IsSentEveryCollection_IncludingOneWithEightEntries()
    {
        // The collections window reads eight entry columns per collection, and the client makes only as many
        // columns as the longest collection it was sent. A player sent only five-entry collections got a Lua error
        // and a window that couldn't be closed (CollectionsBrowser.lua:134, 2026-10-12).
        var collections = LoadCollections();

        var sent = collections.CreateClientCollections(1, Sanctuary.Game.Collections.CollectionProgress.Empty, _ => null);

        Assert.AreEqual(collections.Count, sent.Count);
        Assert.IsTrue(sent.Max(collection => collection.Entries.Count) >= 8);
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
