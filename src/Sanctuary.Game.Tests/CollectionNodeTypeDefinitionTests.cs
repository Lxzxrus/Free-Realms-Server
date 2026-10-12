using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class CollectionNodeTypeDefinitionTests
{
    [TestMethod]
    [DataRow(0, 1001)]
    [DataRow(2, 1001)]
    [DataRow(3, 1002)]
    [DataRow(3 + 6, 1002)]
    [DataRow(3 + 7, 1003)]
    public void SelectItemDefinitionId_UsesWeightBoundaries(int roll, int expectedItemDefinitionId)
    {
        var definition = new CollectionNodeTypeDefinition
        {
            Key = "test",
            Name = "Test",
            DropTable =
            [
                new CollectionNodeDropDefinition { ItemDefinitionId = 1001, Weight = 3 },
                new CollectionNodeDropDefinition { ItemDefinitionId = 1002, Weight = 7 },
                new CollectionNodeDropDefinition { ItemDefinitionId = 1003, Weight = 1 }
            ]
        };

        Assert.AreEqual(expectedItemDefinitionId, definition.Table.Select(roll).ItemDefinitionId);
    }

    [TestMethod]
    public void Load_RejectsNonPositiveDropWeight()
    {
        var path = Path.Combine(Path.GetTempPath(), $"collection-node-types-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            "[{\"Key\":\"test\",\"Name\":\"Test\",\"ModelId\":1," +
            "\"DropTable\":[{\"ItemDefinitionId\":1001,\"Weight\":0}]}]");

        try
        {
            var definitions = new CollectionNodeTypeDefinitionCollection(NullLogger.Instance);

            Assert.IsFalse(definitions.Load(path));
            Assert.AreEqual(0, definitions.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_ReadsCoinOnlyType()
    {
        var path = Path.Combine(Path.GetTempPath(), $"collection-node-types-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            "[{\"Key\":\"Coin\",\"Name\":\"Coin\",\"NameId\":386170,\"ModelId\":816,\"DropTable\":[],\"Coins\":10}]");

        try
        {
            var definitions = new CollectionNodeTypeDefinitionCollection(NullLogger.Instance);

            Assert.IsTrue(definitions.Load(path));
            Assert.AreEqual(10, definitions["coin"].Coins);
            Assert.AreEqual(386170, definitions["coin"].NameId);
            Assert.IsFalse(definitions["coin"].HasDrop);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("\"DropTable\":[{\"ItemDefinitionId\":1001,\"Weight\":1}],\"Coins\":10")]
    [DataRow("\"DropTable\":[],\"Coins\":-1")]
    [DataRow("\"DropTable\":[],\"NameId\":-1")]
    public void Load_RejectsInvalidCoinType(string fields)
    {
        var path = Path.Combine(Path.GetTempPath(), $"collection-node-types-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "[{\"Key\":\"test\",\"Name\":\"Test\",\"ModelId\":1," + fields + "}]");

        try
        {
            var definitions = new CollectionNodeTypeDefinitionCollection(NullLogger.Instance);

            Assert.IsFalse(definitions.Load(path));
            Assert.AreEqual(0, definitions.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void CommonAndRareDropTables_CanPartitionOneCollection()
    {
        var common = new CollectionNodeTypeDefinition
        {
            Key = "mushrooms",
            Name = "Mushrooms",
            DropTable =
            [
                new CollectionNodeDropDefinition { ItemDefinitionId = 11082 },
                new CollectionNodeDropDefinition { ItemDefinitionId = 11083 },
                new CollectionNodeDropDefinition { ItemDefinitionId = 11084 },
                new CollectionNodeDropDefinition { ItemDefinitionId = 11086 },
                new CollectionNodeDropDefinition { ItemDefinitionId = 11087 },
                new CollectionNodeDropDefinition { ItemDefinitionId = 11088 }
            ]
        };
        var rare = new CollectionNodeTypeDefinition
        {
            Key = "rare-mushrooms",
            Name = "Rare Mushrooms",
            DropTable =
            [
                new CollectionNodeDropDefinition { ItemDefinitionId = 11081 },
                new CollectionNodeDropDefinition { ItemDefinitionId = 11085 }
            ]
        };

        var allDrops = new HashSet<int>();
        foreach (var drop in common.DropTable)
            Assert.IsTrue(allDrops.Add(drop.ItemDefinitionId));
        foreach (var drop in rare.DropTable)
            Assert.IsTrue(allDrops.Add(drop.ItemDefinitionId));

        Assert.AreEqual(8, allDrops.Count);
    }
}
