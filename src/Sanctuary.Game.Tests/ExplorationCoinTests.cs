using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Exploration coins used to be plain npcs spawned by FabledRealms.lua, with nothing behind a click. They are now
/// collection nodes on their own spots; each ticks an entry of its region's exploration collection
/// (<see cref="ExplorationCollectionTests"/>).
/// </summary>
[TestClass]
public sealed class ExplorationCoinTests
{
    private static readonly Dictionary<string, string> Kinds = new()
    {
        ["Rare Exploration Coin"] = "rare",
        ["Elite Exploration Coin"] = "elite"
    };

    // Each region has its own coin model; the node types and pools are named for the region.
    private static readonly Dictionary<string, string> RegionsByModelFile = new()
    {
        ["env_icon_briarwood.adr"] = "briarwood",
        ["env_icon_chugawug.adr"] = "wugachug",
        ["env_icon_merryvale.adr"] = "merry-vale",
        ["env_icon_sanctuary.adr"] = "sanctuary",
        ["env_icon_seaside.adr"] = "seaside",
        ["env_icon_snowhill.adr"] = "snowhill",
        ["env_icon_blackspore.adr"] = "blackspore",
        ["env_icon_shrouded_glade.adr"] = "shrouded-glade",
        ["env_icon_sunstone_valley.adr"] = "sunstone-valley"
    };

    private static (string NodeType, string Pool) KindOf(NpcDefinition coin)
    {
        var kind = Kinds[coin.Name!];
        Assert.IsTrue(RegionsByModelFile.TryGetValue(coin.ModelFileName, out var region),
            $"{coin.Name} {coin.Id} uses an unknown coin model {coin.ModelFileName}.");

        return ($"{region}-{kind}-exploration-coin", $"{region}-exploration-coins-{kind}");
    }

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

    /// <summary>
    /// Coins placed by hand where the 2014 npc list had none, from Ariel's <c>!whereami</c>: Oberyn's Broken Home
    /// (Blackspore Elite, 2026-10-20), Melted Corner (Snowhill Elite, 2026-10-21), Between the Peaks (Snowhill Rare,
    /// 2026-10-24: its old spot was built over, so it stands at the nearest place a player can reach), and Pixiewood
    /// Mist and A Royal Way In (Sanctuary Rare, 2026-10-24), and Bridge View (Wilds Northwest Rare, 2026-10-27).
    /// </summary>
    private static readonly int[] PlacedCoinIds = [4493, 4494, 4495, 4496, 4497, 4498];

    /// <summary>
    /// 2014 coins whose place belongs to the other kind's collection, so they sit in that kind's pool: the elite coin
    /// npc 4435 stands at Perry's Lookout, a Snowhill Rare place (Ariel, 2026-10-21).
    /// </summary>
    private static readonly Dictionary<int, string> MovedPools = new() { [4435] = "snowhill-exploration-coins-rare" };

    private static NpcDefinition[] CoinNpcDefinitions()
    {
        var npcs = new NpcDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(npcs.Load(FindSourcePath("Resources", "Npcs.json")));

        var coins = npcs.Values.Where(npc => npc.Name is not null && Kinds.ContainsKey(npc.Name)).ToArray();
        Assert.IsTrue(coins.Length > 0, "Npcs.json has no exploration coins.");

        return coins;
    }

    private static CollectionNodeTypeDefinitionCollection LoadTypes()
    {
        var types = new CollectionNodeTypeDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(types.Load(FindSourcePath("Resources", "CollectionNodeTypes.json")));
        return types;
    }

    private static CollectionNodePoolDefinitionCollection LoadPools()
    {
        var pools = new CollectionNodePoolDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(pools.Load(FindSourcePath("Resources", "CollectionNodePools.json")));
        return pools;
    }

    private static CollectionNodeSpawnDefinitionCollection LoadSpawns()
    {
        var spawns = new CollectionNodeSpawnDefinitionCollection(NullLogger.Instance);
        // Other tests write scratch spawns under the test output's Resources folder, so anchor on a shipped file.
        var resources = Path.GetDirectoryName(FindSourcePath("Resources", "CollectionNodePools.json"))!;
        Assert.IsTrue(spawns.Load(Path.Combine(resources, "CollectionNodeSpawns")));
        return spawns;
    }

