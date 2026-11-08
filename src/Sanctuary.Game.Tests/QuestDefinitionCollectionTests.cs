using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;

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

    // Quests name NPCs by their Npcs.json id, which each spawn names first (spawnNpcWithGuid(id, guid, ...); upstream
    // OSFR's #164 turns these into spawnFixedNpc(id, ...)).
    private static HashSet<int> NpcIdsSpawnedByFabledRealms()
    {
        var script = File.ReadAllText(FindSourceFile("Scripts", "Zone", "FabledRealms.lua"));

        var ids = Regex.Matches(script, @"spawn(?:NpcWithGuid|FixedNpc)\(\s*(\d+)\s*,")
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToHashSet();

        Assert.IsTrue(ids.Count > 0, "Found no NPC spawns in FabledRealms.lua.");

        return ids;
    }

    private static IEnumerable<(string Field, int NpcId)> NpcIdsNamedBy(QuestDefinition quest)
    {
        yield return ("GiverNpcId", quest.GiverNpcId);
        yield return ("TargetNpcId", quest.TargetNpcId);

        for (var i = 0; i < quest.Goals.Count; i++)
            if (quest.Goals[i] is TalkToNpcGoal talkGoal)
                foreach (var npcId in talkGoal.AllTalkTargetNpcIds())
                    yield return ($"Goals[{i}]", npcId);
    }

    /// <summary>Mistakes in a goal's conversation that would show a blank line or trap the player in it.</summary>
    private static IEnumerable<string> ConversationProblems(QuestGoal goal)
    {
        var talkGoal = goal as TalkToNpcGoal;

        if (talkGoal is { CompleteAfterDialogue: true, IsCounted: true })
            yield return "CompleteAfterDialogue is set on a counted talk goal, where it does nothing";

        if (talkGoal is { CompleteAfterDialogue: true } && goal.Dialogue.Count == 0)
            yield return "CompleteAfterDialogue is set without a Dialogue";

        foreach (var problem in LineProblems(goal.Dialogue))
            yield return problem;
    }

    private static IEnumerable<string> LineProblems(List<QuestDialogueLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.TextId == 0)
                yield return "a Dialogue line has no TextId";

            if (line.Choices.Count == 0)
                continue;

            if (line.Choices.TrueForAll(choice => choice.Retry))
                yield return $"every answer to line {line.TextId} is a Retry, so it can never be passed";

            foreach (var choice in line.Choices)
            {
                if (choice.ResponseTextId == 0)
                    yield return $"an answer to line {line.TextId} has no ResponseTextId";

                foreach (var problem in LineProblems(choice.Then))
                    yield return problem;
            }
        }
    }

    [TestMethod]
    public void ShippedConversations_HaveNoProblems()
    {
        var problems = LoadQuests(FindShippedQuests()).Quests.Values
            .SelectMany(quest => quest.Goals.SelectMany(goal => ConversationProblems(goal)
                .Select(problem => $"Quest {quest.QuestId}, goal {goal.NameId}: {problem}")))
            .ToList();

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    [TestMethod]
    public void ShippedQuests_LoadAndIndexEveryGiver()
    {
        var quests = LoadQuests(FindShippedQuests());

        Assert.IsTrue(quests.Quests.Count > 0);

        foreach (var quest in quests.Quests.Values)
        {
            Assert.IsTrue(quest.Goals.Count > 0, $"Quest {quest.QuestId} has no goals.");

            if (quest.GiverNpcId != 0)
                Assert.IsTrue(quests.ByGiver.TryGetValue(quest.GiverNpcId, out var ids) && ids.Contains(quest.QuestId),
                    $"Quest {quest.QuestId} is missing from its giver's index.");
        }
    }

    [TestMethod]
    public void ShippedQuests_NameOnlyNpcsThatFabledRealmsSpawns()
    {
        var spawned = NpcIdsSpawnedByFabledRealms();
        var quests = LoadQuests(FindShippedQuests());

        var unspawned = quests.Quests.Values
            .SelectMany(quest => NpcIdsNamedBy(quest)
                .Where(named => named.NpcId != 0 && !spawned.Contains(named.NpcId))
                .Select(named => $"{quest.QuestId} {named.Field} {named.NpcId}"))
            .ToList();

        Assert.AreEqual(0, unspawned.Count,
            "Quests name npcs that FabledRealms.lua never spawns, so they can't be finished. "
            + "Move them to Quests.disabled.json: " + string.Join(", ", unspawned));
    }

    [TestMethod]
    public void ShippedQuests_NameOnlyNpcsThatHaveAName()
    {
        // The npc a quest sends the player to shows its name in the interaction pop-up; without one the pop-up is
        // blank, as for Missing Homework's squirrel (4313).
        var npcs = new NpcDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(npcs.Load(FindSourceFile("Resources", "Npcs.json")));

        var unnamed = LoadQuests(FindShippedQuests()).Quests.Values
            .SelectMany(quest => NpcIdsNamedBy(quest)
                .Where(named => npcs.TryGetValue(named.NpcId, out var npc) && npc.NameId == 0 && string.IsNullOrEmpty(npc.Name))
                .Select(named => $"{quest.QuestId} {named.Field} {named.NpcId}"))
            .ToList();

        Assert.AreEqual(0, unnamed.Count, "Quests name npcs with no name in Npcs.json: " + string.Join(", ", unnamed));
    }

    [TestMethod]
    public void ShippedQuests_ReferenceOnlyShippedQuests()
    {
        var quests = LoadQuests(FindShippedQuests());

        foreach (var quest in quests.Quests.Values)
        {
            int[] references = [quest.PrerequisiteQuestId ?? 0, quest.NextQuestId ?? 0, .. quest.ExcludesQuestIds];

            foreach (var reference in references.Where(id => id != 0))
                Assert.IsTrue(quests.Quests.ContainsKey(reference),
                    $"Quest {quest.QuestId} references quest {reference}, which Quests.json doesn't have.");
        }
    }

    [TestMethod]
    public void ShippedCollectGoals_HaveEnoughPickupsOutAtOnce()
    {
        // A Collect goal names a node type; the pickups come from the pools of that type. A goal needing more than
        // its pools put out at once makes the player wait for respawns, and a type nobody spawns can't be finished.
        // Everything from the folder Quests.json is in: other tests leave an empty CollectionNodeSpawns in the
        // test output, which a search of its own would find first.
        var resources = Path.GetDirectoryName(FindShippedQuests())!;

        var types = new CollectionNodeTypeDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(types.Load(Path.Combine(resources, "CollectionNodeTypes.json")));

        var pools = new CollectionNodePoolDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(pools.Load(Path.Combine(resources, "CollectionNodePools.json")));

        var spawns = new CollectionNodeSpawnDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(spawns.Load(Path.Combine(resources, "CollectionNodeSpawns")));

        var problems = new List<string>();

        foreach (var quest in LoadQuests(FindShippedQuests()).Quests.Values)
        {
            foreach (var goal in quest.Goals.OfType<CollectGoal>())
            {
                if (!types.ContainsKey(goal.CollectNodeType))
                {
                    problems.Add($"Quest {quest.QuestId}, goal {goal.NameId}: no node type \"{goal.CollectNodeType}\"");
                    continue;
                }

                var outAtOnce = pools.Values
                    .Where(pool => pool.NodeType == goal.CollectNodeType)
                    .Sum(pool => pool.GetTargetActiveCount(spawns.Values.Count(spawn =>
                        spawn.Pool == pool.Key && spawn.ZoneDefinitionId == pool.ZoneDefinitionId)));

                if (goal.RequiredCount <= 0 || outAtOnce < goal.RequiredCount)
                    problems.Add($"Quest {quest.QuestId}, goal {goal.NameId}: needs {goal.RequiredCount} " +
                        $"\"{goal.CollectNodeType}\", and {outAtOnce} are out at once");
            }
        }

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    [TestMethod]
    public void ShippedFirewood_IsScaledToAStick()
    {
        // The firewood pickup borrows sg_treasure_hunter_plank_01 (model 156), a bridge board whose mesh is 14.3 m
        // long. At full size each stick was as long as a tree.
        var resources = Path.GetDirectoryName(FindShippedQuests())!;

        var types = new CollectionNodeTypeDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(types.Load(Path.Combine(resources, "CollectionNodeTypes.json")));

        var firewood = types["firewood"];
        Assert.AreEqual(156, firewood.ModelId, "The model changed: measure the new one and set Scale to match.");
        Assert.IsTrue(firewood.Scale * 14.3f is > 0.5f and < 1.5f,
            $"A stick of firewood should be about a metre long, and Scale {firewood.Scale} makes it " +
            $"{firewood.Scale * 14.3f:0.0} m.");
    }

    private static string WriteQuestFile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"quests-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string Quest(int id, string extra = "") =>
        $$"""{ "QuestId": {{id}}, "Goals": [ { "Type": 0, "NameId": {{id}}1 } ]{{extra}} }""";

    [TestMethod]
    [DataRow(@", ""NextQuestId"": 99", "NextQuestId")]
    [DataRow(@", ""PrerequisiteQuestId"": 99", "PrerequisiteQuestId")]
    [DataRow(@", ""ExcludesQuestIds"": [ 2, 99 ]", "ExcludesQuestIds")]
    public void LinkToMissingQuest_RefusesTheFile(string link, string field)
    {
        var path = WriteQuestFile($"[ {Quest(1, link)}, {Quest(2)} ]");

        try
        {
            Assert.IsFalse(new QuestDefinitionCollection(NullLogger.Instance).Load(path), $"A {field} naming a missing quest loaded.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void SameQuestTwice_RefusesTheFile()
    {
        var path = WriteQuestFile($"[ {Quest(1)}, {Quest(1)} ]");

        try
        {
            Assert.IsFalse(new QuestDefinitionCollection(NullLogger.Instance).Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Reload_ReplacesTheQuests()
    {
        var path = WriteQuestFile($$"""[ {{Quest(1, @", ""GiverNpcId"": 500, ""NextQuestId"": 2")}}, {{Quest(2)}} ]""");

        try
        {
            var quests = LoadQuests(path);

            File.WriteAllText(path, $$"""[ {{Quest(3, @", ""GiverNpcId"": 600")}} ]""");
            Assert.IsTrue(quests.Load(path));

            CollectionAssert.AreEquivalent(new[] { 3 }, quests.Quests.Keys.ToArray());
            Assert.IsFalse(quests.ByGiver.ContainsKey(500), "The old quest's giver is still indexed.");
            Assert.IsTrue(quests.ByGiver.ContainsKey(600));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void BrokenReload_KeepsTheLoadedQuests()
    {
        var path = WriteQuestFile($$"""[ {{Quest(1, @", ""GiverNpcId"": 500")}} ]""");

        try
        {
            var quests = LoadQuests(path);

            File.WriteAllText(path, $$"""[ {{Quest(3, @", ""NextQuestId"": 99")}} ]""");
            Assert.IsFalse(quests.Load(path));

            CollectionAssert.AreEquivalent(new[] { 1 }, quests.Quests.Keys.ToArray());
            Assert.IsTrue(quests.ByGiver.ContainsKey(500));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void DisabledQuests_StayLoadableAndOutOfTheShippedFile()
    {
        // Disabled quests are parked, and link to shipped ones, so they're checked the way they'd come back: moved
        // into Quests.json, the two files must load as one.
        var shippedJson = JsonNode.Parse(File.ReadAllText(FindShippedQuests()))!.AsArray();
        var disabledJson = JsonNode.Parse(File.ReadAllText(FindSourceFile("Resources", "Quests.disabled.json")))!.AsArray();

        // Upstream OSFR spawns the NPCs of the five quests Evergrove had parked, so the file is empty after the merge.

        var shippedIds = shippedJson.Select(quest => (int)quest!["QuestId"]!).ToHashSet();
        foreach (var quest in disabledJson)
        {
            var questId = (int)quest!["QuestId"]!;
            Assert.IsFalse(shippedIds.Contains(questId), $"Quest {questId} is in both Quests.json and Quests.disabled.json.");
        }

        var combined = new JsonArray([.. shippedJson.Select(quest => quest!.DeepClone()), .. disabledJson.Select(quest => quest!.DeepClone())]);
        var path = WriteQuestFile(combined.ToJsonString());

        try
        {
            Assert.AreEqual(shippedJson.Count + disabledJson.Count, LoadQuests(path).Quests.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
