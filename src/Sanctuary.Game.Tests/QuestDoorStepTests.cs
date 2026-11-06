using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A talk step at an NPC the player goes through rather than talks to, such as a Housing door
/// (<see cref="IQuestManager.AdvanceTalkStepsAt"/>, just before the move into the house): completed without a line, and
/// never in a quest's last step, which would hand the quest in as the player leaves the world.
/// </summary>
[TestClass]
public sealed class QuestDoorStepTests
{
    private const int Door = 90101;
    private const int Samantha = 2045;
    private const int Ricky = 2049;

    private const int DoorStep = 437426;
    private const int BackToSamantha = 94363;
    private const int DoorLine = 78487;

    private QuestWorld _world = null!;

    [TestInitialize]
    public void Setup() => _world = new QuestWorld();

    [TestCleanup]
    public void Cleanup() => _world.Dispose();

    private static TalkToNpcGoal Talk(int nameId, int npcId, int line = 0) =>
        new() { NameId = nameId, DialogueId = line, Targets = [new TalkTarget { NpcId = npcId }] };

    /// <summary>Samantha sends you through the door and back to her, as Housing (9100) does.</summary>
    private static QuestDefinition ThroughTheDoor(int questId = 1, int doorLine = 0) => new()
    {
        QuestId = questId,
        GiverNpcId = Samantha,
        TargetNpcId = Samantha,
        Goals = [Talk(DoorStep, Door, doorLine), Talk(BackToSamantha, Samantha)]
    };

    private static Npc NpcOf(int npcId) =>
        new(null!) { DefinitionId = npcId, Guid = GuidHelper.GetNpcGuid((ulong)npcId, GuidHelper.OpenWorldInstanceId) };

    private void GoThrough(int npcId) => _world.Quests.AdvanceTalkStepsAt(_world.Player, NpcOf(npcId));

    private int Warnings => _world.Logged.Entries.Count(entry => entry.Level == LogLevel.Warning);

    [TestMethod]
    public void TheStepAtTheDoor_IsCompleted_AndTheNextOneIsSaved()
    {
        var progress = _world.Accept(ThroughTheDoor());

        GoThrough(Door);

        Assert.AreEqual(1, progress.GoalIndex, "On to going back to Samantha.");
        Assert.AreEqual(BackToSamantha, _world.ReadCharacter().Quests.Single().CurrentGoalId, "A relog finds the player on that step.");
        Assert.IsTrue(_world.Player.Quests.TryGetInProgress(1, out _), "Not handed in.");
        Assert.AreEqual(0, _world.Player.Quests.PendingTurnInQuestId);
        Assert.AreEqual(0, Warnings);
    }

    [TestMethod]
    public void AnotherNpc_OrAStepNotOpenYet_IsLeftAlone()
    {
        var progress = _world.Accept(ThroughTheDoor());

        GoThrough(Samantha);
        GoThrough(Ricky);

        Assert.AreEqual(0, progress.GoalIndex, "Samantha's step isn't open until the door's is done.");
        Assert.AreEqual(DoorStep, _world.ReadCharacter().Quests.Single().CurrentGoalId);
    }

    [TestMethod]
    public void APlayerWithNoStepThere_IsLeftAlone()
    {
        _world.Add(ThroughTheDoor());

        GoThrough(Door);

        Assert.AreEqual(0, _world.Player.Quests.All.Count, "Nothing is taken or ticked by going through.");
    }

    [TestMethod]
    public void EveryQuestWaitingAtTheDoor_MovesOn()
    {
        var first = _world.Accept(ThroughTheDoor(questId: 1));
        var second = _world.Accept(ThroughTheDoor(questId: 2));

        GoThrough(Door);

        Assert.AreEqual(1, first.GoalIndex);
        Assert.AreEqual(1, second.GoalIndex);
    }

    /// <summary>
    /// Nobody stands at a door to say a line, and a conversation would be cut off by the zone change, so none plays even
    /// if the step has one. The same step at an NPC the player talks to plays it from its button, so the test would see one.
    /// </summary>
    [TestMethod]
    public void NoLinePlays()
    {
        _world.Accept(ThroughTheDoor(doorLine: DoorLine));

        GoThrough(Door);

        Assert.IsNull(_world.Player.QuestConversation.Current);
        Assert.AreEqual(0UL, _world.Player.QuestConversation.TalkingNpcGuid);

        _world.Accept(new QuestDefinition
        {
            QuestId = 2,
            GiverNpcId = Samantha,
            TargetNpcId = Samantha,
            Goals = [Talk(DoorStep, Ricky, DoorLine), Talk(BackToSamantha, Samantha)]
        });

        _world.Quests.GetInteractionOptions(_world.Player, NpcOf(Ricky)).Single().Invoke(_world.Player);

        Assert.AreEqual(DoorLine, _world.Player.QuestConversation.Current?.TextId, "Clicked, the step's line plays.");
    }

