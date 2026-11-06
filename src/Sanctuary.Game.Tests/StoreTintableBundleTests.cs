using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Whether an item comes in a colour of the buyer's choice decides a mount's item definition, once per id a bundle
/// content request names. It walked every bundle of every store for each id, so the stores gather the items of their
/// tintable bundles when the bundles load.
/// </summary>
[TestClass]
public sealed class StoreTintableBundleTests
{
    private static string FindResourcesDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources");
            if (File.Exists(Path.Combine(candidate, "StoreBundles.json")))
                return candidate;
        }

        throw new DirectoryNotFoundException("src/Resources not found above the test output.");
    }

    [TestMethod]
    public void ItemsOfTintableBundles_InAnyStore_AreFound()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"stores-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var storesFile = Path.Combine(directory, "Stores.json");
            var bundlesFile = Path.Combine(directory, "StoreBundles.json");

            File.WriteAllText(storesFile, """[ { "Id": 1 }, { "Id": 2 } ]""");
            File.WriteAllText(bundlesFile, """
                [
                    { "Id": 10, "StoreId": 1, "IsTintable": true,
                      "Entries": [ { "Quantity": 1, "MarketingItemId": 100 }, { "Quantity": 1, "MarketingItemId": 101 } ] },
                    { "Id": 11, "StoreId": 1, "IsTintable": false, "Entries": [ { "Quantity": 1, "MarketingItemId": 200 } ] },
                    { "Id": 20, "StoreId": 2, "IsTintable": true, "Entries": [ { "Quantity": 1, "MarketingItemId": 300 } ] }
                ]
                """);

            var stores = new StoreDefinitionCollection(NullLogger.Instance);
            Assert.IsTrue(stores.Load(storesFile));
            Assert.IsTrue(stores.LoadBundles(bundlesFile));

            Assert.IsTrue(stores.IsInTintableBundle(100));
            Assert.IsTrue(stores.IsInTintableBundle(101));
            Assert.IsTrue(stores.IsInTintableBundle(300), "A tintable bundle of the second store.");
            Assert.IsFalse(stores.IsInTintableBundle(200), "Only in a bundle that isn't tintable.");
            Assert.IsFalse(stores.IsInTintableBundle(999), "In no bundle.");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void ShippedStores_AgreeWithAWalkOfEveryBundle()
    {
        var resources = FindResourcesDirectory();
        var stores = new StoreDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(stores.Load(Path.Combine(resources, "Stores.json")), "Stores.json failed to load.");
        Assert.IsTrue(stores.LoadBundles(Path.Combine(resources, "StoreBundles.json")), "StoreBundles.json failed to load.");

        var bundles = stores.Values.SelectMany(store => store.Bundles.Values).ToList();
        var itemIds = bundles.SelectMany(bundle => bundle.Entries).Select(entry => entry.MarketingItemId).Distinct().ToList();
        var tintable = 0;

        foreach (var itemId in itemIds)
        {
            var walked = bundles.Any(bundle => bundle.IsTintable && bundle.Entries.Any(entry => entry.MarketingItemId == itemId));

            Assert.AreEqual(walked, stores.IsInTintableBundle(itemId), $"Item {itemId}.");

            if (walked)
                tintable++;
        }

        Assert.IsTrue(tintable > 0 && tintable < itemIds.Count, $"{tintable} of {itemIds.Count} items are in a tintable bundle.");
    }
}
