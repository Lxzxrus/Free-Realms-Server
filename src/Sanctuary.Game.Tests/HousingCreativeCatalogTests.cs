using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.GameCommerce;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HousingCreativeCatalogTests
{
    private const int Chair = 100;
    private const int YellowBlock = 101;
    private const int BlueBlock = 102;
    private const int Coffin = 103;
    private const int Wallpaper = 104;
    private const int ChairWithoutModel = 200;
    private const int House = 300;
    private const int Shirt = 400;

    private ResourceManager _resourceManager = null!;
    private HousingCreativeCatalog _catalog = null!;

    [TestInitialize]
    public void Setup()
    {
        Directory.CreateDirectory(ResourceManager.BaseDirectory);
        _resourceManager = new ResourceManager(NullLogger<ResourceManager>.Instance);
        StopWatcher(_resourceManager);

        AddModel(5000, "hsg_chair_test_01.adr");
        AddModel(5001, "hsg_block_01.adr");

        AddItem(Chair, type: 1, category: 53, model: "hsg_chair_test_01.adr");
        // Colour variants the game ships as separate definitions, told apart by the icon tint.
        AddItem(YellowBlock, type: 1, category: 147, model: "hsg_block_01.adr", iconTint: 237);
        AddItem(BlueBlock, type: 1, category: 147, model: "hsg_block_01.adr", iconTint: 249);
        AddItem(Coffin, type: 29, category: 57, model: "hsg_vampire_coffin_02.adr", param1: 77);
        AddItem(Wallpaper, type: 17, category: 51, model: string.Empty, param1: 2);
        AddItem(ChairWithoutModel, type: 1, category: 53, model: "hsg_missing_01.adr");
        AddItem(House, type: 16, category: 0, model: "hsg_house_01.adr");
        AddItem(Shirt, type: 1, category: 1, model: "shirt_01.adr");

        _catalog = new HousingCreativeCatalog(_resourceManager);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _catalog.Dispose();
    }

    [TestMethod]
    public void Catalog_OffersEveryPlaceableFixtureAndNothingElse()
    {
        CollectionAssert.AreEqual(
            new[] { Chair, YellowBlock, BlueBlock, Coffin, Wallpaper },
            _catalog.Entries.Select(entry => entry.ItemDefinitionId).ToArray());
    }

    [TestMethod]
    public void Catalog_OffersEachColourVariantInItsOwnTint()
    {
        Assert.IsTrue(_catalog.TryGetByDefinitionId(YellowBlock, out var yellow));
        Assert.IsTrue(_catalog.TryGetByDefinitionId(BlueBlock, out var blue));

        Assert.AreEqual(237, yellow.TintId);
        Assert.AreEqual(249, blue.TintId);
        Assert.AreEqual(5001, yellow.Definition.ModelId);
    }

    [TestMethod]
    public void RecordIds_RoundTripAndStayClearOfInventoryIds()
    {
        Assert.IsTrue(_catalog.TryGetByDefinitionId(Chair, out var chair));

        Assert.AreEqual(HousingCreativeCatalog.RecordIdBase + Chair, chair.RecordId);
        Assert.IsTrue(_catalog.TryGetByRecordId(chair.RecordId, out var found));
        Assert.AreEqual(Chair, found.ItemDefinitionId);

        // An inventory record id, even one equal to a catalog definition id, is never a catalog entry.
        Assert.IsFalse(HousingCreativeCatalog.IsCreativeRecordId(Chair));
        Assert.IsFalse(_catalog.TryGetByRecordId(Chair, out _));
        Assert.IsFalse(_catalog.TryGetByRecordId(HousingCreativeCatalog.RecordIdBase + Shirt, out _));
    }

    [TestMethod]
    public void RecordOrDefinitionId_AcceptsBoth()
    {
        Assert.IsTrue(_catalog.TryGetByRecordOrDefinitionId(Wallpaper, out var byDefinition));
        Assert.IsTrue(_catalog.TryGetByRecordOrDefinitionId(HousingCreativeCatalog.RecordIdBase + Wallpaper, out var byRecord));

        Assert.AreEqual(Wallpaper, byDefinition.ItemDefinitionId);
        Assert.AreEqual(Wallpaper, byRecord.ItemDefinitionId);
        Assert.IsFalse(_catalog.TryGetByRecordOrDefinitionId(House, out _));
    }

    [TestMethod]
    public void Catalog_FollowsAReloadOfTheItemDefinitions()
    {
        Assert.AreEqual(5, _catalog.Entries.Count);

        AddItem(105, type: 29, category: 54, model: "hsg_clock_01.adr", param1: 78);

        // The collections post their change events, so the catalog notices shortly after, not at once.
        Assert.IsTrue(SpinWait.SpinUntil(() => _catalog.Entries.Count == 6, TimeSpan.FromSeconds(5)));
        Assert.IsTrue(_catalog.TryGetByDefinitionId(105, out _));
    }

    [TestMethod]
    public void Options_DefaultToCreativeBuildingAndFreeLots()
    {
        var options = new HousingOptions();

        Assert.IsTrue(options.CreativeMode);
        Assert.IsTrue(options.FreeLots);
        Assert.AreEqual(0, options.CreativeCatalogLimit);
    }

    [TestMethod]
    public void FreeBundle_CostsNothingAndLeavesTheOriginalAlone()
    {
        var lot = new AppStoreBundleDefinition
        {
            Id = 4536,
            Price = 1,
            AltPrice = 1,
            AltCurrencyPrice = 1,
            MembersOnlyPrice = 1,
            SalePrice = 1
        };

        var free = lot.AsFree();

        Assert.AreNotSame(lot, free);
        Assert.AreEqual(4536, free.Id);
        Assert.AreEqual(0, free.Price + free.AltPrice + free.AltCurrencyPrice + free.MembersOnlyPrice + free.SalePrice);
        Assert.AreEqual(1, lot.Price);
        Assert.AreEqual(1, lot.MembersOnlyPrice);
    }

    private void AddModel(int id, string fileName)
    {
        _resourceManager.Models.TryAdd(id, new ModelDefinition
        {
            Id = id,
            ModelFileName = fileName,
            Description = string.Empty
        });
    }

    private void AddItem(int id, int type, int category, string model, int param1 = 0, int iconTint = 0)
    {
        _resourceManager.ClientItemDefinitions.TryAdd(id, new ClientItemDefinition
        {
            Id = id,
            Type = type,
            CategoryId = category,
            ModelName = model,
            Param1 = param1,
            Icon = new IconData { TintId = iconTint }
        });
    }

    private static void StopWatcher(ResourceManager resourceManager)
    {
        var watcher = (FileSystemWatcher)typeof(ResourceManager)
            .GetField("_fileSystemWatcher", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resourceManager)!;

        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
    }
}
