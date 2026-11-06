using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A quest's stars go to its job (<see cref="QuestDefinition.RewardProfileId"/>) when the character has it, whichever job
/// they wear, and to the job they wear when they haven't: the shipped Cobblestone chef quests pay theirs to Chef.
/// </summary>
[TestClass]
public sealed class QuestRewardJobTests
{
    private QuestWorld _world = null!;

    [TestInitialize]
    public void Setup() => _world = new QuestWorld();

    [TestCleanup]
    public void Cleanup() => _world.Dispose();

    /// <summary>The shipped quest, taken after the one before it, with every step done: waiting to be handed in.</summary>
    private QuestDefinition ReadyToHandIn(int questId)
    {
        var quest = QuestWorld.Shipped(questId);

        if (quest.PrerequisiteQuestId is { } prerequisite)
            _world.Player.Quests.AddCompleted(prerequisite);

        var progress = _world.Accept(quest);

        for (var goalIndex = 0; goalIndex < quest.Goals.Count; goalIndex++)
            progress.FinishGoal(quest, goalIndex);

        return quest;
    }

    private (int Level, int Stars) Fresh(int stars) => _world.Resources.RankLevels.Accrue(1, 0, stars);

    [TestMethod]
    public void AChefWearingAdventurer_GetsTheCafesStarsOnChef()
    {
        _world.Give(QuestWorld.Chef);
        var cafe = ReadyToHandIn(9103);

        Assert.IsTrue(_world.Quests.CompleteQuest(_world.Player, 9103));

        Assert.AreEqual(Fresh(cafe.RewardExperience), _world.SavedJob(QuestWorld.Chef));
        Assert.AreEqual((1, 0), _world.SavedJob(QuestWorld.Adventurer), "Worn, but not the quest's job.");
        Assert.AreEqual(QuestWorld.Adventurer, _world.Player.ActiveProfileId, "Still wearing it.");
    }

    [TestMethod]
    public void APlayerWithoutChef_GetsTheCafesStarsOnTheJobTheyWear()
    {
        var cafe = ReadyToHandIn(9103);

        Assert.IsTrue(_world.Quests.CompleteQuest(_world.Player, 9103));

        Assert.AreEqual(Fresh(cafe.RewardExperience), _world.SavedJob(QuestWorld.Adventurer));
        Assert.IsNull(_world.SavedJob(QuestWorld.Chef), "The Cafe teaches nothing.");
    }

    [TestMethod]
    public void AssistantNeeded_StillPutsItsStarsOnChef()
    {
        var assistantNeeded = ReadyToHandIn(9102);

        Assert.IsTrue(_world.Quests.CompleteQuest(_world.Player, 9102));

        Assert.AreEqual(Fresh(assistantNeeded.RewardExperience), _world.SavedJob(QuestWorld.Chef), "Taught, then paid.");
        Assert.AreEqual((1, 0), _world.SavedJob(QuestWorld.Adventurer));
    }
}