    [TestMethod]
    public void FabledRealmsScript_NoLongerSpawnsCoinsAsPlainNpcs()
    {
        var script = File.ReadAllText(FindSourcePath("Scripts", "Zone", "FabledRealms.lua"));
        var spawnedNpcIds = Regex.Matches(script, @"spawnNpc(?:WithGuid)?\(\s*(\d+)")
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToHashSet();

        var stillPlain = CoinNpcDefinitions().Where(coin => spawnedNpcIds.Contains(coin.Id)).Select(coin => coin.Id);

        Assert.AreEqual(string.Empty, string.Join(", ", stillPlain),
            "These coins would spawn as plain npcs that ignore a click.");
    }

    [TestMethod]
    public void EveryCoin_IsACollectionNodeSpawnInItsPool()
    {
        var spawns = LoadSpawns();

        foreach (var coin in CoinNpcDefinitions())
        {
            Assert.IsTrue(spawns.TryGetValue(coin.Id, out var spawn), $"{coin.Name} {coin.Id} has no spawn.");
            Assert.AreEqual(MovedPools.GetValueOrDefault(coin.Id, KindOf(coin).Pool), spawn.Pool, $"{coin.Name} {coin.Id} is in the wrong pool.");
            Assert.AreEqual(1, spawn.ZoneDefinitionId);
        }

        foreach (var id in PlacedCoinIds)
            Assert.IsTrue(spawns.TryGetValue(id, out var placed) && placed.Pool.Contains("-exploration-coins-", StringComparison.Ordinal),
                $"Placed coin {id} is missing from the coin pools.");

        Assert.AreEqual(CoinNpcDefinitions().Length + PlacedCoinIds.Length,
            spawns.Values.Count(spawn => spawn.Pool.Contains("-exploration-coins-", StringComparison.Ordinal)),
            "A coin pool holds a spawn that isn't an exploration coin.");
    }

    [TestMethod]
    public void CoinTypes_PayNothingAndLookLikeTheOriginalNpcs()
    {
        var types = LoadTypes();
        var pools = LoadPools();
        var models = new ModelDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(models.Load(FindSourcePath("Resources", "Models.txt")));

        // Nate's decision (2026-10-12): a coin fills its collection and pays nothing. The placeholder payout is gone.
        var typesJson = File.ReadAllText(FindSourcePath("Resources", "CollectionNodeTypes.json"));
        Assert.IsFalse(typesJson.Contains("\"Coins\"", StringComparison.Ordinal), "A node type still has a coin payout.");

        foreach (var coin in CoinNpcDefinitions())
        {
            var (nodeType, poolKey) = KindOf(coin);

            Assert.IsTrue(types.TryGetValue(nodeType, out var type), $"No node type {nodeType}.");
            Assert.IsFalse(type.HasDrop, $"{nodeType} drops an item; a coin ticks a collection entry instead.");
            Assert.AreEqual(coin.NameId, type.NameId, $"{nodeType} doesn't use {coin.Name}'s name.");
            Assert.AreEqual(coin.ModelId, type.ModelId, $"{nodeType} doesn't use {coin.Name}'s model.");
            Assert.IsTrue(models.ContainsKey(type.ModelId));

            Assert.IsTrue(pools.TryGetValue(poolKey, out var pool), $"No pool {poolKey}.");
            Assert.AreEqual(nodeType, pool.NodeType);
            Assert.AreEqual(1, pool.ZoneDefinitionId);
        }
    }

    [TestMethod]
    public void CoinPools_KeepEveryCoinUpAndRespawnItOnItsOwnSpot()
    {
        var pools = LoadPools();
        var spawns = LoadSpawns();

        foreach (var poolKey in CoinNpcDefinitions().Select(coin => KindOf(coin).Pool).Distinct())
        {
            var pool = pools[poolKey];
            var hardPoints = spawns.Values.Where(spawn => spawn.Pool == poolKey).ToArray();

            var initial = pool.SelectSpawnsToActivate(hardPoints, new HashSet<int>(), int.MaxValue);
            Assert.AreEqual(hardPoints.Length, initial.Count, $"{poolKey} doesn't activate every coin.");

            var collected = hardPoints[hardPoints.Length / 2];
            var stillActive = hardPoints.Where(spawn => spawn.Id != collected.Id).Select(spawn => spawn.Id).ToHashSet();
            var refill = pool.SelectSpawnsToActivate(hardPoints, stillActive, 1, collected.Id);

            Assert.AreEqual(1, refill.Count, $"{poolKey} doesn't bring a collected coin back.");
            Assert.AreEqual(collected.Id, refill[0].Id, $"{poolKey} moves a collected coin to another spot.");
        }
    }
}
