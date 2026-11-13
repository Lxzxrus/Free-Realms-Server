using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;
using Sanctuary.Packet.Common.GameCommerce;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class WelcomeScreenHelperTests
{
    private static AppStoreBundleDefinition Bundle(
        int id, int categoryGroupId = 1, bool merchantOnly = false,
        int currencyType = 1, int nameId = 100, int descriptionId = 200,
        string? image = "123")
        => new()
        {
            Id = id,
            CategoryGroupId = categoryGroupId,
            IsMerchantOnly = merchantOnly,
            CurrencyType = currencyType,
            NameId = nameId,
            DescriptionId = descriptionId,
            Image = new ImageDataDefinition { Image = image }
        };

    private static StoreBundleCategoryNode Category(int id, int parentId)
        => new() { Id = id, ParentId = parentId };

    private static StoreBundleCategoryGroupDefinition Group(int id, params int[] categoryIds)
        => new() { Id = id, CategoryIds = categoryIds.ToList() };

    private static StoreBundleGroupDefinition Template()
        => new()
        {
            Id = 6,
            NameId = 10,
            DescriptionId = 20,
            Status = 1,
            Image = new ImageDataDefinition { Image = "999" },
            IsExclusive = true
        };

    [TestMethod]
    public void GetShowableCategoryGroupIds_CategoryWithParentZero_IsShowable()
    {
        var categories = new Dictionary<int, StoreBundleCategoryNode>
        {
            [1] = Category(1, 0)
        };
        var groups = new[] { Group(1, 1) };

        var result = WelcomeScreenHelper.GetShowableCategoryGroupIds(groups, categories);

        CollectionAssert.AreEquivalent(new[] { 1 }, result.ToArray());
    }

    [TestMethod]
    public void GetShowableCategoryGroupIds_TwoLevelChain_IsShowable()
    {
        var categories = new Dictionary<int, StoreBundleCategoryNode>
        {
            [12] = Category(12, 11),
            [11] = Category(11, 0)
        };
        var groups = new[] { Group(1, 12) };

        var result = WelcomeScreenHelper.GetShowableCategoryGroupIds(groups, categories);

        CollectionAssert.AreEquivalent(new[] { 1 }, result.ToArray());
    }

    [TestMethod]
    public void GetShowableCategoryGroupIds_ParentNotInCategories_NotShowable()
    {
        var categories = new Dictionary<int, StoreBundleCategoryNode>
        {
            [1] = Category(1, 5)   // 5 is not in the dictionary
        };
        var groups = new[] { Group(1, 1) };

        var result = WelcomeScreenHelper.GetShowableCategoryGroupIds(groups, categories);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetShowableCategoryGroupIds_Cycle_NotShowable()
    {
        var categories = new Dictionary<int, StoreBundleCategoryNode>
        {
            [21] = Category(21, 22),
            [22] = Category(22, 21)
        };
        var groups = new[] { Group(1, 21) };

        var result = WelcomeScreenHelper.GetShowableCategoryGroupIds(groups, categories);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetShowableCategoryGroupIds_EmptyListAndNullCategoryIds_NotShowable()
    {
        var categories = new Dictionary<int, StoreBundleCategoryNode>
        {
            [1] = Category(1, 0)
        };
        var emptyGroup = Group(1);
        var nullGroup = new StoreBundleCategoryGroupDefinition { Id = 2, CategoryIds = null! };
        var groups = new[] { emptyGroup, nullGroup };

        var result = WelcomeScreenHelper.GetShowableCategoryGroupIds(groups, categories);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void GetShowableCategoryGroupIds_OneBadOneGoodCategory_IsShowable()
    {
        var categories = new Dictionary<int, StoreBundleCategoryNode>
        {
            [1] = Category(1, 0),
            [2] = Category(2, 99)   // 99 not in dictionary
        };
        var groups = new[] { Group(1, 2, 1) };

        var result = WelcomeScreenHelper.GetShowableCategoryGroupIds(groups, categories);

        CollectionAssert.AreEquivalent(new[] { 1 }, result.ToArray());
    }

    [TestMethod]
    public void GetShowableCategoryGroupIds_CategoryIdZero_IsShowable()
    {
        var categories = new Dictionary<int, StoreBundleCategoryNode>();
        var groups = new[] { Group(1, 0) };

        var result = WelcomeScreenHelper.GetShowableCategoryGroupIds(groups, categories);

        CollectionAssert.AreEquivalent(new[] { 1 }, result.ToArray());
    }

    [TestMethod]
    public void GetPopularItems_MerchantOnlyBundles_NeverPicked()
    {
        var catalog = new[]
        {
            Bundle(1), Bundle(2), Bundle(3),
            Bundle(4, merchantOnly: true),
            Bundle(5, merchantOnly: true),
            Bundle(6, merchantOnly: true)
        };
        var showable = new HashSet<int> { 1 };

        var result = WelcomeScreenHelper.GetPopularItems(Template(), catalog, showable);

        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, result.Entries.Keys);
    }

    [TestMethod]
    public void GetPopularItems_CategoryNotInShowableSet_NeverPicked()
    {
        var catalog = new[]
        {
            Bundle(1, categoryGroupId: 1),
            Bundle(2, categoryGroupId: 2)
        };
        var showable = new HashSet<int> { 1 };

        var result = WelcomeScreenHelper.GetPopularItems(Template(), catalog, showable);

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Entries.Keys);
    }

    [TestMethod]
    public void GetPopularItems_TwentyEligible_CountEight_PicksEightDistinct()
    {
        var catalog = Enumerable.Range(1, 20).Select(i => Bundle(i)).ToArray();
        var showable = new HashSet<int> { 1 };

        var result = WelcomeScreenHelper.GetPopularItems(Template(), catalog, showable, count: 8);

        Assert.AreEqual(8, result.Entries.Count);
        var pickedIds = result.Entries.Keys.ToHashSet();
        Assert.AreEqual(8, pickedIds.Count);
        foreach (var id in pickedIds)
            Assert.IsTrue(id is >= 1 and <= 20);
    }

    [TestMethod]
    public void GetPopularItems_EntryKeyEqualsStoreBundleId_DisplayOrderIsSequential()
    {
        var catalog = new[] { Bundle(10), Bundle(20), Bundle(30) };
        var showable = new HashSet<int> { 1 };

        var result = WelcomeScreenHelper.GetPopularItems(Template(), catalog, showable);

        var displayOrders = new List<int>();
        foreach (var kvp in result.Entries)
        {
            Assert.AreEqual(kvp.Value.StoreBundleId, kvp.Key);
            displayOrders.Add(kvp.Value.DisplayOrder);
        }

        displayOrders.Sort();
        CollectionAssert.AreEquivalent(Enumerable.Range(1, result.Entries.Count).ToList(), displayOrders);
    }

    [TestMethod]
    public void GetPopularItems_CopiesTemplateFields()
    {
        var template = Template();
        var catalog = new[] { Bundle(1) };
        var showable = new HashSet<int> { 1 };

        var result = WelcomeScreenHelper.GetPopularItems(template, catalog, showable);

        Assert.AreEqual(template.Id, result.Id);
        Assert.AreEqual(template.NameId, result.NameId);
        Assert.AreEqual(template.DescriptionId, result.DescriptionId);
        Assert.AreEqual(template.Status, result.Status);
        Assert.AreSame(template.Image, result.Image);
        Assert.AreEqual(template.IsExclusive, result.IsExclusive);
    }

    [TestMethod]
    public void GetPopularItems_OlderFilters_StillApply()
    {
        var catalog = new[]
        {
            Bundle(1),                              // good
            Bundle(2, currencyType: 2),            // wrong currency
            Bundle(3, nameId: 0),                  // nameId 0
            Bundle(4, descriptionId: 0),           // descriptionId 0
            Bundle(5, image: "abc"),               // non-numeric image
            Bundle(6, image: "0"),                 // image id 0
            Bundle(7, image: null)                 // null image
        };
        var showable = new HashSet<int> { 1 };

        var result = WelcomeScreenHelper.GetPopularItems(Template(), catalog, showable);

        CollectionAssert.AreEquivalent(new[] { 1 }, result.Entries.Keys);
    }

    [TestMethod]
    public void GetPopularItems_DuplicateBundleId_PickedOnce()
    {
        var catalog = new[]
        {
            Bundle(5),
            Bundle(5),
            Bundle(6)
        };
        var showable = new HashSet<int> { 1 };

        var result = WelcomeScreenHelper.GetPopularItems(Template(), catalog, showable);

        CollectionAssert.AreEquivalent(new[] { 5, 6 }, result.Entries.Keys);
    }
}
