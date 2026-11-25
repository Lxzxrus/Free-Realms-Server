using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A new character's model is one of the player models in the shipped model list (<c>Resources/Models.txt</c>), never an
/// npc's or a creature's.
/// </summary>
[TestClass]
public sealed class PlayerModelTests
{
    private static ModelDefinitionCollection Shipped()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", "Models.txt");

            if (File.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "Resources", "Quests.json")))
            {
                var models = new ModelDefinitionCollection(NullLogger.Instance);
                Assert.IsTrue(models.Load(candidate));
                return models;
            }
        }

        throw new FileNotFoundException("src/Resources/Models.txt not found above the test output.");
    }

    [TestMethod]
    public void TheShippedListHasTheFourPlayerModels()
    {
        var playerModels = Shipped().Values.Where(model => model.IsValidForPC).Select(model => model.Id).Order().ToArray();

        // human boy, pixie boy, human girl, pixie girl: the four the character creator offers
        CollectionAssert.AreEqual(new[] { 1, 2, 60, 61 }, playerModels);
    }

    [TestMethod]
    [DataRow(1, DisplayName = "human boy")]
    [DataRow(2, DisplayName = "pixie boy")]
    [DataRow(60, DisplayName = "human girl")]
    [DataRow(61, DisplayName = "pixie girl")]
    public void APlayerModelIsAllowed(int id)
    {
        Assert.IsTrue(Shipped().TryGetPlayerModel(id, out var model));
        Assert.AreEqual(id, model.Id);
    }

    [TestMethod]
    [DataRow(3, DisplayName = "an npc's model")]
    [DataRow(4, DisplayName = "a creature's model")]
    public void AnotherModelInTheListIsRefused(int id)
    {
        var models = Shipped();

        Assert.IsTrue(models.ContainsKey(id), "the model is in the list, so only the player-model rule can refuse it");
        Assert.IsFalse(models.TryGetPlayerModel(id, out _));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MaxValue)]
    public void AModelThatIsNotInTheListIsRefused(int id)
    {
        Assert.IsFalse(Shipped().TryGetPlayerModel(id, out _));
    }
}
