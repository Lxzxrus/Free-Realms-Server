using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The Adventurer's Journal, which picks the quest the welcome window's "Hero's Journal Quest" box shows
/// (docs/research/heros-journal.md).
/// </summary>
[TestClass]
public sealed class AdventurersJournalTests
{
    private const int IntroduceYourself = 2563;
    private const int CallTheCrew = 1801;
    private const int IntroduceYourselfElsewhere = 2564;

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

    private static QuestDefinitionCollection LoadShippedQuests()
    {
        var quests = new QuestDefinitionCollection(NullLogger.Instance);

        Assert.IsTrue(quests.Load(FindSourceFile("Resources", "Quests.json")), "Quests.json failed to load.");

        return quests;
    }

    [TestMethod]
    public void CobblestoneVillage_HoldsOnlyEvergrovesStory()
    {
        var journal = WorldZone.CreateAdventurersJournalInfo();

        CollectionAssert.AreEqual(new[] { AdventurersJournal.CobblestoneVillageHubId },
            journal.Hubs.Values.Where(hub => hub.RegionId == 1).Select(hub => hub.Id).ToArray(),
            "The client makes a region's first unfinished hub current, so no other hub may come before the story's.");

        CollectionAssert.AreEqual(new[] { IntroduceYourself, CallTheCrew },
            journal.HubQuests.Values
                .Where(quest => quest.HubId == AdventurersJournal.CobblestoneVillageHubId)
                .OrderBy(quest => quest.Unknown)
                .Select(quest => quest.Id)
                .ToArray());
    }

    [TestMethod]
    public void HubQuests_AreNumberedInStoryOrderFromOne()
    {
        CollectionAssert.AreEqual(new[] { 1, 2 },
            AdventurersJournal.HubQuests().Select(quest => quest.Unknown).ToArray());
    }

    [TestMethod]
    public void EveryHubQuest_IsInAHubTheJournalSends()
    {
        var journal = WorldZone.CreateAdventurersJournalInfo();

        foreach (var quest in journal.HubQuests.Values)
            Assert.IsTrue(journal.Hubs.ContainsKey(quest.HubId), $"Quest {quest.Id} is in hub {quest.HubId}, which isn't sent.");
    }

    [TestMethod]
    public void Stories_OnlyListQuestsTheServerHas()
    {
        var quests = LoadShippedQuests();

        foreach (var questId in AdventurersJournal.Stories.Values.SelectMany(story => story))
            Assert.IsTrue(quests.TryGet(questId, out _), $"Story quest {questId} isn't in Quests.json.");
    }

    [TestMethod]
    public void Stories_GiveEachQuestWhereTheOneBeforeIsHandedIn()
    {
        // The box only has a quest's text once the quest is in the player's log. A story quest given by another NPC
        // would leave the player between two story quests, with an empty box, for the whole walk there.
        var quests = LoadShippedQuests();

        foreach (var story in AdventurersJournal.Stories.Values)
        {
            for (var index = 1; index < story.Count; index++)
            {
                Assert.IsTrue(quests.TryGet(story[index - 1], out var previous));
                Assert.IsTrue(quests.TryGet(story[index], out var next));

                Assert.AreEqual(previous.QuestId, next.PrerequisiteQuestId,
                    $"Quest {next.QuestId} should need {previous.QuestId}, the story quest before it.");
                Assert.AreEqual(previous.GoalTargetGuid(previous.Goals.Count - 1), next.GiverGuid,
                    $"Quest {next.QuestId} should be given where {previous.QuestId} is handed in.");
            }
        }
    }

    [TestMethod]
    public void IsStoryQuest_KnowsTheStoryQuests()
    {
        Assert.IsTrue(AdventurersJournal.IsStoryQuest(IntroduceYourself));
        Assert.IsTrue(AdventurersJournal.IsStoryQuest(CallTheCrew));
        Assert.IsFalse(AdventurersJournal.IsStoryQuest(IntroduceYourselfElsewhere));
    }

    [TestMethod]
    public void IsBetweenStoryQuests_OnlyAfterHandingOneInAndBeforeTakingTheNext()
    {
        static bool Between(Dictionary<int, bool> quests) => AdventurersJournal.IsBetweenStoryQuests(quests);

        Assert.IsFalse(Between(new()), "A new player hasn't started the story.");
        Assert.IsFalse(Between(new() { [IntroduceYourself] = false }), "The first quest is in the log.");
        Assert.IsTrue(Between(new() { [IntroduceYourself] = true }), "Handed in, and Call the Crew! not taken yet.");
        Assert.IsFalse(Between(new() { [IntroduceYourself] = true, [CallTheCrew] = false }), "The next one is in the log.");
        Assert.IsFalse(Between(new() { [IntroduceYourself] = true, [CallTheCrew] = true }), "The story is finished.");
        Assert.IsFalse(Between(new() { [IntroduceYourselfElsewhere] = true }),
            "The other town's Introduce Yourself rules this story out; its quests are never taken.");
    }
}
