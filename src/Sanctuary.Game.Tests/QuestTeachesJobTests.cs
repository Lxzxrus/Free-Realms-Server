using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A quest that teaches a job (<see cref="QuestDefinition.TeachesProfileId"/>), as Simone's Chef: Assistant Needed
/// does: the turn-in gives the job through the job manager a trainer uses, before the rewards, so the quest's stars go
/// to the new job (<see cref="QuestDefinition.RewardProfileId"/>), and only to a player who hasn't got it yet.
/// </summary>
[TestClass]
public sealed class QuestTeachesJobTests
{
    private const int Simone = 2069;
    private const int Coins = 15;
    private const int Stars = 40;

    private QuestWorld _world = null!;

    [TestInitialize]
    public void Setup() => _world = new QuestWorld();

    [TestCleanup]
    public void Cleanup() => _world.Dispose();

    /// <summary>
    /// Find Simone, who teaches you the chef job: Assistant Needed's one step, done, waiting to be handed in. Its stars go
    /// to the job it teaches, as the shipped quest's do.
    /// </summary>
    private QuestDefinition AssistantNeeded(int teaches = QuestWorld.Chef)
    {
        var quest = new QuestDefinition
        {
            QuestId = 9102,
            TargetNpcId = Simone,
            RewardCoins = Coins,
            RewardExperience = Stars,
            RewardProfileId = teaches,
            TeachesProfileId = teaches,
            Goals = [new TalkToNpcGoal { NameId = 140539, Targets = [new TalkTarget { NpcId = Simone }] }]
        };

        _world.Accept(quest).FinishGoal(quest, 0);

        return quest;
    }

    private (int Level, int Stars)? SavedJob(int profileId) => _world.SavedJob(profileId);

    [TestMethod]
    public void TheTurnIn_TeachesTheJob_AndItsStarsGoToIt()
    {
        AssistantNeeded();

        Assert.IsTrue(_world.Quests.CompleteQuest(_world.Player, 9102));

        Assert.AreEqual(QuestWorld.Chef, _world.Player.ActiveProfileId, "Switched into the Chef job, as a trainer switches you.");
        Assert.IsTrue(_world.Player.Profiles.Any(profile => profile.Id == QuestWorld.Chef));

        Assert.AreEqual((1, Stars), SavedJob(QuestWorld.Chef), "Saved, with the quest's stars: the job came before the rewards.");
        Assert.AreEqual((1, 0), SavedJob(QuestWorld.Adventurer), "The Adventurer gets none of them.");
        Assert.AreEqual(Coins, _world.ReadCharacter().Coins);
    }

    [TestMethod]
    public void APlayerWhoHasTheJob_KeepsTheJobTheyHaveOn_AndGetsTheStarsOnIt()
    {
        _world.Give(QuestWorld.Chef);
        AssistantNeeded();

        Assert.IsTrue(_world.Quests.CompleteQuest(_world.Player, 9102));

        Assert.AreEqual(QuestWorld.Adventurer, _world.Player.ActiveProfileId, "Not switched: nothing was taught.");
        Assert.AreEqual(1, _world.Player.Profiles.Count(profile => profile.Id == QuestWorld.Chef));
        Assert.AreEqual((1, Stars), SavedJob(QuestWorld.Chef), "A Chef quest's stars go to Chef, whichever job is worn.");
        Assert.AreEqual((1, 0), SavedJob(QuestWorld.Adventurer));
    }

    [TestMethod]
    public void AJobTheServerDoesntHave_IsNotTaught_AndTheRewardsArePaidAllTheSame()
    {
        const int unknownJob = 9999;

        AssistantNeeded(teaches: unknownJob);

        Assert.IsTrue(_world.Quests.CompleteQuest(_world.Player, 9102));

        Assert.AreEqual(QuestWorld.Adventurer, _world.Player.ActiveProfileId);
        Assert.IsNull(SavedJob(unknownJob));
        Assert.AreEqual((1, Stars), SavedJob(QuestWorld.Adventurer));
        Assert.AreEqual(Coins, _world.ReadCharacter().Coins);
    }

    [TestMethod]
    public void AQuestThatTeachesNothing_LeavesTheJobsAlone()
    {
        AssistantNeeded(teaches: 0);

        Assert.IsTrue(_world.Quests.CompleteQuest(_world.Player, 9102));

        Assert.AreEqual(QuestWorld.Adventurer, _world.Player.ActiveProfileId);
        Assert.AreEqual(1, _world.ReadCharacter().Profiles.Count);
    }

    [TestMethod]
    public void AQuestNotReadyToHandIn_TeachesNothing()
    {
        var quest = new QuestDefinition
        {
            QuestId = 9102,
            TargetNpcId = Simone,
            TeachesProfileId = QuestWorld.Chef,
            Goals = [new TalkToNpcGoal { NameId = 140539, Targets = [new TalkTarget { NpcId = Simone }] }]
        };

        _world.Accept(quest);

        Assert.IsFalse(_world.Quests.CompleteQuest(_world.Player, 9102), "Simone hasn't been found yet.");
        Assert.IsNull(SavedJob(QuestWorld.Chef));
        Assert.AreEqual(QuestWorld.Adventurer, _world.Player.ActiveProfileId);
    }
}
