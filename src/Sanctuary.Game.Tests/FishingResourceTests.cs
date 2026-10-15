using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Minigames.Fishing;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Fishing's data against the shipped resources (task 33): the six holes' zone files and every item the fish tables,
/// rods and lures name.
/// </summary>
[TestClass]
public sealed class FishingResourceTests
{
    private static readonly int[] HoleIds = [560, 561, 562, 563, 564, 565];

    private const int FishingActivityCategory = 13;

    private static ZoneDefinitionCollection _zones = null!;
    private static ClientItemDefinitionCollection _items = null!;
    private static ClientActivityDefinitionCollection _activities = null!;
    private static PointOfInterestDefinitionCollection _pointsOfInterest = null!;

    [ClassInitialize]
    public static void LoadResources(TestContext _)
    {
        var resources = FindResourcesDirectory();

        _zones = new ZoneDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_zones.Load(Path.Combine(resources, "Zones")));

        _items = new ClientItemDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_items.Load(Path.Combine(resources, "ClientItemDefinitions.json")));

        _activities = new ClientActivityDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_activities.Load(Path.Combine(resources, "ClientActivityDefinitions.json")));

        _pointsOfInterest = new PointOfInterestDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_pointsOfInterest.Load(Path.Combine(resources, "PointOfInterests.json")));
    }

    private static string FindResourcesDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources");

            if (Directory.Exists(Path.Combine(candidate, "Zones")))
                return candidate;
        }

        throw new DirectoryNotFoundException("src/Resources not found above the test output.");
    }

    private static FishingZoneDefinition Hole(int id)
    {
        Assert.IsTrue(_zones.TryGetValue(id, out var zone), $"No zone {id}.");
        return (FishingZoneDefinition)zone;
    }

    [TestMethod]
    public void EachFishingActivity_IsAFishingZone_WithItsOwnScene()
    {
        var activityIds = _activities.Values.Where(activity => activity.Category == FishingActivityCategory).Select(activity => activity.Id);
        CollectionAssert.AreEquivalent(HoleIds, activityIds.ToArray(), "The client's fishing activities are the six holes.");

        CollectionAssert.AreEquivalent(HoleIds, _zones.Values.OfType<FishingZoneDefinition>().Select(zone => zone.Id).ToArray());

        var scenes = HoleIds.Select(id => Hole(id).Name).ToList();

        Assert.IsTrue(scenes.All(scene => Regex.IsMatch(scene, "^(sg|bw|sh)_fishing_(medpond|stream)$")), string.Join(", ", scenes));
        Assert.AreEqual(scenes.Count, scenes.Distinct().Count(), "One scene per hole: two holes in one zone would share one instance.");
    }

    [TestMethod]
    public void SacredGroveShallows_KeepsItsCheckedSpawn()
    {
        var hole = Hole(563);

        Assert.AreEqual("sg_fishing_medpond", hole.Name);
        Assert.AreEqual(435.05676f, hole.SpawnPosition.X);
        Assert.AreEqual(-64.46508f, hole.SpawnPosition.Y);
        Assert.AreEqual(370.70682f, hole.SpawnPosition.Z);
        Assert.AreEqual(68f, hole.UnderwaterBedX);

        // The rotation edenfps sent: Quaternion.Identity.
        Assert.AreEqual(System.Numerics.Quaternion.Identity, hole.SpawnRotation);
    }

    [TestMethod]
    public void EverySpawn_IsInsideItsZonesGrid()
    {
        foreach (var id in HoleIds)
        {
            var hole = Hole(id);

            // Off the grid a player is on no tile, and nobody sees them fish.
            var latitude = (int)Math.Floor(hole.SpawnPosition.X / hole.TileSize);
            var longitude = (int)Math.Floor(hole.SpawnPosition.Z / hole.TileSize);

            Assert.IsTrue(latitude >= hole.StartLatitude && latitude < hole.EndLatitude, $"Hole {id}: X {hole.SpawnPosition.X}.");
            Assert.IsTrue(longitude >= hole.StartLongitude && longitude < hole.EndLongitude, $"Hole {id}: Z {hole.SpawnPosition.Z}.");

            // Every Underwater_Bed edenfps read was between X 55 and 74.
            Assert.IsTrue(hole.UnderwaterBedX is >= 50f and <= 80f, $"Hole {id}: bed X {hole.UnderwaterBedX}.");
        }
    }

    [TestMethod]
    public void EachHole_HasFish_AndAPointOfInterest()
    {
        foreach (var id in HoleIds)
        {
            Assert.IsTrue(FishTables.ByActivity.ContainsKey(id), $"Hole {id} has no fish.");

            // A point of interest offers its hole's start screen when it shares the activity's name.
            var activity = _activities[id];
            Assert.AreEqual(1, _pointsOfInterest.Values.Count(poi => poi.NameId == activity.NameId), $"Hole {id}.");
        }
    }

    [TestMethod]
    public void EveryCatch_IsAnItem_StackedUntinted()
    {
        var catches = FishTables.ByActivity.Values.SelectMany(table => table)
            .Select(fish => (fish.Name, fish.ItemId, fish.IconId))
            .Concat(FishTables.MiscCatches.Select(misc => (misc.Name, misc.ItemId, misc.IconId)));

        foreach (var (name, itemId, iconId) in catches)
        {
            Assert.IsTrue(_items.TryGetValue(itemId, out var item), $"{name}: no item {itemId}.");

            Assert.AreEqual(iconId, item.Icon.Id, $"{name}: the banner's icon is the item's.");

            // A catch is granted with tint 0, so stacks of it must have no other tint.
            Assert.IsFalse(item.IsTintable, name);
            Assert.AreEqual(0, item.Icon.TintId, name);
        }
    }

    [TestMethod]
    public void Lures_AreSingleUseItems_WhoseFishLiveSomewhere()
    {
        var allFish = FishTables.ByActivity.Values.SelectMany(table => table).Select(fish => fish.Name).ToHashSet();

        foreach (var lureId in FishingGear.LureFish.Keys.Append(FishingGear.TreasureMagnetItemId))
        {
            Assert.IsTrue(_items.TryGetValue(lureId, out var lure), $"No lure {lureId}.");
            Assert.IsTrue(lure.SingleUse, $"Lure {lureId}.");
            Assert.AreNotEqual(0, lure.ActivatableAbilityId, $"Lure {lureId} can't be used from the action bar.");
            Assert.IsTrue(FishingGear.IsLure(lureId));
        }

        foreach (var (lureId, fish) in FishingGear.LureFish)
        {
            Assert.AreEqual(3, fish.Length);

            foreach (var name in fish)
                Assert.IsTrue(allFish.Contains(name), $"Lure {lureId}: no hole has {name}.");
        }
    }

    [TestMethod]
    public void Rods_AreTheFishingRods_InTheirSlot()
    {
        for (var id = 68185; id <= 68359; id++)
            AssertIsRod(id);

        for (var id = 76687; id <= 76691; id++)
            AssertIsRod(id);

        static void AssertIsRod(int id)
        {
            Assert.IsTrue(_items.TryGetValue(id, out var rod), $"No item {id}.");
            Assert.AreEqual(FishingGear.RodSlot, rod.Slot, $"Item {id}.");
            Assert.AreEqual(FishingGear.CastRangeFor(id).RodName, rod.Comment, $"Item {id}.");
        }
    }
}
