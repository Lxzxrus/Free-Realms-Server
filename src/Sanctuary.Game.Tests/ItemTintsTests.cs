using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Rewards are saved at the item's own tint, not 0: Ariel's white Striped T-Shirt from Missing Homework was neon red.
/// </summary>
[TestClass]
public sealed class ItemTintsTests
{
    private const int StripedTShirt = 248;
    private const int Mushroom = 11082;
    private const int Untinted = 500;

    private static readonly Dictionary<int, ClientItemDefinition> Definitions = new()
    {
        [StripedTShirt] = Item(StripedTShirt, iconTint: 228, isTintable: true),
        [Mushroom] = Item(Mushroom, iconTint: 229),
        [Untinted] = Item(Untinted, iconTint: 0)
    };

    [TestMethod]
    public void Default_IsTheIconTint_TintableOrNot()
    {
        Assert.AreEqual(228, ItemTints.Default(Definitions[StripedTShirt]));
        Assert.AreEqual(229, ItemTints.Default(Definitions[Mushroom]));
        Assert.AreEqual(0, ItemTints.Default(Definitions[Untinted]));
    }

    [TestMethod]
    public void ShippedStripedTShirt_HasAWhiteTintOfItsOwn()
    {
        // Missing Homework (3074) rewards item 248, the shirt Ariel saw turn red.
        var path = FindResource("ClientItemDefinitions.json");
        var definitions = new ClientItemDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(definitions.Load(path));

        var shirt = definitions[StripedTShirt];
        Assert.IsTrue(shirt.IsTintable);
        Assert.AreEqual(228, ItemTints.Default(shirt));
    }

    [TestMethod]
    public void FindMistinted_RetintsTintZeroRowsOfItemsWithATintOfTheirOwn()
    {
        var shirt = Row(1, StripedTShirt, tint: 0);
        var mushroom = Row(2, Mushroom, tint: 0);
        var untinted = Row(3, Untinted, tint: 0);
        var dyed = Row(4, StripedTShirt, tint: 50);
        var unknown = Row(5, 999_999, tint: 0);

        var found = ItemTints.FindMistinted([shirt, mushroom, untinted, dyed, unknown], Definitions);

        CollectionAssert.AreEquivalent(new[] { (shirt, 228), (mushroom, 229) }, found);
    }

    [TestMethod]
    public void FindMistinted_LeavesARowWhoseOwnTintTheCharacterAlreadyHolds()
    {
        // One row per item and tint: the old tint-0 shirt can't become a second row at 228.
        var oldReward = Row(1, StripedTShirt, tint: 0);
        var starterShirt = Row(2, StripedTShirt, tint: 228);

        Assert.AreEqual(0, ItemTints.FindMistinted([oldReward, starterShirt], Definitions).Count);
    }

    [TestMethod]
    public void FindMistinted_LeavesItemsTheCallerKeeps()
    {
        var found = ItemTints.FindMistinted([Row(1, StripedTShirt, tint: 0), Row(2, Mushroom, tint: 0)], Definitions,
            keep: definition => definition.Id == StripedTShirt);

        Assert.AreEqual(Mushroom, found.Single().Item.Definition);
    }

    private static DbItem Row(int id, int definition, int tint) =>
        new() { Id = id, Definition = definition, Tint = tint, Count = 1 };

    private static ClientItemDefinition Item(int id, int iconTint, bool isTintable = false) => new()
    {
        Id = id,
        Type = 1,
        IsTintable = isTintable,
        TintAlias = isTintable ? "dyetint" : string.Empty,
        Icon = new IconData { TintId = iconTint }
    };

    private static string FindResource(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", name);

            if (File.Exists(candidate))
                return candidate;
        }

        Assert.Fail($"src/Resources/{name} not found above the test output.");
        return string.Empty;
    }
}
