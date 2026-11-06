using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Game.Jobs;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The world objects that open games (<see cref="GameStations"/>), against the shipped data: each is an object the world
/// spawns, under the name its row gives, and opens games the client knows, each under its own name and all under one
/// icon. Which rows open a game today is pinned, so wiring a game changes this test on purpose.
/// </summary>
[TestClass]
public sealed class GameStationsTests
{
    private static ClientActivityDefinitionCollection _activities = null!;
    private static MimicDefinitions _mimic = null!;
    private static ZoneDefinitionCollection _zones = null!;
    private static DailyWheel _dailyWheel = null!;
    private static NpcDefinitionCollection _npcs = null!;
    private static string _spawnScript = null!;

    [ClassInitialize]
    public static void LoadResources(TestContext _)
    {
        var resources = Path.GetDirectoryName(FindSourceFile("Resources", "ClientActivityDefinitions.json"))!;

        _activities = new ClientActivityDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_activities.Load(Path.Combine(resources, "ClientActivityDefinitions.json")));

        Assert.IsTrue(MimicDefinitions.TryLoad(Path.Combine(resources, "Mimic.json"), NullLogger.Instance, out _mimic));

        _zones = new ZoneDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_zones.Load(Path.Combine(resources, "Zones")));

        Assert.IsTrue(DailyWheel.TryLoad(Path.Combine(resources, "DailyWheel.json"), NullLogger.Instance, out _dailyWheel));

        _npcs = new NpcDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_npcs.Load(Path.Combine(resources, "Npcs.json")));

        _spawnScript = File.ReadAllText(FindSourceFile("Scripts", "Zone", "FabledRealms.lua"));
    }

    private static string FindSourceFile(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);

            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"src/{string.Join('/', relativePath)} not found above the test output.");
    }

    private static bool IsPlayable(int activityId) => PlayableActivities.IsPlayable(activityId, _mimic, _zones, _dailyWheel);

    private static int SpawnCount(int npcId) => Regex.Matches(_spawnScript, $@"spawnFixedNpc\({npcId}, ").Count;

    /// <summary>
    /// Every station is an object FabledRealms.lua spawns once, so under one GUID (upstream OSFR #164 derives it from the
    /// id), with the name its row gives it; and every spawned object of that name is on the row.
    /// </summary>
    [TestMethod]
    public void EveryStation_IsTheWorldsObjectsOfItsName()
    {
        foreach (var station in GameStations.All)
        {
            Assert.IsTrue(station.NpcIds.Count > 0, $"{station.Name} has no objects.");

            foreach (var npcId in station.NpcIds)
            {
                Assert.AreEqual(1, SpawnCount(npcId), $"{station.Name} {npcId} isn't spawned once by FabledRealms.lua.");
                Assert.IsTrue(_npcs.TryGetValue(npcId, out var npc), $"{station.Name} {npcId} isn't in Npcs.json.");
                Assert.AreEqual(station.Name, npc.Name, $"npc {npcId} isn't a {station.Name}.");
            }

            var spawnedOfThatName = _npcs.Values
                .Where(npc => npc.Name == station.Name && SpawnCount(npc.Id) > 0)
                .Select(npc => npc.Id);

            CollectionAssert.AreEquivalent(spawnedOfThatName.ToArray(), station.NpcIds.ToArray(),
                $"Every spawned {station.Name} is on its row.");
        }
    }

    [TestMethod]
    public void NoObject_IsOnTwoRows_OrATrainer()
    {
        var npcIds = GameStations.All.SelectMany(station => station.NpcIds).ToList();

        CollectionAssert.AllItemsAreUnique(npcIds);
        CollectionAssert.AllItemsAreUnique(GameStations.All.Select(station => station.Name).ToList());

        // A trainer's click is the Gateway's (CommandPacketInteractRequestHandler), which shows the trainer's menu.
        Assert.IsFalse(npcIds.Any(JobTrainers.IsTrainer), "A station is a job trainer.");

        foreach (var npcId in npcIds)
            Assert.IsTrue(GameStations.TryGet(npcId, out var station) && station.NpcIds.Contains(npcId));

        Assert.IsFalse(GameStations.TryGet(2069, out _), "Simone (a trainer) isn't a station.");
    }

    /// <summary>
    /// Each button reads its game's name in the Games menu, under the game's icon: every game is one the client knows, no
    /// two of a row's games share a name (two buttons would read alike), and all of them share one icon, the kind of the
    /// object.
    /// </summary>
    [TestMethod]
    public void EveryGame_IsOneTheClientKnows_UnderItsOwnName_AndTheRowsIcon()
    {
        foreach (var station in GameStations.All)
        {
            Assert.IsTrue(station.ActivityIds.Count > 0, $"{station.Name} opens no game.");
            CollectionAssert.AllItemsAreUnique(station.ActivityIds.ToList(), $"{station.Name} lists a game twice.");

            foreach (var activityId in station.ActivityIds)
                Assert.IsTrue(_activities.ContainsKey(activityId), $"{station.Name}'s activity {activityId} isn't in ClientActivityDefinitions.json.");

            var games = station.ActivityIds.Select(activityId => _activities[activityId]).ToList();

            Assert.IsFalse(games.Any(game => game.NameId == 0), $"One of {station.Name}'s games has no name.");
            CollectionAssert.AllItemsAreUnique(games.Select(game => game.NameId).ToList(), $"Two of {station.Name}'s games share a name.");
            Assert.AreEqual(1, games.Select(game => game.ImageSetId).Distinct().Count(), $"{station.Name}'s games have different icons.");
        }
    }

    [TestMethod]
    public void EveryRecipeTheServerCooks_IsOnTheTables()
    {
        var tables = GameStations.All.Single(station => station.Name == "Cooking Table");
        var cooking = _mimic.Find("cooking");

        Assert.IsNotNull(cooking);
        Assert.IsTrue(cooking.Recipes.Count > 0);

        foreach (var recipe in cooking.Recipes)
            CollectionAssert.Contains(tables.ActivityIds.ToList(), recipe.ActivityId, $"Recipe {recipe.ActivityId} isn't cooked at a table.");
    }

    /// <summary>
    /// What each row opens today, in its buttons' order. Wiring a game (a FlashMinigames.Definitions row, a Mimic.json
    /// recipe) lights up its row with no change to the table, and changes this test.
    /// </summary>
    [TestMethod]
    public void WhatEachStationOpensToday_IsPinned()
    {
        var opens = GameStations.All.ToDictionary(station => station.Name, station => station.ActivityIds.Where(IsPlayable).ToArray());

        CollectionAssert.AreEqual(new[] { 450, 451, 456, 460, 471 }, opens["Cooking Table"]);
        CollectionAssert.AreEqual(new[] { 414 }, opens["mailbox"]);
        CollectionAssert.AreEqual(new[] { 414 }, opens["Sanctuary Postboxes"]);
        CollectionAssert.AreEqual(new[] { 410 }, opens["Queensfields Harvesting"]);
        CollectionAssert.AreEqual(new[] { 420 }, opens["Anvil"]);
        CollectionAssert.AreEqual(new[] { 420 }, opens["Spectrum Anvil"]);
        CollectionAssert.AreEqual(new[] { 565 }, opens["Frostbitten Banks"]);

        // Copper veins wait for Copper Mining (400), not Mining Practice (1113), which pays nothing.
        var nothingYet = opens.Where(entry => entry.Value.Length == 0).Select(entry => entry.Key).ToArray();

        CollectionAssert.AreEquivalent(new[]
        {
            "Lakesfield Harvesting", "Perry's Pastures Harvesting", "Seaside Postboxes", "Merry Vale Postboxes",
            "Shrouded Glade Postboxes", "Snowhill Postboxes", "Copper Vein"
        }, nothingYet);
    }

    // Evergrove's own cooking tables, above every id upstream OSFR's Npcs.json uses (its highest is in the 40000s).
    private static readonly int[] EvergrovesTables = [90001, 90002, 90003, 90004, 90005, 90006];

    // The tables' and their map points' name.
    private const int CookingTableName = 2061;

    private static Vector3 SpawnedAt(int npcId)
    {
        var spawn = Regex.Match(_spawnScript, $@"spawnFixedNpc\({npcId}, ([-\d.e]+), ([-\d.e]+), ([-\d.e]+),");
        Assert.IsTrue(spawn.Success, $"{npcId} isn't spawned.");

        float At(int group) => float.Parse(spawn.Groups[group].Value, CultureInfo.InvariantCulture);

        return new Vector3(At(1), At(2), At(3));
    }

    /// <summary>
    /// Evergrove's own tables are copies of Sanctuary's (2417), each defined and spawned once under an id no upstream NPC
    /// uses: Npcs.json's loader keeps the first of two rows with one id, and a second spawn of an id is refused, its GUID
    /// being taken. Each GUID is a fixed one, below the counters zones hand out to the NPCs they make.
    /// </summary>
    [TestMethod]
    public void EvergrovesOwnTables_CollideWithNothing()
    {
        using var npcsFile = JsonDocument.Parse(File.ReadAllText(FindSourceFile("Resources", "Npcs.json")));
        var definedIds = npcsFile.RootElement.EnumerateArray().Select(npc => npc.GetProperty("Id").GetInt32()).ToList();
        var sanctuarysTable = _npcs[2417];

        Assert.IsTrue(definedIds.Except(EvergrovesTables).All(id => id < EvergrovesTables.Min()), "An upstream NPC has an id in Evergrove's range.");

        foreach (var npcId in EvergrovesTables)
        {
            Assert.AreEqual(1, definedIds.Count(id => id == npcId), $"Npcs.json doesn't define {npcId} once.");
            Assert.AreEqual(1, SpawnCount(npcId), $"{npcId} isn't spawned once.");

            var guid = GuidHelper.GetNpcGuid((ulong)npcId, GuidHelper.OpenWorldInstanceId);
            Assert.IsTrue(GuidHelper.IsFixedNpcGuid(guid), $"{npcId}'s GUID isn't a fixed one.");
            Assert.AreEqual((ulong)npcId, GuidHelper.GetNpcCounter(guid));

            var table = _npcs[npcId];
            Assert.AreEqual((sanctuarysTable.NameId, sanctuarysTable.Name, sanctuarysTable.ModelId, sanctuarysTable.ModelFileName),
                (table.NameId, table.Name, table.ModelId, table.ModelFileName), $"{npcId} isn't a cooking table.");
        }

        CollectionAssert.IsSubsetOf(EvergrovesTables, GameStations.All.Single(station => station.Name == "Cooking Table").NpcIds.ToArray());
    }

    /// <summary>
    /// Every Cooking Table map icon teleports the player within the walk-in range of a table that opens the recipes, so a
    /// click on it opens them. Six icons had only scenery there before Evergrove's own tables.
    /// </summary>
    [TestMethod]
    public void EveryCookingTableMapIcon_LandsThePlayerBesideATable()
    {
        var tables = GameStations.All.Single(station => station.Name == "Cooking Table").NpcIds.Select(SpawnedAt).ToList();

        using var pointsFile = JsonDocument.Parse(File.ReadAllText(FindSourceFile("Resources", "PointOfInterests.json")));
        var icons = pointsFile.RootElement.EnumerateArray()
            .Where(point => point.TryGetProperty("NameId", out var name) && name.GetInt32() == CookingTableName)
            .ToList();

        Assert.AreEqual(12, icons.Count);

        foreach (var icon in icons)
        {
            var landing = icon.GetProperty("SpawnPosition").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            var nearest = tables.Min(table => Vector3.Distance(table, new Vector3(landing[0], landing[1], landing[2])));

            Assert.IsTrue(nearest <= GameStations.WalkInRange, $"Map point {icon.GetProperty("Id")} lands {nearest:F1} from the nearest table.");
        }
    }
}
