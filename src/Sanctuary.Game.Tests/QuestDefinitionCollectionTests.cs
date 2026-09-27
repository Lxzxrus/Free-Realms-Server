using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class QuestDefinitionCollectionTests
{
    private static string FindSourceFile(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);

            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"src/{string.Join('/', relativePath)} not found above the test output.");
    }

    private static string FindShippedQuests() => FindSourceFile("Resources", "Quests.json");

    private static QuestDefinitionCollection LoadQuests(string path)
    {
        var quests = new QuestDefinitionCollection(NullLogger.Instance);

        Assert.IsTrue(quests.Load(path), $"\"{path}\" failed to load.");

        return quests;
    }

    private static HashSet<ulong> NpcGuidsSpawnedByFabledRealms()
    {
        var script = File.ReadAllText(FindSourceFile("Scripts", "Zone", "FabledRealms.lua"));

        var guids = Regex.Matches(script, @"spawnNpcWithGuid\(\s*\d+\s*,\s*(\d+)")
            .Select(match => ulong.Parse(match.Groups[1].Value))
            .ToHashSet();

        Assert.IsTrue(guids.Count > 0, "Found no spawnNpcWithGuid calls in FabledRealms.lua.");

        return guids;
    }

    private static IEnumerable<(string Field, ulong Guid)> NpcGuidsNamedBy(QuestDefinition quest)
    {
        yield return ("GiverGuid", quest.GiverGuid);
        yield return ("TargetGuid", quest.TargetGuid);

        for (var i = 0; i < quest.Goals.Count; i++)
            foreach (var guid in quest.Goals[i].AllTalkTargetGuids())
                yield return ($"Goals[{i}]", guid);
    }

    [TestMethod]
    public void ShippedQuests_LoadAndIndexEveryGiver()
    {
        var quests = LoadQuests(FindShippedQuests());

        Assert.IsTrue(quests.Quests.Count > 0);

        foreach (var quest in quests.Quests.Values)
        {
            Assert.IsTrue(quest.Goals.Count > 0, $"Quest {quest.QuestId} has no goals.");

            if (quest.GiverGuid != 0)
                Assert.IsTrue(quests.ByGiver.TryGetValue(quest.GiverGuid, out var ids) && ids.Contains(quest.QuestId),
                    $"Quest {quest.QuestId} is missing from its giver's index.");
        }
    }

    [TestMethod]
    public void ShippedQuests_NameOnlyNpcsThatFabledRealmsSpawns()
    {
        var spawned = NpcGuidsSpawnedByFabledRealms();
        var quests = LoadQuests(FindShippedQuests());

        var unspawned = quests.Quests.Values
            .SelectMany(quest => NpcGuidsNamedBy(quest)
                .Where(named => named.Guid != 0 && !spawned.Contains(named.Guid))
                .Select(named => $"{quest.QuestId} {named.Field} {named.Guid}"))
            .ToList();

        Assert.AreEqual(0, unspawned.Count,
            "Quests name npcs that FabledRealms.lua never spawns, so they can't be finished. "
            + "Move them to Quests.disabled.json: " + string.Join(", ", unspawned));
    }

    [TestMethod]
    public void ShippedQuests_ReferenceOnlyShippedQuests()
    {
        var quests = LoadQuests(FindShippedQuests());

        foreach (var quest in quests.Quests.Values)
        {
            int[] references = [quest.PrerequisiteQuestId, quest.NextQuestId, .. quest.ExcludesQuestIds];

            foreach (var reference in references.Where(id => id != 0))
                Assert.IsTrue(quests.Quests.ContainsKey(reference),
                    $"Quest {quest.QuestId} references quest {reference}, which Quests.json doesn't have.");
        }
    }

    [TestMethod]
    public void DisabledQuests_StayLoadableAndOutOfTheShippedFile()
    {
        var shipped = LoadQuests(FindShippedQuests());
        var disabled = LoadQuests(FindSourceFile("Resources", "Quests.disabled.json"));

        Assert.IsTrue(disabled.Quests.Count > 0);

        foreach (var questId in disabled.Quests.Keys)
            Assert.IsFalse(shipped.Quests.ContainsKey(questId), $"Quest {questId} is in both Quests.json and Quests.disabled.json.");
    }
}
