using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;
using Sanctuary.Game.Store;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.GameCommerce;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Buying took the client's quantity and colour on trust. A free item with a tint group came in any made-up tint, a new
/// inventory row each time and any number in one request, and a coin pet was a database row per unit asked for. A free
/// Station Cash bundle came the same way, in any tint and any number of bundles.
/// </summary>
[TestClass]
public sealed class StorePurchaseTests
{
    private static ClientItemDefinition Item(int id = 0, int maxStackSize = -1, int iconTintId = 0) =>
        new() { Id = id, MaxStackSize = maxStackSize, Icon = { TintId = iconTintId } };

    private static AppStoreBundleDefinition Bundle(int id, string? imageTint = null, string? tintGroup = null, int maxPerOrder = 0) =>
        new() { Id = id, Image = { Tint = imageTint }, TintGroup = tintGroup, IsTintable = tintGroup is not null, MaxPerOrder = maxPerOrder };

    private static string FindResourcesDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources");
            if (File.Exists(Path.Combine(candidate, "CoinStoreItems.json")))
                return candidate;
        }

        throw new DirectoryNotFoundException("src/Resources not found above the test output.");
    }

    [TestMethod]
    public void AnItemWithNoStackLimit_SellsUpToTheQuantityPickersNinetyNine()
    {
        foreach (var maxStackSize in new[] { -1, 0 })
        {
            Assert.IsTrue(StorePurchase.IsQuantityAllowed(Item(maxStackSize: maxStackSize), 1));
            Assert.IsTrue(StorePurchase.IsQuantityAllowed(Item(maxStackSize: maxStackSize), 99));
            Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(maxStackSize: maxStackSize), 100));
            Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(maxStackSize: maxStackSize), int.MaxValue));
        }
    }

    [TestMethod]
    public void AnItemWithAStackLimit_SellsUpToItsLimit()
    {
        Assert.IsTrue(StorePurchase.IsQuantityAllowed(Item(maxStackSize: 50), 50));
        Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(maxStackSize: 50), 51));
        Assert.IsTrue(StorePurchase.IsQuantityAllowed(Item(maxStackSize: 1), 1));
        Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(maxStackSize: 1), 2));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(-1_000_000L)]
    public void NoneOrANegativeQuantity_IsRefused(long quantity)
    {
        Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(), quantity));
        Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(maxStackSize: 50), quantity));
    }

    [TestMethod]
    public void AnOrderOfManyPets_IsRefused()
    {
        // 15956, Taz: 9 coins for a member and no stack limit, like every coin store pet. 1,000 starting coins bought 111.
        var taz = new ClientItemDefinition { Id = 15956, Type = 2, MaxStackSize = -1 };

        Assert.IsTrue(StorePurchase.IsQuantityAllowed(taz, 1));
        Assert.IsFalse(StorePurchase.IsQuantityAllowed(taz, 111));
    }

    [TestMethod]
    public void AColourOfTheItemsTintGroup_IsOffered()
    {
        // 2713, Yeti Domino: free, tint group 2, no icon tint.
        var yetiDomino = Item(2713);

        foreach (var tint in StorePurchase.TintGroups[2])
            Assert.IsTrue(StorePurchase.IsOfferedTint(yetiDomino, 2, tint), $"Tint {tint}");

        // Group 3's mahogany is tint 96, and the dye palette's mahogany isn't one of its colours.
        Assert.IsTrue(StorePurchase.IsOfferedTint(yetiDomino, 3, 96));
        Assert.IsFalse(StorePurchase.IsOfferedTint(yetiDomino, 3, 229));
    }

    [TestMethod]
    public void TheItemsOwnTint_OrNone_IsOffered()
    {
        // 55830, Doggy Graduation Cap: free, tint group 1, but its own tint (264, darkmatter) is one of group 3's.
        var doggyGraduationCap = Item(55830, iconTintId: 264);

        Assert.IsTrue(StorePurchase.IsOfferedTint(doggyGraduationCap, 1, 264));
        Assert.IsTrue(StorePurchase.IsOfferedTint(doggyGraduationCap, 1, 0));
        Assert.IsTrue(StorePurchase.IsOfferedTint(doggyGraduationCap, 0, 264), "No tint group: its own tint.");
    }

    [TestMethod]
    [DataRow(1, 227)]
    [DataRow(1, 1)]
    [DataRow(2, 96)]
    [DataRow(0, 228)]
    [DataRow(7, 517)]
    [DataRow(1, -1)]
    [DataRow(1, int.MaxValue)]
    public void AnyOtherTint_IsNotOffered(int tintGroupId, int tint)
    {
        // 225, Snowman: free housing, tint group 1, no icon tint.
        Assert.IsFalse(StorePurchase.IsOfferedTint(Item(225), tintGroupId, tint));
    }

    [TestMethod]
    public void EveryShippedCoinStoreItem_HasATintGroupWhoseColoursAreKnown()
    {
        var items = new CoinStoreItemCollection(NullLogger.Instance);
        Assert.IsTrue(items.Load(Path.Combine(FindResourcesDirectory(), "CoinStoreItems.json")));

        var unknown = items.Values
            .Where(item => item.TintGroupId != 0 && !StorePurchase.TintGroups.ContainsKey(item.TintGroupId))
            .Select(item => $"{item.Id} (group {item.TintGroupId})")
            .ToList();

        // A group missing from StorePurchase.TintGroups would leave a free item only its own tint.
        Assert.AreEqual(0, unknown.Count, "Tint groups StorePurchase doesn't know: " + string.Join(", ", unknown.Take(10)));
    }

    [TestMethod]
    public void AnOrder_SellsUpToTheQuantityPickersNinetyNineBundles()
    {
        // 100013, Yellow Fluffy Cloud (6): the picker counts bundles, so 99 of them are 594 clouds.
        var clouds = Bundle(100013, imageTint: "237");
        clouds.Entries.Add(new MarketingBundleDefinition.Entry { Quantity = 6, MarketingItemId = 15499 });

        Assert.IsTrue(StorePurchase.IsOrderQuantityAllowed(clouds, 1));
        Assert.IsTrue(StorePurchase.IsOrderQuantityAllowed(clouds, 99));
        Assert.IsFalse(StorePurchase.IsOrderQuantityAllowed(clouds, 100));
        Assert.IsFalse(StorePurchase.IsOrderQuantityAllowed(clouds, int.MaxValue));
        Assert.IsFalse(StorePurchase.IsOrderQuantityAllowed(clouds, 0));
        Assert.IsFalse(StorePurchase.IsOrderQuantityAllowed(clouds, -1));
    }

    [TestMethod]
    public void ABundleWithAMaxPerOrder_SellsUpToIt()
    {
        // The Marketplace's picker goes to a bundle's MaxPerOrder when it has one, above 99 or below it.
        Assert.IsTrue(StorePurchase.IsOrderQuantityAllowed(Bundle(1, maxPerOrder: 5), 5));
        Assert.IsFalse(StorePurchase.IsOrderQuantityAllowed(Bundle(1, maxPerOrder: 5), 6));
        Assert.IsTrue(StorePurchase.IsOrderQuantityAllowed(Bundle(1, maxPerOrder: 200), 200));
        Assert.IsFalse(StorePurchase.IsOrderQuantityAllowed(Bundle(1, maxPerOrder: 200), 201));
    }

    [TestMethod]
    public void AFreeBundle_ComesInNoMadeUpTint()
    {
        // 50037, Basic Swimming Pool: free for members, not tintable and in no tint group, so the client sends 0. Every
        // other tint made a new free inventory row.
        var pool = Bundle(50037, imageTint: "233");

        Assert.IsTrue(StorePurchase.IsOfferedTint(pool, 0));
        Assert.IsTrue(StorePurchase.IsOfferedTint(pool, 233), "Its own tint, as for a tintable bundle.");
        Assert.IsFalse(StorePurchase.IsOfferedTint(pool, 1));
        Assert.IsFalse(StorePurchase.IsOfferedTint(pool, 227), "A colour of a group it isn't in.");
        Assert.IsFalse(StorePurchase.IsOfferedTint(pool, -1));
    }

    [TestMethod]
    public void ATintableBundle_ComesInItsGroupsColours_AndItsOwnTint()
    {
        // 50047, Juice Bar: free for members, tintable in group 2, its image tinted 227.
        var juiceBar = Bundle(50047, imageTint: "227", tintGroup: "2");

        foreach (var tint in StorePurchase.TintGroups[2])
            Assert.IsTrue(StorePurchase.IsOfferedTint(juiceBar, tint), $"Tint {tint}");

        Assert.IsTrue(StorePurchase.IsOfferedTint(juiceBar, 0));
        Assert.IsFalse(StorePurchase.IsOfferedTint(juiceBar, 228), "Group 1's thunderbird.");
        Assert.IsFalse(StorePurchase.IsOfferedTint(juiceBar, 96), "Group 3's mahogany.");
        Assert.IsFalse(StorePurchase.IsOfferedTint(juiceBar, int.MaxValue));

        // 30, Bermuda Shorts: group 2, but its image is tinted 121. The client starts from that tint and sends it unless
        // a colour gets picked.
        Assert.IsTrue(StorePurchase.IsOfferedTint(Bundle(30, imageTint: "121", tintGroup: "2"), 121));

        // 50045, Festival Pavilion: free for members, group 2 and no image tint, so nothing beyond the group and 0.
        var pavilion = Bundle(50045, tintGroup: "2");
        Assert.IsTrue(StorePurchase.IsOfferedTint(pavilion, 245));
        Assert.IsFalse(StorePurchase.IsOfferedTint(pavilion, 121));
    }

    [TestMethod]
    public void EveryShippedStoreBundle_HasATintGroupWhoseColoursAreKnown()
    {
        var resources = FindResourcesDirectory();
        var stores = new StoreDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(stores.Load(Path.Combine(resources, "Stores.json")));
        Assert.IsTrue(stores.LoadBundles(Path.Combine(resources, "StoreBundles.json")));

        var unknown = stores.Values
            .SelectMany(store => store.Bundles.Values)
            .Where(bundle => !string.IsNullOrEmpty(bundle.TintGroup) &&
                !(int.TryParse(bundle.TintGroup, out var tintGroupId) && StorePurchase.TintGroups.ContainsKey(tintGroupId)))
            .Select(bundle => $"{bundle.Id} (group {bundle.TintGroup})")
            .ToList();

        // A group missing from StorePurchase.TintGroups would leave a free bundle only its own tint and 0.
        Assert.AreEqual(0, unknown.Count, "Tint groups StorePurchase doesn't know: " + string.Join(", ", unknown.Take(10)));
    }
}
