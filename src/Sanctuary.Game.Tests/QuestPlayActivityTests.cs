using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A quest step won by playing a game (<see cref="PlayActivityGoal"/>), from the win the server's own game decides to
/// the step ticking and being saved. MimicHandler and FlashMinigameHandler raise <see cref="ActivityWonEvent"/> where
/// they pay a win; the quest manager does the rest.
/// </summary>
[TestClass]
public sealed class QuestPlayActivityTests
{
    private const int Steak = 450;  // Cooking: Simone's Spiralmint Steak
    private const int Sundae = 460; // Cooking: Gummyfruit Topped Sundae
    private const int Simone = 2069;

    private const int CookStep = 94528;
    private const int HandInStep = 94536;

    private QuestWorld _world = null!;

    [TestInitialize]
    public void Setup() => _world = new QuestWorld();

    [TestCleanup]
    public void Cleanup() => _world.Dispose();

    /// <summary>The Cobblestone Village Cafe's two steps: win the steak, then take it to Simone.</summary>
    private static QuestDefinition Cafe(int questId = 9103, int wins = 1) => new()
    {
        QuestId = questId,
        GiverNpcId = Simone,
        TargetNpcId = Simone,
        Goals =
        [
            new PlayActivityGoal { NameId = CookStep, ActivityId = Steak, RequiredCount = wins },
            new TalkToNpcGoal { NameId = HandInStep, Targets = [new TalkTarget { NpcId = Simone }] }
        ]
    };

    private void Win(int activityId) => _world.Quests.OnEvent(_world.Player, new ActivityWonEvent(activityId));

    [TestMethod]
    public void AWin_TicksTheStep_AndTheNextOneIsSaved()
    {
        var progress = _world.Accept(Cafe());

        Win(Steak);

        Assert.AreEqual(1, progress.GoalIndex, "On to taking the steak to Simone.");
        Assert.AreEqual(HandInStep, _world.ReadCharacter().Quests.Single().CurrentGoalId, "A relog finds the player on that step.");
    }

    [TestMethod]
    public void AnotherGamesWin_CountsForNothing()
    {
        var progress = _world.Accept(Cafe());

        Win(Sundae);

        Assert.AreEqual(0, progress.GoalIndex);
        Assert.AreEqual(0, progress.ProgressFor(0).Count);
        Assert.AreEqual(CookStep, _world.ReadCharacter().Quests.Single().CurrentGoalId);
    }

    [TestMethod]
    public void EachWin_CountsOnce_AndIsSavedAsItCounts()
    {
        var progress = _world.Accept(Cafe(wins: 2));

        Win(Steak);

        Assert.AreEqual(0, progress.GoalIndex, "One steak of two.");
        Assert.AreEqual(1, progress.ProgressFor(0).Count);

        var saved = _world.ReadCharacter().Quests.Single().Progress.Single();
        Assert.AreEqual((CookStep, 0UL, 1), (saved.GoalId, saved.TargetId, saved.Count), "A relog keeps the first steak.");

        Win(Steak);

        Assert.AreEqual(1, progress.GoalIndex);
    }

    [TestMethod]
    public void AWinBeforeTheStepIsOpen_IsNotKeptForIt()
    {
        // Talk to Simone first, then cook: a steak cooked before the talk doesn't count once the cooking step opens.
        var quest = new QuestDefinition
        {
            QuestId = 1,
            TargetNpcId = Simone,
            Goals =
            [
                new TalkToNpcGoal { NameId = 140539, Targets = [new TalkTarget { NpcId = Simone }] },
                new PlayActivityGoal { NameId = CookStep, ActivityId = Steak },
                new TalkToNpcGoal { NameId = HandInStep, Targets = [new TalkTarget { NpcId = Simone }] }
            ]
        };

        var progress = _world.Accept(quest);

        Win(Steak);

        Assert.AreEqual(0, progress.GoalIndex);

        _world.Quests.OnEvent(_world.Player, new NpcTalkedEvent(new Npc(null!) { DefinitionId = Simone }));

        Assert.AreEqual(1, progress.GoalIndex, "Talked to Simone.");
        Assert.AreEqual(0, progress.ProgressFor(1).Count, "The steak cooked before is not counted.");

        Win(Steak);

        Assert.AreEqual(2, progress.GoalIndex);
    }

    [TestMethod]
    public void OneWin_CountsForOneQuest_NotTwo()
    {
        var first = _world.Accept(Cafe(questId: 1));
        var second = _world.Accept(Cafe(questId: 2));

        Win(Steak);

        Assert.AreEqual(1, first.GoalIndex + second.GoalIndex, "One steak ticks one of the two steps.");

        Win(Steak);

        Assert.AreEqual(1, first.GoalIndex);
        Assert.AreEqual(1, second.GoalIndex);
    }

    [TestMethod]
    public void TheCookingStep_HasNoMarker_AndNothingForTakeMeThere()
    {
        _world.Accept(Cafe());

        Assert.IsFalse(_world.Quests.TryGetActiveObjectiveTarget(_world.Player, out _),
            "No world object starts a game yet, so there is nowhere to walk to.");
    }
}
