using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;
using Sanctuary.Game.Jobs;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Game.Quests;
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

    // Quests name NPCs by their Npcs.json id, which each spawn names first: spawnFixedNpc(id, ...) since upstream OSFR's
    // #164 derived fixed NPC GUIDs from it, spawnNpcWithGuid(id, guid, ...) before.
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
        {
            if (quest.Goals[i] is TalkToNpcGoal talkGoal)
                foreach (var npcId in talkGoal.AllTalkTargetNpcIds())
                    yield return ($"Goals[{i}]", npcId);

            if (quest.Goals[i] is PlayActivityGoal playGoal)
                yield return ($"Goals[{i}].NpcId", playGoal.NpcId);
        }
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

    /// <summary>
    /// The first hour's Cobblestone Village chain (Ricky, Samantha, Simone, Jonelle): each quest is given by the NPC the
    /// one before it is handed in to, once that one is done, so a new player never has to look for the next giver. Where
    /// the earlier quest names it as <c>NextQuestId</c>, <see cref="QuestManager.OfferNextQuest"/> offers it straight
    /// after the reward. Simone gives the Cobblestone Village Cafe (9103) as Assistant Needed (9102) is handed in, and
    /// Delivery Derailed (9104) as the Cafe is.
    /// </summary>
    [TestMethod]
    [DataRow(3290, 9101, true)]
    [DataRow(9101, 9102, true)]
    [DataRow(9102, 9103, true)]
    [DataRow(9103, 9104, true)]
    [DataRow(9104, 9105, true)]
    public void ShippedFirstHourChain_GivesEachQuestWhereTheOneBeforeEnds(int previousId, int nextId, bool offeredAtTurnIn)
    {
        var quests = LoadQuests(FindShippedQuests());

        Assert.IsTrue(quests.TryGet(previousId, out var previous), $"Quest {previousId} is missing.");
        Assert.IsTrue(quests.TryGet(nextId, out var next), $"Quest {nextId} is missing.");

        var handedInTo = previous.GoalTargetNpcId(previous.Goals.Count - 1);
        Assert.AreEqual(handedInTo, next.GiverNpcId, $"Quest {nextId} isn't given by npc {handedInTo}, where {previousId} is handed in.");
        Assert.AreEqual(previousId, next.PrerequisiteQuestId, $"Quest {nextId} doesn't wait for {previousId}.");

        if (offeredAtTurnIn)
            Assert.AreEqual(nextId, previous.NextQuestId, $"Quest {previousId} doesn't offer {nextId} at its turn-in.");
        else
            Assert.IsNull(previous.NextQuestId, $"Quest {previousId} offers a quest at its turn-in, which this chain doesn't expect.");

        var playerQuests = new QuestLog();
        Assert.IsFalse(next.IsOfferableFor(playerQuests), $"Quest {nextId} is offered before {previousId} is done.");

        playerQuests.AddCompleted(previousId);
        Assert.IsTrue(next.IsOfferableFor(playerQuests), $"Quest {nextId} isn't offered once {previousId} is done.");
    }

    /// <summary>
    /// A step won by playing a game (<see cref="PlayActivityGoal"/>) is credited only by a win the server reports, from a
    /// match-three game or a Cooking recipe. A step naming any other activity (a fishing hole, a game that isn't built)
    /// could never be finished; the server only warns about it. Every one names the NPC who offers its game, or it would
    /// have no target and no Take Me There.
    /// </summary>
    [TestMethod]
    public void ShippedPlayGoals_NameAGameWhoseWinsTheServerReports()
    {
        var resources = Path.GetDirectoryName(FindShippedQuests())!;

        Assert.IsTrue(MimicDefinitions.TryLoad(Path.Combine(resources, "Mimic.json"), NullLogger.Instance, out var mimic));

        var playGoals = LoadQuests(FindShippedQuests()).Quests.Values
            .SelectMany(quest => quest.Goals.OfType<PlayActivityGoal>().Select(goal => (Quest: quest, Goal: goal)))
            .ToList();

        Assert.IsTrue(playGoals.Count > 0, "The Cobblestone Village Cafe's steak is a play step.");

        var problems = playGoals
            .Where(play => PlayableActivities.WinsOf(play.Goal.ActivityId, mimic) == ActivityWins.NotReported
                || play.Goal.RequiredCount < 1 || play.Goal.NpcId == 0)
            .Select(play => $"Quest {play.Quest.QuestId}, goal {play.Goal.NameId}: activity {play.Goal.ActivityId}, " +
                $"{play.Goal.RequiredCount} wins, offered by NPC {play.Goal.NpcId}")
            .ToList();

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    /// <summary>
    /// A Cooking win is the client's report, which a modified client can make without playing, so a quest with a step on
    /// a game whose wins the client reports gives coins and stars only, as the server warns at startup.
    /// </summary>
    [TestMethod]
    public void ShippedQuestsWonOnTheClientsReport_RewardOnlyCoinsAndStars()
    {
        var resources = Path.GetDirectoryName(FindShippedQuests())!;

        Assert.IsTrue(MimicDefinitions.TryLoad(Path.Combine(resources, "Mimic.json"), NullLogger.Instance, out var mimic));

        var wonOnTheClientsReport = LoadQuests(FindShippedQuests()).Quests.Values
            .Where(quest => quest.Goals.OfType<PlayActivityGoal>()
                .Any(goal => PlayableActivities.WinsOf(goal.ActivityId, mimic) == ActivityWins.ReportedByClient))
            .ToList();

        Assert.IsTrue(wonOnTheClientsReport.Any(quest => quest.QuestId == 9103), "The Cafe's steak is cooked.");

        var problems = wonOnTheClientsReport
            .Where(quest => quest.RewardsBeyondCoinsAndStars().Any())
            .Select(quest => $"Quest {quest.QuestId}: {string.Join("; ", quest.RewardsBeyondCoinsAndStars())}")
            .ToList();

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    /// <summary>
    /// The Cobblestone Village Cafe (9103): Simone's Spiralmint Steak (activity 450, a Cooking recipe the Games menu
    /// lists, as PlayableActivitiesTests checks), won once, then handed to Simone. No world object starts a game yet, so
    /// Simone offers the steak herself, and no row of the quest sends the player to her cooking table.
    /// </summary>
    [TestMethod]
    public void ShippedCafe_IsWonBySimonesSteak_ThenHandedToHer()
    {
        const int simone = 2069;

        Assert.IsTrue(LoadQuests(FindShippedQuests()).TryGet(9103, out var cafe));

        var steak = (PlayActivityGoal)cafe.Goals[0];
        Assert.AreEqual(450, steak.ActivityId);
        Assert.AreEqual(1, steak.RequiredCount);
        Assert.AreEqual(simone, steak.NpcId, "Simone offers the steak.");
        Assert.AreEqual(simone, cafe.GoalTargetNpcId(0), "So the step's marker and Take Me There point at her.");

        Assert.AreEqual(2, cafe.Goals.Count);
        Assert.AreEqual(simone, cafe.GoalTargetNpcId(1), "The steak is handed to Simone.");
        Assert.AreNotEqual(0, cafe.TurnInDialogueId, "Simone says something as it's handed in.");

        // Her offer and the step's rows that say to click (or use) her cooking table, from the live quest and its unreleased
        // first version: they wait for a table that opens the game.
        int[] sendToTheTable = [94278, 94528, 100259, 100265, 21838];
        int[] shown =
        [
            cafe.TitleId, cafe.DescriptionId, cafe.GiverDialogueId, cafe.ObjectiveDescriptionId,
            .. cafe.Goals.SelectMany(goal => new[] { goal.NameId, goal.DescriptionId, goal.DialogueId })
        ];

        CollectionAssert.AreEqual(Array.Empty<int>(), shown.Intersect(sendToTheTable).ToArray());
    }

    /// <summary>
    /// A quest that teaches a job (<see cref="QuestDefinition.TeachesProfileId"/>) names one the server has and a
    /// trainer could give: never a staff job, which only login gives with the account's flags.
    /// </summary>
    [TestMethod]
    public void ShippedQuests_TeachOnlyJobsATrainerCouldGive()
    {
        var profiles = new ProfileDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(profiles.Load(FindSourceFile("Resources", "Profiles.json")));

        var quests = LoadQuests(FindShippedQuests()).Quests.Values.Where(quest => quest.TeachesProfileId != 0).ToList();

        foreach (var quest in quests)
        {
            Assert.IsTrue(profiles.ContainsKey(quest.TeachesProfileId), $"Quest {quest.QuestId} teaches unknown job {quest.TeachesProfileId}.");
            Assert.IsFalse(Enum.IsDefined((SpecialProfileIds)quest.TeachesProfileId), $"Quest {quest.QuestId} teaches a staff job.");
        }

        // Simone says she'll teach you the chef job as Assistant Needed is handed in: the job her trainer menu gives.
        var assistantNeeded = quests.Single(quest => quest.QuestId == 9102);
        CollectionAssert.Contains(JobTrainers.JobsTaughtBy(assistantNeeded.GoalTargetNpcId(0)).ToList(), assistantNeeded.TeachesProfileId);
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

    /// <summary>
    /// Upstream OSFR's #166 credits a Collect goal only from player-specific pickups (<c>CollectGoal.Matches</c>), so a
    /// goal whose node type has no player-specific pool can never be finished. The server only warns about it.
    /// </summary>
    [TestMethod]
    public void ShippedCollectGoals_GatherPlayerSpecificPickups()
    {
        var resources = Path.GetDirectoryName(FindShippedQuests())!;

        var pools = new CollectionNodePoolDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(pools.Load(Path.Combine(resources, "CollectionNodePools.json")));

        var playerSpecific = pools.Values.Where(pool => pool.PlayerSpecific).Select(pool => pool.NodeType).ToHashSet();

        var problems = LoadQuests(FindShippedQuests()).Quests.Values
            .SelectMany(quest => quest.Goals.OfType<CollectGoal>()
                .Where(goal => !playerSpecific.Contains(goal.CollectNodeType))
                .Select(goal => $"Quest {quest.QuestId}, goal {goal.NameId}: \"{goal.CollectNodeType}\" has no player-specific pool"))
            .ToList();

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    /// <summary>
    /// Quest collections (upstream OSFR #166): a quest's <c>RewardCollectionId</c> is a shipped collection, and the entry
    /// a quest pickup ticks belongs to the collection its node type names, in Evergrove's Collections.json.
    /// </summary>
    [TestMethod]
    public void ShippedQuestCollections_Resolve()
    {
        var resources = Path.GetDirectoryName(FindShippedQuests())!;

        var collections = new CollectionDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(collections.Load(Path.Combine(resources, "Collections.json")));

        var types = new CollectionNodeTypeDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(types.Load(Path.Combine(resources, "CollectionNodeTypes.json")));

        var problems = new List<string>();

        foreach (var quest in LoadQuests(FindShippedQuests()).Quests.Values)
        {
            if (quest.RewardCollectionId != 0 && !collections.ContainsKey(quest.RewardCollectionId))
                problems.Add($"Quest {quest.QuestId} rewards unknown collection {quest.RewardCollectionId}");
        }

        foreach (var type in types.Values.Where(type => type.CollectionEntryId != 0))
        {
            if (!collections.TryGetEntry(type.CollectionEntryId, out var match) || match.Definition.Id != type.CollectionId)
                problems.Add($"Node type {type.Key} ticks entry {type.CollectionEntryId}, which isn't in collection {type.CollectionId}");
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
