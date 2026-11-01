using System.Linq;
using System.Numerics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class QuestGoalTests
{
    private static readonly QuestDefinition Quest = new() { QuestId = 1, TargetNpcId = 500 };

    [TestMethod]
    public void TalkGoal_CompletesAtItsTargetAndIgnoresOtherNpcs()
    {
        var goal = new TalkToNpcGoal { Targets = [new TalkTarget { NpcId = 600 }] };
        var progress = goal.CreateProgress();

        Assert.AreEqual(QuestGoalResult.None, goal.Progress(Quest, Talk(601), progress));
        Assert.AreEqual(QuestGoalResult.Completed, goal.Progress(Quest, Talk(600), progress));
    }

    [TestMethod]
    public void TalkGoal_WithoutTargetsFallsBackToTheQuestTarget()
    {
        var goal = new TalkToNpcGoal();

        Assert.AreEqual(QuestGoalResult.Completed, goal.Progress(Quest, Talk(500), goal.CreateProgress()));
    }

    [TestMethod]
    public void CountedTalkGoal_CountsEachNpcOnceUntilEnoughAreTalkedTo()
    {
        var goal = new TalkToNpcGoal
        {
            RequiredCount = 2,
            Targets = [new TalkTarget { NpcId = 600 }, new TalkTarget { NpcId = 601 }, new TalkTarget { NpcId = 602 }]
        };
        var progress = goal.CreateProgress();

        Assert.AreEqual(QuestGoalResult.Progressed, goal.Progress(Quest, Talk(600), progress));
        Assert.AreEqual(QuestGoalResult.None, goal.Progress(Quest, Talk(600), progress));
        Assert.AreEqual(1, progress.Count);
        Assert.AreEqual(QuestGoalResult.Completed, goal.Progress(Quest, Talk(602), progress));
    }

    [TestMethod]
    public void CountedTalkGoals_KeepSeparateProgressPerQuest()
    {
        var goal = new TalkToNpcGoal
        {
            RequiredCount = 2,
            Targets = [new TalkTarget { NpcId = 600 }, new TalkTarget { NpcId = 601 }]
        };
        var firstQuest = goal.CreateProgress();
        var secondQuest = goal.CreateProgress();

        goal.Progress(Quest, Talk(600), firstQuest);

        Assert.AreEqual(QuestGoalResult.Progressed, goal.Progress(Quest, Talk(600), secondQuest));
        Assert.AreEqual(1, firstQuest.Count);
    }

    [TestMethod]
    public void CollectGoal_CountsItsNodeTypeUntilTheRequiredCount()
    {
        var goal = new CollectGoal { CollectNodeType = "tin-ore", RequiredCount = 2 };
        var progress = goal.CreateProgress();

        Assert.AreEqual(QuestGoalResult.None, goal.Progress(Quest, Gather("iron-ore"), progress));
        Assert.AreEqual(QuestGoalResult.Progressed, goal.Progress(Quest, Gather("tin-ore"), progress));
        Assert.AreEqual(QuestGoalResult.Completed, goal.Progress(Quest, Gather("tin-ore"), progress));
    }

    [TestMethod]
    public void CollectGoal_IgnoresSharedNodes()
    {
        var goal = new CollectGoal { CollectNodeType = "tin-ore", RequiredCount = 1 };
        var sharedNode = new CollectionNode(null!,
            new CollectionNodeTypeDefinition { Key = "tin-ore", Name = "tin-ore" },
            new CollectionNodePoolDefinition { Key = "tin-ore", NodeType = "tin-ore" },
            new CollectionNodeSpawnDefinition { Id = 1 });

        Assert.IsFalse(goal.CanGather(sharedNode, goal.CreateProgress()));
        Assert.AreEqual(QuestGoalResult.None, goal.Progress(Quest, new NodeGatheredEvent(sharedNode), goal.CreateProgress()));
    }

    [TestMethod]
    public void CollectGoal_CountsEachPlayerSpecificNodeOnce()
    {
        var goal = new CollectGoal { CollectNodeType = "tin-ore", RequiredCount = 2 };
        var progress = goal.CreateProgress();

        Assert.IsTrue(goal.CanGather(PlayerNode("tin-ore", 1), progress));
        Assert.AreEqual(QuestGoalResult.Progressed, goal.Progress(Quest, GatherPlayerNode("tin-ore", 1), progress));
        Assert.IsFalse(goal.CanGather(PlayerNode("tin-ore", 1), progress));
        Assert.AreEqual(QuestGoalResult.None, goal.Progress(Quest, GatherPlayerNode("tin-ore", 1), progress));
        Assert.AreEqual(QuestGoalResult.Completed, goal.Progress(Quest, GatherPlayerNode("tin-ore", 2), progress));
    }

    [TestMethod]
    public void CollectGoal_SavesWhichPlayerSpecificNodesWereGathered()
    {
        var goal = new CollectGoal { CollectNodeType = "tin-ore", RequiredCount = 3 };
        var progress = goal.CreateProgress();
        goal.Progress(Quest, GatherPlayerNode("tin-ore", 7), progress);
        goal.Progress(Quest, GatherPlayerNode("tin-ore", 9), progress);

        var loaded = goal.CreateProgress();
        foreach (var (targetId, count) in progress.Save())
            loaded.Load(targetId, count);

        Assert.AreEqual(2, loaded.Count);
        Assert.IsFalse(goal.CanGather(PlayerNode("tin-ore", 7), loaded));
        Assert.IsTrue(goal.CanGather(PlayerNode("tin-ore", 8), loaded));
    }

    /// <summary>
    /// Evergrove: pickups saved before spawn ids were kept (stage 2, and the rows ReconcileUpstreamQuests carried over)
    /// load as a count with no spawn id. A pickup after the update keeps them at the next save and load.
    /// </summary>
    [TestMethod]
    public void CollectGoal_KeepsPickupsSavedWithoutSpawnIds()
    {
        var goal = new CollectGoal { CollectNodeType = "hastis-homework", RequiredCount = 8 };
        var progress = goal.CreateProgress();
        progress.Load(0, 3);

        Assert.AreEqual(QuestGoalResult.Progressed, goal.Progress(Quest, GatherPlayerNode("hastis-homework", 11), progress));
        Assert.AreEqual(4, progress.Count);

        var loaded = goal.CreateProgress();
        foreach (var (targetId, count) in progress.Save())
            loaded.Load(targetId, count);

        Assert.AreEqual(4, loaded.Count, "The three pages found before the update are still counted.");
        Assert.IsFalse(goal.CanGather(PlayerNode("hastis-homework", 11), loaded));
        CollectionAssert.AreEquivalent(new[] { (0UL, 3), (11UL, 1) }, progress.Save().ToArray());
    }

    [TestMethod]
    public void ReachGoal_CompletesOnlyInsideItsRadius()
    {
        var goal = new ReachLocationGoal { ReachPosition = new Vector3(100f, 0f, 100f), ReachRadius = 10f };
        var progress = goal.CreateProgress();

        Assert.AreEqual(QuestGoalResult.None, goal.Progress(Quest, Move(120f, 100f), progress));
        Assert.AreEqual(QuestGoalResult.Completed, goal.Progress(Quest, Move(105f, 100f), progress));
    }

    [TestMethod]
    public void ReachGoal_IgnoresTheSameSpotInAnotherZone()
    {
        var goal = new ReachLocationGoal { ReachPosition = new Vector3(100f, 0f, 100f), ReachRadius = 10f };
        var progress = goal.CreateProgress();

        Assert.AreEqual(QuestGoalResult.None, goal.Progress(Quest, Move(105f, 100f, zoneDefinitionId: 2), progress));
    }

    [TestMethod]
    public void GoalsIgnoreEventsMeantForOtherGoalTypes()
    {
        var talk = new TalkToNpcGoal { Targets = [new TalkTarget { NpcId = 600 }] };
        var collect = new CollectGoal { CollectNodeType = "tin-ore", RequiredCount = 1 };
        var reach = new ReachLocationGoal { ReachPosition = new Vector3(1f, 0f, 1f) };
        var play = new PlayActivityGoal { ActivityId = 450 };

        Assert.AreEqual(QuestGoalResult.None, talk.Progress(Quest, Gather("tin-ore"), talk.CreateProgress()));
        Assert.AreEqual(QuestGoalResult.None, collect.Progress(Quest, Talk(600), collect.CreateProgress()));
        Assert.AreEqual(QuestGoalResult.None, reach.Progress(Quest, Talk(500), reach.CreateProgress()));

        foreach (var goal in new QuestGoal[] { talk, collect, reach })
            Assert.AreEqual(QuestGoalResult.None, goal.Progress(Quest, Win(450), goal.CreateProgress()), goal.GetType().Name);

        Assert.AreEqual(QuestGoalResult.None, play.Progress(Quest, Talk(600), play.CreateProgress()));
        Assert.AreEqual(QuestGoalResult.None, play.Progress(Quest, Gather("tin-ore"), play.CreateProgress()));
        Assert.AreEqual(QuestGoalResult.None, play.Progress(Quest, Move(1f, 1f), play.CreateProgress()));
    }

    [TestMethod]
    public void PlayGoal_NeedsOneWinOfItsGameByDefault()
    {
        var goal = new PlayActivityGoal { ActivityId = 450 };
        var progress = goal.CreateProgress();

        Assert.AreEqual(1, goal.RequiredCount);
        Assert.AreEqual(QuestGoalResult.None, goal.Progress(Quest, Win(460), progress), "Another recipe's win.");
        Assert.AreEqual(0, progress.Count);
        Assert.AreEqual(QuestGoalResult.Completed, goal.Progress(Quest, Win(450), progress));
    }

    [TestMethod]
    public void PlayGoal_CountsEachWinOnceUntilTheRequiredCount()
    {
        var goal = new PlayActivityGoal { ActivityId = 410, RequiredCount = 3 };
        var progress = goal.CreateProgress();

        Assert.AreEqual(QuestGoalResult.Progressed, goal.Progress(Quest, Win(410), progress));
        Assert.AreEqual(QuestGoalResult.Progressed, goal.Progress(Quest, Win(410), progress));
        Assert.AreEqual(2, progress.Count);
        Assert.AreEqual(QuestGoalResult.Completed, goal.Progress(Quest, Win(410), progress));
    }

    [TestMethod]
    public void PlayGoal_WithoutAGameOrACount_IsNeverCredited()
    {
        var noGame = new PlayActivityGoal();
        var noCount = new PlayActivityGoal { ActivityId = 450, RequiredCount = 0 };

        Assert.AreEqual(QuestGoalResult.None, noGame.Progress(Quest, Win(0), noGame.CreateProgress()));
        Assert.AreEqual(QuestGoalResult.None, noCount.Progress(Quest, Win(450), noCount.CreateProgress()));
    }

    [TestMethod]
    public void PlayGoal_KeepsItsWinsOverARelog()
    {
        var goal = new PlayActivityGoal { ActivityId = 414, RequiredCount = 3 };
        var progress = goal.CreateProgress();
        goal.Progress(Quest, Win(414), progress);
        goal.Progress(Quest, Win(414), progress);

        var loaded = goal.CreateProgress();
        foreach (var (targetId, count) in progress.Save())
            loaded.Load(targetId, count);

        Assert.AreEqual(2, loaded.Count);
        Assert.AreEqual(QuestGoalResult.Completed, goal.Progress(Quest, Win(414), loaded));
    }

    [TestMethod]
    public void PlayGoal_NamesNoNpc_SoNoNpcWaitsOnIt()
    {
        var quest = new QuestDefinition
        {
            QuestId = 3,
            TargetNpcId = 500,
            Goals = [new PlayActivityGoal { ActivityId = 450 }, new TalkToNpcGoal { Targets = [new TalkTarget { NpcId = 500 }] }]
        };
        var progress = new QuestProgress(3);
        progress.StartStep(quest, 0);

        Assert.IsFalse(quest.IsWaitingOn(progress, Npc(500)), "The turn-in NPC's \"?\" waits until the game is won.");

        progress.FinishGoal(quest, 0);

        Assert.IsTrue(quest.IsWaitingOn(progress, Npc(500)));
    }

    [TestMethod]
    public void AWin_CreditsOneQuest()
    {
        Assert.IsTrue(Win(450).CreditsOneQuest, "One game won is one win, like one pickup.");
    }

    [TestMethod]
    public void Progress_RoundTripsThroughItsSavedRows()
    {
        var talkGoal = new TalkToNpcGoal { RequiredCount = 3, Targets = [new TalkTarget { NpcId = 600 }, new TalkTarget { NpcId = 601 }] };
        var talk = talkGoal.CreateProgress();
        talkGoal.Progress(Quest, Talk(600), talk);
        talkGoal.Progress(Quest, Talk(601), talk);

        var loadedTalk = talkGoal.CreateProgress();
        foreach (var (targetId, count) in talk.Save())
            loadedTalk.Load(targetId, count);

        Assert.AreEqual(2, loadedTalk.Count);
        Assert.AreEqual(QuestGoalResult.None, talkGoal.Progress(Quest, Talk(600), loadedTalk));

        var collectGoal = new CollectGoal { CollectNodeType = "tin-ore", RequiredCount = 5 };
        var collect = collectGoal.CreateProgress();
        collectGoal.Progress(Quest, Gather("tin-ore"), collect);
        collectGoal.Progress(Quest, Gather("tin-ore"), collect);

        var loadedCollect = collectGoal.CreateProgress();
        foreach (var (targetId, count) in collect.Save())
            loadedCollect.Load(targetId, count);

        Assert.AreEqual(2, collect.Save().Count());
        Assert.AreEqual(2, loadedCollect.Count);
    }

    [TestMethod]
    public void IsWaitingOn_OnlyTheCurrentGoalsUntalkedNpcsThenTheTurnInNpc()
    {
        var quest = new QuestDefinition
        {
            QuestId = 2,
            TargetNpcId = 500,
            Goals =
            [
                new TalkToNpcGoal { Targets = [new TalkTarget { NpcId = 600 }] },
                new TalkToNpcGoal { RequiredCount = 2, Targets = [new TalkTarget { NpcId = 700 }, new TalkTarget { NpcId = 701 }, new TalkTarget { NpcId = 702 }] }
            ]
        };
        var progress = new QuestProgress(2);
        progress.StartStep(quest, 0);

        Assert.IsTrue(quest.IsWaitingOn(progress, Npc(600)));
        Assert.IsFalse(quest.IsWaitingOn(progress, Npc(700)));

        progress.StartStep(quest, 1);
        quest.Goals[1].Progress(quest, Talk(700), progress.ProgressFor(1));

        Assert.IsFalse(quest.IsWaitingOn(progress, Npc(600)));
        Assert.IsFalse(quest.IsWaitingOn(progress, Npc(700)));
        Assert.IsTrue(quest.IsWaitingOn(progress, Npc(701)));

        progress.StartStep(quest, 2);

        Assert.IsFalse(quest.IsWaitingOn(progress, Npc(701)));
        Assert.IsTrue(quest.IsWaitingOn(progress, Npc(700)));
    }

    private static Npc Npc(int npcId) => new(null!) { DefinitionId = npcId };

    private static NpcTalkedEvent Talk(int npcId) => new(Npc(npcId));

    private static PlayerMovedEvent Move(float x, float z, int zoneDefinitionId = ReachLocationGoal.FabledRealmsZoneDefinitionId)
        => new(new Vector4(x, 0f, z, 1f), zoneDefinitionId);

    private static ActivityWonEvent Win(int activityId) => new(activityId);

    private static CollectionNode PlayerNode(string nodeType, int spawnId) => new(null!,
        new CollectionNodeTypeDefinition { Key = nodeType, Name = nodeType },
        new CollectionNodePoolDefinition { Key = nodeType, NodeType = nodeType, PlayerSpecific = true },
        new CollectionNodeSpawnDefinition { Id = spawnId });

    private static NodeGatheredEvent GatherPlayerNode(string nodeType, int spawnId) => new(PlayerNode(nodeType, spawnId));

    private static int _nextSpawnId;

    private static NodeGatheredEvent Gather(string nodeType) => GatherPlayerNode(nodeType, ++_nextSpawnId);
}
