using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The objective marker and Take Me There on a Collect step point at the nearest pickup the player can still take, the
/// same target <see cref="QuestManager.TryGetActiveObjectiveTarget"/> answers with. Upstream OSFR #166 hid both on every
/// Collect step, because a player-specific pickup stays in the zone after a player takes it.
/// </summary>
[TestClass]
public sealed class QuestObjectiveTargetTests
{
    private const int QuestId = 9105;
    private const string Packages = "jonelles-packages";
    private const int TurnInNpcId = 4096;

    /// <summary>The zone the pickups lie in: its NPCs, and an NPC by guid.</summary>
    public class PickupZone : DispatchProxy
    {
        public List<Npc> Npcs { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_" + nameof(IZone.Npcs):
                    return Npcs;
                case nameof(IZone.TryGetNpc):
                    var npc = Npcs.FirstOrDefault(candidate => candidate.Guid == (ulong)args![0]!);
                    args![1] = npc;
                    return npc is not null;
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }

    /// <summary>The quests, the only resources the objective target reads.</summary>
    public class QuestResources : DispatchProxy
    {
        public QuestDefinitionCollection Quests { get; } = new(NullLogger.Instance);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == "get_" + nameof(IResourceManager.Quests)
                ? Quests
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private static QuestDefinition PackageQuest() => new()
    {
        QuestId = QuestId,
        TargetNpcId = TurnInNpcId,
        Goals =
        [
            new CollectGoal { NameId = 101999, CollectNodeType = Packages, RequiredCount = 3 },
            new TalkToNpcGoal { NameId = 139895, Targets = [new TalkTarget { NpcId = TurnInNpcId }] }
        ]
    };

    private IZone _zone = null!;
    private Player _player = null!;
    private QuestManager _questManager = null!;
    private ulong _nextGuid = 1;

    private List<Npc> ZoneNpcs => ((PickupZone)(object)_zone).Npcs;

    private CollectProgress Gathered => (CollectProgress)Progress.OpenGoals[0];

    private QuestProgress Progress => _player.Quests.TryGetInProgress(QuestId, out var progress) ? progress : throw new InvalidOperationException();

    [TestInitialize]
    public void Setup()
    {
        _zone = DispatchProxy.Create<IZone, PickupZone>();

        var resources = DispatchProxy.Create<IResourceManager, QuestResources>();
        var quest = PackageQuest();
        ((QuestResources)(object)resources).Quests.Quests[quest.QuestId] = quest;

        _questManager = new QuestManager(resources, null!, null!, NullLogger<QuestManager>.Instance);

        _player = new Player(null!, null!, null!, null!, null!) { Zone = _zone };
        _player.UpdatePosition(new Vector4(0f, 0f, 0f, 1f), Quaternion.Identity);

        var progress = new QuestProgress(quest.QuestId);
        progress.StartStep(quest, 0);
        _player.Quests.Add(progress);
        _player.Quests.ActiveQuestId = quest.QuestId;

        // The turn-in NPC stands nearest of all, so a Collect step that fell back to it would point there.
        ZoneNpcs.Add(Place(new Npc(_zone) { Guid = _nextGuid++, DefinitionId = TurnInNpcId }, 0.5f));
    }

    private Npc Place(Npc npc, float x)
    {
        npc.UpdatePosition(new Vector4(x, 0f, 0f, 1f), Quaternion.Identity);
        return npc;
    }

    private void AddPickup(int spawnId, float x, string nodeType = Packages, bool playerSpecific = true)
    {
        var node = new CollectionNode(_zone,
            new CollectionNodeTypeDefinition { Key = nodeType, Name = nodeType },
            new CollectionNodePoolDefinition { Key = nodeType, NodeType = nodeType, PlayerSpecific = playerSpecific },
            new CollectionNodeSpawnDefinition { Id = spawnId })
        {
            Guid = _nextGuid++
        };

        ZoneNpcs.Add(Place(node, x));
    }

    private float? TargetX() => _questManager.TryGetActiveObjectiveTarget(_player, out var target) ? target.X : null;

    [TestMethod]
    public void CollectStep_PointsAtTheNearestPickupThePlayerCanStillTake()
    {
        AddPickup(spawnId: 1, x: 2f);
        AddPickup(spawnId: 2, x: 3f, nodeType: "firewood");
        AddPickup(spawnId: 3, x: 4f, playerSpecific: false);
        AddPickup(spawnId: 4, x: 10f);
        AddPickup(spawnId: 5, x: 30f);

        Assert.AreEqual(2f, TargetX());

        // Taken, spawn 1 stays in the zone, hidden from this player alone. The next nearest are another quest's pickup
        // and a shared one, neither of which can count for the step.
        Gathered.TryAdd(1);

        Assert.AreEqual(10f, TargetX());
    }

    [TestMethod]
    public void CollectStep_MovesOnAsPickupsAreTaken()
    {
        AddPickup(spawnId: 4, x: 10f);
        AddPickup(spawnId: 5, x: 30f);

        Assert.AreEqual(10f, TargetX());

        Gathered.TryAdd(4);

        Assert.AreEqual(30f, TargetX());
    }

    [TestMethod]
    public void CollectStep_WithNothingLeftToTake_HasNoTarget()
    {
        AddPickup(spawnId: 4, x: 10f);
        AddPickup(spawnId: 6, x: 3f, nodeType: "firewood");

        Gathered.TryAdd(4);

        // Hidden, as every Collect step was before, rather than pointing at the turn-in NPC or another quest's pickup.
        Assert.IsNull(TargetX());
    }

    [TestMethod]
    public void AfterTheCollectStep_TheTargetIsTheNextStepsNpc()
    {
        AddPickup(spawnId: 4, x: 10f);

        Progress.FinishGoal(PackageQuest(), 0);

        Assert.AreEqual(0.5f, TargetX());
    }

    // The marker packet (what the client's Take Me There button and map pin read) takes the same answer from
    // ObjectiveTargetFor, with whether to take the last marker down when there is none.

    [TestMethod]
    public void Marker_OnACollectStep_IsTheNearestPickup()
    {
        AddPickup(spawnId: 4, x: 10f);

        var target = _questManager.ObjectiveTargetFor(_player, PackageQuest(), 0, out _);

        Assert.IsNotNull(target);
        Assert.AreEqual(10f, target.Value.Position.X);
        Assert.AreNotEqual(0UL, target.Value.Guid);
    }

    [TestMethod]
    public void Marker_OnACollectStepWithNothingLeft_ComesDown()
    {
        AddPickup(spawnId: 4, x: 10f);
        Gathered.TryAdd(4);

        Assert.IsNull(_questManager.ObjectiveTargetFor(_player, PackageQuest(), 0, out var clear));
        Assert.IsTrue(clear, "The client shows Take Me There only while there is a target.");
    }

    [TestMethod]
    public void Marker_OnATalkStepWhoseNpcIsElsewhere_StaysAsItWas()
    {
        ZoneNpcs.Clear();

        Assert.IsNull(_questManager.ObjectiveTargetFor(_player, PackageQuest(), 1, out var clear));
        Assert.IsFalse(clear, "An NPC step leaves the last marker up, as upstream does.");
    }

    [TestMethod]
    public void Marker_OnAHiddenObjective_ComesDown()
    {
        AddPickup(spawnId: 4, x: 10f);
        var quest = PackageQuest();
        quest.HideObjectiveTarget = true;

        Assert.IsNull(_questManager.ObjectiveTargetFor(_player, quest, 0, out var clear));
        Assert.IsTrue(clear);
    }
}