    /// <summary>
    /// A door offers no quest buttons, whatever the quest data says: a button for its step would complete the step without
    /// going in. The Gateway hands a door's click to the door before the quest buttons are asked for, and this holds
    /// whichever way a click arrives, even for a quest the door would give, as no shipped quest may.
    /// </summary>
    [TestMethod]
    public void ADoor_OffersNoQuestButtons()
    {
        var progress = _world.Accept(ThroughTheDoor());

        var givenAtTheDoor = _world.Add(new QuestDefinition
        {
            QuestId = 2,
            GiverNpcId = Door,
            TargetNpcId = Samantha,
            Goals = [Talk(BackToSamantha, Samantha)]
        });
        _world.Resources.Quests.ByGiver.GetOrAdd(Door, _ => []).Add(givenAtTheDoor.QuestId);

        Assert.AreEqual(0, _world.Quests.GetInteractionOptions(_world.Player, NpcOf(Door)).Count, "No step's button, and no offer.");
        Assert.AreEqual(0, progress.GoalIndex, "The step waits for the player to go in.");

        // Samantha, given the same quest to offer, offers it.
        _world.Resources.Quests.ByGiver.GetOrAdd(Samantha, _ => []).Add(givenAtTheDoor.QuestId);
        Assert.AreEqual(1, _world.Quests.GetInteractionOptions(_world.Player, NpcOf(Samantha)).Count);
    }

    /// <summary>
    /// A quest's last step is never completed at a door: whichever of its goals is done last hands the quest in, and the
    /// reward window would open as the player leaves the world. It's left open, and logged.
    /// </summary>
    [TestMethod]
    [DataRow(false, DisplayName = "the door is the last step")]
    [DataRow(true, DisplayName = "the door is beside the last talk (WithPrevious)")]
    public void AQuestsLastStep_IsNeverCompleted_SoNothingIsHandedIn(bool besideTheLastTalk)
    {
        var doorStep = Talk(DoorStep, Door);
        doorStep.WithPrevious = besideTheLastTalk;

        var progress = _world.Accept(new QuestDefinition
        {
            QuestId = 1,
            GiverNpcId = Samantha,
            TargetNpcId = Door,
            Goals = besideTheLastTalk ? [Talk(BackToSamantha, Samantha), doorStep] : [doorStep]
        });

        GoThrough(Door);

        Assert.IsFalse(progress.IsGoalDone(besideTheLastTalk ? 1 : 0), "The door's goal is still open.");
        Assert.IsTrue(_world.Player.Quests.TryGetInProgress(1, out _));
        Assert.AreEqual(0, _world.Player.Quests.PendingTurnInQuestId, "No reward window.");
        Assert.AreEqual(1, Warnings);
    }

    /// <summary>A door among the NPCs of a counted talk counts once, like any of them, and is saved as it counts.</summary>
    [TestMethod]
    public void ACountedStep_CountsTheDoorOnce()
    {
        var progress = _world.Accept(new QuestDefinition
        {
            QuestId = 1,
            GiverNpcId = Samantha,
            TargetNpcId = Samantha,
            Goals =
            [
                new TalkToNpcGoal { NameId = DoorStep, RequiredCount = 2, Targets = [new TalkTarget { NpcId = Door }, new TalkTarget { NpcId = Ricky }] },
                Talk(BackToSamantha, Samantha)
            ]
        });

        GoThrough(Door);
        GoThrough(Door);

        Assert.AreEqual(0, progress.GoalIndex);
        Assert.AreEqual(1, progress.ProgressFor(0).Count, "The door counts once.");

        var saved = _world.ReadCharacter().Quests.Single().Progress.Single();
        Assert.AreEqual((DoorStep, (ulong)Door, 1), (saved.GoalId, saved.TargetId, saved.Count), "A relog keeps the door.");
    }
}
