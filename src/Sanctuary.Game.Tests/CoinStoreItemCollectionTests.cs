using System;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

/// <summary>
/// An item definition request for a coin store item also sends the items after it in its category. It used to copy
/// and walk all 17,000 coin store items for each request, so the coin store indexes its categories when it loads.
/// </summary>
[TestClass]
public sealed class CoinStoreItemCollectionTests
{
    private const int BatchSize = 100;

    private static CoinStoreItemCollection Load(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"coin-store-items-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        try
        {
            var items = new CoinStoreItemCollection(NullLogger.Instance);
            Assert.IsTrue(items.Load(path));
            return items;
        }
        finally
        {
            File.Delete(path);
        }
    }

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
    public void AnItem_ComesWithTheItemsAfterItInItsCategory_InTheFilesOrder()
    {
        // Categories interleave in the file, as they do in CoinStoreItems.json.
        var items = Load("""
            [
                { "Id": 10, "CategoryId": 1 }, { "Id": 20, "CategoryId": 2 }, { "Id": 11, "CategoryId": 1 },
                { "Id": 12, "CategoryId": 1 }, { "Id": 21, "CategoryId": 2 }, { "Id": 13, "CategoryId": 1 }
            ]
            """);

        CollectionAssert.AreEqual(new[] { 11, 12, 13 }, items.GetCategoryItemIdsFrom(11, BatchSize).ToArray());
        CollectionAssert.AreEqual(new[] { 10, 11 }, items.GetCategoryItemIdsFrom(10, 2).ToArray());
        CollectionAssert.AreEqual(new[] { 13 }, items.GetCategoryItemIdsFrom(13, BatchSize).ToArray());
        CollectionAssert.AreEqual(new[] { 20, 21 }, items.GetCategoryItemIdsFrom(20, BatchSize).ToArray());
    }

    [TestMethod]
    public void AnItemTheStoreDoesntSell_ComesWithNothing()
    {
        var items = Load("""[ { "Id": 10, "CategoryId": 1 } ]""");

        Assert.AreEqual(0, items.GetCategoryItemIdsFrom(99, BatchSize).Length);
        Assert.AreEqual(0, items.GetCategoryItemIdsFrom(10, 0).Length);
    }

    [TestMethod]
    public void ADuplicateId_KeepsItsFirstEntry_AsTheCollectionDoes()
    {
        var items = Load("""
            [ { "Id": 10, "CategoryId": 1 }, { "Id": 11, "CategoryId": 1 }, { "Id": 10, "CategoryId": 2 } ]
            """);

        Assert.AreEqual(1, items[10].CategoryId);
        CollectionAssert.AreEqual(new[] { 10, 11 }, items.GetCategoryItemIdsFrom(10, BatchSize).ToArray());
    }

    [TestMethod]
    public void ShippedCoinStore_EveryItemsBatch_IsItAndTheNextItemsOfItsCategory()
    {
        var items = new CoinStoreItemCollection(NullLogger.Instance);
        Assert.IsTrue(items.Load(Path.Combine(FindResourcesDirectory(), "CoinStoreItems.json")));

        var fullBatches = 0;

        foreach (var (id, item) in items)
        {
            var batch = items.GetCategoryItemIdsFrom(id, BatchSize);

            Assert.IsTrue(batch.Length is >= 1 and <= BatchSize, $"Item {id} has a batch of {batch.Length}.");
            Assert.AreEqual(id, batch[0], $"Item {id}'s batch doesn't start with it.");

            foreach (var batchItemId in batch)
                Assert.AreEqual(item.CategoryId, items[batchItemId].CategoryId, $"Item {batchItemId} is in item {id}'s batch.");

            if (batch.Length == BatchSize)
                fullBatches++;
        }

        // Category 6 alone holds 7,000 items, so most batches are full ones.
        Assert.IsTrue(fullBatches > items.Count / 2, $"Only {fullBatches} full batches.");
    }
}
