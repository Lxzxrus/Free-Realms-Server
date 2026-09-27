using System;
using System.IO;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class QuestDefinitionCollectionTests
{
    private static string FindShippedQuests()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", "Quests.json");

            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("src/Resources/Quests.json not found above the test output.");
    }

    [TestMethod]
    public void ShippedQuests_LoadAndIndexEveryGiver()
    {
        var quests = new QuestDefinitionCollection(NullLogger.Instance);

        Assert.IsTrue(quests.Load(FindShippedQuests()));
        Assert.IsTrue(quests.Quests.Count > 0);

        foreach (var quest in quests.Quests.Values)
        {
            Assert.IsTrue(quest.Goals.Count > 0, $"Quest {quest.QuestId} has no goals.");

            if (quest.GiverGuid != 0)
                Assert.IsTrue(quests.ByGiver.TryGetValue(quest.GiverGuid, out var ids) && ids.Contains(quest.QuestId),
                    $"Quest {quest.QuestId} is missing from its giver's index.");
        }
    }
}
