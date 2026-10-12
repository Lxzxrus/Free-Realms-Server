using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The shipped housing zone files against Houses.json and the Marketplace (task 26, open-lots).
/// </summary>
[TestClass]
public sealed class HousingLotZoneTests
{
    private const int HouseItemType = 16;
    private const int MainStoreId = 1;

    private static string _resources = null!;
    private static ZoneDefinitionCollection _zones = null!;
    private static HouseDefinitionCollection _houses = null!;
    private static StoreDefinitionCollection _stores = null!;
    private static ClientItemDefinitionCollection _items = null!;

    [ClassInitialize]
    public static void LoadResources(TestContext _)
    {
        _resources = FindResourcesDirectory();

        _zones = new ZoneDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_zones.Load(Path.Combine(_resources, "Zones")), "The zone files failed to load.");

        _houses = new HouseDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_houses.Load(Path.Combine(_resources, "Houses.json")), "Houses.json failed to load.");

        _stores = new StoreDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_stores.Load(Path.Combine(_resources, "Stores.json")), "Stores.json failed to load.");
        Assert.IsTrue(_stores.LoadBundles(Path.Combine(_resources, "StoreBundles.json")), "StoreBundles.json failed to load.");

        _items = new ClientItemDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_items.Load(Path.Combine(_resources, "ClientItemDefinitions.json")), "ClientItemDefinitions.json failed to load.");
    }

    private static string FindResourcesDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources");

            if (File.Exists(Path.Combine(candidate, "Houses.json")))
                return candidate;
        }

        throw new DirectoryNotFoundException("src/Resources not found above the test output.");
    }

    private static List<HousingZoneDefinition> HousingZones() =>
        _zones.Values.OfType<HousingZoneDefinition>().OrderBy(zone => zone.Id).ToList();

    [TestMethod]
    public void EveryZoneFileLoads_AndNoTwoShareAnId()
    {
        var files = Directory.GetFiles(Path.Combine(_resources, "Zones"), "*.json");
        var ids = files
            .Select(file => (File: Path.GetFileName(file), Id: (int)JsonNode.Parse(File.ReadAllText(file))!["Id"]!))
            .ToList();

        foreach (var group in ids.GroupBy(entry => entry.Id).Where(group => group.Count() > 1))
            Assert.Fail($"Zone id {group.Key} is used by {string.Join(", ", group.Select(entry => entry.File))}.");

        // The loader skips a file it can't parse or whose id is taken, so every file must have become a definition.
        Assert.AreEqual(files.Length, _zones.Count, "Not every zone file became a zone definition.");
    }

    [TestMethod]
    public void TheLotsOpenBeforeKeepTheirZoneIds()
    {
        // Players' Houses rows point at these ids.
        Assert.IsTrue(_zones.TryGetValue(14, out var seaside) && seaside is HousingZoneDefinition);
        Assert.AreEqual("hsg_emptylot_seaside_cliffs_01", seaside.Name);
        Assert.IsTrue(_zones.TryGetValue(22, out var blackspore) && blackspore is HousingZoneDefinition);
        Assert.AreEqual("hsg_hum_blackspore_01", blackspore.Name);
    }

    [TestMethod]
    public void EighteenLotsAreOpen()
    {
        CollectionAssert.AreEquivalent(
            new[] { 1, 2, 3, 4, 5, 6, 8, 9, 10, 14, 15, 16, 17, 19, 20, 21, 22, 24 },
            HousingZones().Select(zone => HouseFor(zone).Id).ToArray());
    }

    [TestMethod]
    public void NoHouseEntryPointsAtAWorldZone()
    {
        foreach (var house in _houses.Values)
        {
            if (_zones.TryGetValue(house.ZoneId, out var zone))
                Assert.IsInstanceOfType<HousingZoneDefinition>(zone, $"House {house.Id} points at zone {house.ZoneId}, \"{zone.Name}\".");
        }
    }

    [TestMethod]
    public void EveryHousingZoneAgreesWithItsHouseEntry()
    {
        foreach (var zone in HousingZones())
        {
            var house = HouseFor(zone);
            var label = $"Zone {zone.Id} ({zone.Name}) and house {house.Id}";

            Assert.AreEqual(house.NameId, zone.NameId, $"{label}: name id.");
            Assert.AreEqual(house.Icon, zone.IconId, $"{label}: icon.");
            Assert.AreEqual(Xyz(house.SpawnPosition), Xyz(zone.SpawnPosition), $"{label}: spawn position.");
            // Houses.json writes the heading as [x, z, 0, 0]; a zone file as [x, z].
            Assert.AreEqual(new Vector2(house.SpawnRotation.X, house.SpawnRotation.Y),
                new Vector2(zone.SpawnRotation.X, zone.SpawnRotation.Z), $"{label}: spawn rotation.");
            Assert.AreEqual(house.BuildAreas.Count, zone.BuildAreas.Count, $"{label}: build area count.");

            for (var index = 0; index < house.BuildAreas.Count; index++)
            {
                Assert.AreEqual(Xyz(house.BuildAreas[index].Min), Xyz(zone.BuildAreas[index].Min), $"{label}: build area {index} min.");
                Assert.AreEqual(Xyz(house.BuildAreas[index].Max), Xyz(zone.BuildAreas[index].Max), $"{label}: build area {index} max.");
            }
        }
    }

    [TestMethod]
    public void EveryHousingZoneHasItsOwnNameAndCommand()
    {
        var zones = HousingZones();

        Assert.AreEqual(zones.Count, zones.Select(zone => zone.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "Two zones share a client zone name.");
        Assert.AreEqual(zones.Count, zones.Select(zone => zone.CommandName).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "Two zones share a /house command.");
        Assert.IsTrue(zones.All(zone => !zone.CommandName.Contains(' ')), "A /house command name has a space.");
        Assert.AreEqual(zones.Count, zones.Select(zone => zone.StoreBundleId).Distinct().Count(), "Two zones share a store bundle.");
    }

    [TestMethod]
    public void EveryHousingZoneBundleSellsItsDeed()
    {
        Assert.IsTrue(_stores.TryGetValue(MainStoreId, out var store));

        foreach (var zone in HousingZones())
        {
            var label = $"Zone {zone.Id} ({zone.DisplayName})";

            Assert.IsTrue(_items.TryGetValue(zone.ItemDefinitionId, out var deed), $"{label}: deed {zone.ItemDefinitionId} doesn't exist.");
            Assert.AreEqual(HouseItemType, deed.Type, $"{label}: item {zone.ItemDefinitionId} isn't a deed.");
            Assert.AreEqual(deed.NameId, zone.NameId, $"{label}: the deed's name differs from the zone's.");

            Assert.IsTrue(store.Bundles.TryGetValue(zone.StoreBundleId, out var bundle), $"{label}: bundle {zone.StoreBundleId} isn't in the Marketplace.");

            // What PacketInGamePurchasePlaceOrderPacketHandler accepts for a house: one entry, the zone's deed.
            Assert.AreEqual(1, bundle.Entries.Count, $"{label}: bundle {bundle.Id} sells more than the deed.");
            Assert.AreEqual(zone.ItemDefinitionId, bundle.Entries[0].MarketingItemId, $"{label}: bundle {bundle.Id} sells another item.");
            // BaseZone leaves a bundle off the shelf if the server doesn't know one of its items.
            Assert.IsTrue(bundle.Entries.All(entry => _items.ContainsKey(entry.MarketingItemId)), $"{label}: bundle {bundle.Id} has an unknown item.");
            // The bundle's name is what the shelf shows: it must name what it sells.
            Assert.AreEqual(deed.NameId, bundle.NameId, $"{label}: bundle {bundle.Id} is named for something else.");
        }
    }

    [TestMethod]
    public void EveryArrivalIsInsideItsFirstBuildArea()
    {
        foreach (var zone in HousingZones())
        {
            var arrival = zone.GetArrivalPosition();

            Assert.IsTrue(zone.BuildAreas.Count > 0, $"Zone {zone.Id} ({zone.Name}) has no build area.");
            Assert.IsTrue(IsInside(zone.BuildAreas[0], arrival),
                $"Zone {zone.Id} ({zone.Name}) arrives at {arrival}, outside its first build area.");
            Assert.AreEqual(zone.SpawnPosition.Y, arrival.Y, $"Zone {zone.Id} ({zone.Name}): the arrival height changed.");
        }
    }

    [TestMethod]
    public void ArrivalLeavesASpawnInsideTheBuildAreaAlone()
    {
        Assert.IsTrue(_zones.TryGetValue(14, out var seaside));
        var zone = (HousingZoneDefinition)seaside;

        Assert.AreEqual(zone.SpawnPosition, zone.GetArrivalPosition());
    }

    [TestMethod]
    public void ArrivalPullsAnOutsideSpawnInFromTheEdge()
    {
        var zone = new HousingZoneDefinition
        {
            Name = "hsg_emptylot_blackspore_01",
            DisplayName = "Blackspore Swamp Lot",
            CommandName = "swamplot",
            DirectorySnapshot = "blackspore_lot",
            SpawnPosition = new Vector4(444.3f, 5.2f, 429.3f, 1f),
            BuildAreas =
            [
                new BoundingBox { Min = new Vector4(322f, -100f, 392.1f, 1f), Max = new Vector4(443.3f, 60f, 477.8f, 1f) }
            ]
        };

        var arrival = zone.GetArrivalPosition();

        Assert.AreEqual(443.3f - HousingZoneDefinition.ArrivalMargin, arrival.X, 0.001f);
        Assert.AreEqual(5.2f, arrival.Y, 0.001f);
        Assert.AreEqual(429.3f, arrival.Z, 0.001f);
        Assert.IsTrue(zone.IsInsideBuildArea(arrival));
    }

    private static HouseDefinition HouseFor(HousingZoneDefinition zone)
    {
        var houses = _houses.Values.Where(house => house.ZoneId == zone.Id).ToList();

        Assert.AreEqual(1, houses.Count, $"Zone {zone.Id} ({zone.Name}) has {houses.Count} Houses.json entries.");

        return houses[0];
    }

    private static Vector3 Xyz(Vector4 value) => new(value.X, value.Y, value.Z);

    private static bool IsInside(BoundingBox area, Vector4 position) =>
        position.X >= MathF.Min(area.Min.X, area.Max.X) && position.X <= MathF.Max(area.Min.X, area.Max.X) &&
        position.Y >= MathF.Min(area.Min.Y, area.Max.Y) && position.Y <= MathF.Max(area.Min.Y, area.Max.Y) &&
        position.Z >= MathF.Min(area.Min.Z, area.Max.Z) && position.Z <= MathF.Max(area.Min.Z, area.Max.Z);
}
