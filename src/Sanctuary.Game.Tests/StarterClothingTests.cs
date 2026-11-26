using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A new character starts in clothes from the character creator's outfits and nothing else
/// (<c>Resources/CharacterCreate/StarterClothing.txt</c>).
/// </summary>
[TestClass]
public sealed class StarterClothingTests
{
    private static string ShippedFile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", "CharacterCreate", "StarterClothing.txt");
            if (File.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "Resources", "Quests.json")))
                return candidate;
        }

        throw new FileNotFoundException("src/Resources/CharacterCreate/StarterClothing.txt not found above the test output.");
    }

    private static StarterClothingCollection Shipped()
    {
        var clothing = new StarterClothingCollection(NullLogger.Instance);
        Assert.IsTrue(clothing.Load(ShippedFile()));
        return clothing;
    }

    private static StarterClothingCollection From(string text, out bool loaded)
    {
        var path = Path.Combine(Path.GetTempPath(), "starter-clothing-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, text);

        try
        {
            var clothing = new StarterClothingCollection(NullLogger.Instance);
            loaded = clothing.Load(path);
            return clothing;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TheShippedListHasTheCreatorsOutfits()
    {
        Assert.AreEqual(36, Shipped().Count);
    }

    [TestMethod]
    [DataRow(418, 944, 416, DisplayName = "outfit 0")]
    [DataRow(76499, 76498, 76497, DisplayName = "outfit 9")]
    [DataRow(17412, 14964, 28178, DisplayName = "outfit 12, which the load-test bot wears")]
    [DataRow(77686, 77694, 77681, DisplayName = "outfit 35")]
    public void AnOutfitFromTheCreatorIsAllowed(int feet, int legs, int chest)
    {
        Assert.IsTrue(Shipped().Allows(feet, legs, chest));
    }

    [TestMethod]
    public void PiecesFromDifferentOutfitsAreAllowed()
    {
        // feet of outfit 0, legs of outfit 12, chest of outfit 35
        Assert.IsTrue(Shipped().Allows(418, 14964, 77681));
    }

    [TestMethod]
    [DataRow(2929, 944, 416, DisplayName = "another item as feet")]
    [DataRow(418, 2929, 416, DisplayName = "another item as legs")]
    [DataRow(418, 944, 2929, DisplayName = "another item as chest")]
    [DataRow(101, 103, 104, DisplayName = "real clothes that are not a starting choice")]
    [DataRow(76471, 76471, 76471, DisplayName = "one item three times")]
    [DataRow(0, 0, 0, DisplayName = "nothing")]
    [DataRow(-1, 944, 416, DisplayName = "a negative id")]
    public void AnythingElseIsRefused(int feet, int legs, int chest)
    {
        Assert.IsFalse(Shipped().Allows(feet, legs, chest));
    }

    [TestMethod]
    public void APieceInTheWrongSlotIsRefused()
    {
        // outfit 0 with its chest and feet swapped
        Assert.IsFalse(Shipped().Allows(416, 944, 418));
    }

    [TestMethod]
    public void EveryShippedPieceIsInTheItemCatalogue()
    {
        var resources = Path.GetDirectoryName(Path.GetDirectoryName(ShippedFile()))!;
        var catalogue = File.ReadAllText(Path.Combine(resources, "ClientItemDefinitions.json"));

        var ids = File.ReadAllLines(ShippedFile())
            .Where(line => !line.StartsWith('#') && line.Trim().Length > 0)
            .SelectMany(line => line.Split('^', StringSplitOptions.RemoveEmptyEntries).Skip(1))
            .Distinct()
            .ToList();

        Assert.AreEqual(99, ids.Count);

        // Character create also looks each piece up in the catalogue, so one missing there could never be chosen.
        foreach (var id in ids)
            Assert.IsTrue(catalogue.Contains($"\"Id\": {id},") || catalogue.Contains($"\"Id\":{id},"), $"item {id} is not in ClientItemDefinitions.json");
    }

    [TestMethod]
    public void AnEmptyListFailsToLoad()
    {
        From("#ID^FEET_ITEM_ID^LEGS_ITEM_ID^CHEST_ITEM_ID^\n", out var loaded);

        Assert.IsFalse(loaded);
    }

    [TestMethod]
    public void ABadLineFailsToLoad()
    {
        From("#ID^FEET_ITEM_ID^LEGS_ITEM_ID^CHEST_ITEM_ID^\n0^418^944^416^\n1^boots^948^15358^\n", out var loaded);

        Assert.IsFalse(loaded);
    }

    [TestMethod]
    public void AFailedReloadKeepsTheListFromBefore()
    {
        var clothing = Shipped();

        var path = Path.Combine(Path.GetTempPath(), "starter-clothing-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, string.Empty);

        try
        {
            Assert.IsFalse(clothing.Load(path));
        }
        finally
        {
            File.Delete(path);
        }

        Assert.AreEqual(36, clothing.Count);
        Assert.IsTrue(clothing.Allows(418, 944, 416));
    }
}
