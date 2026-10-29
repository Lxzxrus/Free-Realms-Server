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
        var definition = new CollectionNodePoolDefinition
        {
            Key = "test",
            NodeType = "test",
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
            "[{\"Key\":\"test\",\"ZoneDefinitionId\":1,\"NodeType\":\"test\"," +
            "\"MaxActiveNodes\":1,\"RespawnSeconds\":30," +
            "\"DropTable\":[{\"ItemDefinitionId\":1001,\"Weight\":0}]}]");

        try
        {
            var definitions = new CollectionNodePoolDefinitionCollection(NullLogger.Instance);

            Assert.IsFalse(definitions.Load(path));
            Assert.AreEqual(0, definitions.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_ReadsNamedTypeWithoutDrops()
    {
        // Exploration coins: the node's name comes from NameId and it drops nothing; its spawn names a collection entry.
        var path = Path.Combine(Path.GetTempPath(), $"collection-node-types-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            "[{\"Key\":\"Coin\",\"Name\":\"Coin\",\"NameId\":386170,\"ModelId\":816,\"DropTable\":[]}]");

        try
        {
            var definitions = new CollectionNodeTypeDefinitionCollection(NullLogger.Instance);

            Assert.IsTrue(definitions.Load(path));
            // Drop tables live on pools since upstream OSFR #151, so a type has none of its own to read.
            Assert.AreEqual(386170, definitions["coin"].NameId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Load_RejectsNegativeNameId()
    {
        var path = Path.Combine(Path.GetTempPath(), $"collection-node-types-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "[{\"Key\":\"test\",\"Name\":\"Test\",\"ModelId\":1,\"DropTable\":[],\"NameId\":-1}]");

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
        var common = new CollectionNodePoolDefinition
        {
            Key = "mushrooms",
            NodeType = "mushrooms",
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
        var rare = new CollectionNodePoolDefinition
        {
            Key = "rare-mushrooms",
            NodeType = "rare-mushrooms",
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
