using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The Housing door (<see cref="HouseDoors"/>, <see cref="HouseDoorService"/>, <c>docs/design/evergrove-housing.md</c>
/// section 10): what it is in the data, and what a click on it does. The house manager is played by
/// <see cref="FakeHouses"/>, except where a test says it uses the real one.
/// </summary>
[TestClass]
public sealed class HouseDoorTests
{
    private const ulong Character = QuestWorld.CharacterId;
    private const int Samantha = 2045;

    private const int QuestId = 1;
    private const int DoorStep = 437426;
    private const int BackToSamantha = 94363;

    private HouseWorld _world = null!;
    private FakeHouses _houses = null!;
    private HouseDoorService _doors = null!;
    private Npc _door = null!;
    private int _houseListsSent;

    [TestInitialize]
    public void Setup()
    {
        _world = new HouseWorld();
        _houses = new FakeHouses();
        _doors = new HouseDoorService(_houses, _world.Quests.Quests, _world.Quests.Resources, Options.Create(_world.Options));

        // Beside the quest world's player, who stands at the origin of a zone with nothing else in it.
        _door = new Npc(Player.Zone)
        {
            Guid = GuidHelper.GetNpcGuid(HouseDoors.CobblestoneVillage, GuidHelper.OpenWorldInstanceId),
            DefinitionId = HouseDoors.CobblestoneVillage
        };

        _door.UpdatePosition(new Vector4(5, 0, 0, 1), Quaternion.Identity);
    }

    [TestCleanup]
    public void Cleanup() => _world.Dispose();

    private Player Player => _world.Player;

    private HouseDoorResult Click(long now = 1_000) => _doors.Use(Player, _door, () => _houseListsSent++, now);

    /// <summary>Samantha sends you through the door and back to her, as Housing (9100) does.</summary>
    private static QuestDefinition ThroughTheDoor() => new()
    {
        QuestId = QuestId,
        GiverNpcId = Samantha,
        TargetNpcId = Samantha,
        Goals =
        [
            new TalkToNpcGoal { NameId = DoorStep, Targets = [new TalkTarget { NpcId = HouseDoors.CobblestoneVillage }] },
            new TalkToNpcGoal { NameId = BackToSamantha, Targets = [new TalkTarget { NpcId = Samantha }] }
        ]
    };

    private static DbHouse House(ulong id, int lot, HousePlot plot = HousePlot.Personal, int visitedDaysAgo = 0) => new()
    {
        Id = id,
        ZoneDefinitionId = lot,
        CharacterId = Character,
        Plot = plot,
        LastVisited = DateTimeOffset.UnixEpoch.AddYears(50).AddDays(-visitedDaysAgo)
    };

    private int NameOf(int lot) => ((HousingZoneDefinition)_world.Quests.Resources.Zones[lot]).NameId;

    // --- The data ---

    /// <summary>
    /// Every door is an NPC FabledRealms.lua spawns as a fixed NPC, so its GUID comes from its id, under a name, with a
    /// model the client has. The door behind Samantha stands within reach of her, where the Housing quest sends players.
    /// </summary>
    [TestMethod]
    public void EveryDoor_IsSpawnedNamedAndDrawn()
    {
        var script = File.ReadAllText(FindSourceFile("Scripts", "Zone", "FabledRealms.lua"));
        var models = File.ReadAllLines(FindSourceFile("Resources", "Models.txt"))
            .Select(line => line.Split('^')[0])
            .ToHashSet();

        using var npcsFile = JsonDocument.Parse(File.ReadAllText(FindSourceFile("Resources", "Npcs.json")));
        var npcs = npcsFile.RootElement.EnumerateArray().ToLookup(npc => npc.GetProperty("Id").GetInt32());

        Assert.IsTrue(HouseDoors.Ids.Count > 0);

        foreach (var door in HouseDoors.Ids)
        {
            Assert.AreEqual(1, Regex.Matches(script, $@"spawnFixedNpc\({door}, ").Count, $"Door {door} isn't spawned once by FabledRealms.lua.");
            Assert.IsTrue(GuidHelper.IsFixedNpcGuid(GuidHelper.GetNpcGuid((ulong)door, GuidHelper.OpenWorldInstanceId)),
                $"Door {door}'s id doesn't fit a fixed NPC's GUID.");

            var row = npcs[door].Single();
            Assert.AreNotEqual(0, row.GetProperty("NameId").GetInt32(), $"Door {door} has no name to show.");
            Assert.IsFalse(string.IsNullOrEmpty(row.GetProperty("Name").GetString()), $"Door {door}'s menu would have no title.");
            Assert.IsTrue(models.Contains(row.GetProperty("ModelId").GetInt32().ToString(CultureInfo.InvariantCulture)),
                $"Door {door}'s model isn't in Models.txt.");
        }

        var samantha = SpawnedAt(script, Samantha);
        var door90101 = SpawnedAt(script, HouseDoors.CobblestoneVillage);
        Assert.IsTrue(Vector3.Distance(samantha, door90101) < HouseDoors.WalkInRange, "The door stands beside Samantha.");
    }

    /// <summary>
    /// A door only completes a talk step as the player goes in: no shipped quest gives or takes a quest at one, puts one
    /// in a quest's last step or among a counted talk's NPCs, has it offer a game, or gives its step a line.
    /// </summary>
    [TestMethod]
    public void ShippedQuests_UseDoorsOnlyForStepsCompletedOnTheWayIn()
    {
        var quests = new QuestDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(quests.Load(FindSourceFile("Resources", "Quests.json")));

        var problems = quests.Quests.Values
            .SelectMany(quest => HouseDoors.QuestProblems(quest).Select(problem => $"Quest {quest.QuestId} {problem}"))
            .ToList();

        Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
    }

    [TestMethod]
    public void QuestProblems_FindEachWayADoorCantWork()
    {
        Assert.AreEqual(0, HouseDoors.QuestProblems(ThroughTheDoor()).Count(), "Through the door and back to Samantha is fine.");

        var door = HouseDoors.CobblestoneVillage;

        QuestDefinition Quest(Action<QuestDefinition> change)
        {
            var quest = ThroughTheDoor();
            change(quest);
            return quest;
        }

        TalkToNpcGoal DoorGoal(QuestDefinition quest) => (TalkToNpcGoal)quest.Goals[0];

        var misuses = new Dictionary<string, QuestDefinition>
        {
            ["given at the door"] = Quest(quest => quest.GiverNpcId = door),
            ["handed in at the door"] = Quest(quest => quest.TargetNpcId = door),
            ["the door's step is the last"] = Quest(quest => quest.Goals.RemoveAt(1)),
            ["the door's step is beside the last"] = Quest(quest => quest.Goals[1].WithPrevious = true),
            ["the door counted among NPCs"] = Quest(quest =>
            {
                DoorGoal(quest).RequiredCount = 2;
                DoorGoal(quest).Targets.Add(new TalkTarget { NpcId = Samantha });
            }),
            ["a line"] = Quest(quest => DoorGoal(quest).DialogueId = 78487),
            ["a conversation"] = Quest(quest => DoorGoal(quest).Dialogue.Add(new QuestDialogueLine { TextId = 78487 })),
            ["the door's own line"] = Quest(quest => DoorGoal(quest).Targets[0].DialogueId = 78487),
            ["completed after a conversation"] = Quest(quest => DoorGoal(quest).CompleteAfterDialogue = true),
            ["a game offered at the door"] = Quest(quest => quest.Goals.Insert(0,
                new PlayActivityGoal { NameId = 21837, ActivityId = 450, NpcId = door }))
        };

        foreach (var (misuse, quest) in misuses)
            Assert.IsTrue(HouseDoors.QuestProblems(quest).Any(), $"Not found: {misuse}.");
    }

    [TestMethod]
    public void TheQuestLoader_WarnsAboutADoorMisused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"housing-door-quests-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, """
                [
                  { "QuestId": 1, "GiverNpcId": 90101, "TargetNpcId": 2045,
                    "Goals": [ { "Type": 0, "NameId": 94363, "Targets": [ { "NpcId": 2045 } ] } ] }
                ]
                """);

            var logged = new HouseWorld.RecordingLogger<QuestDefinitionCollection>();

            Assert.IsTrue(new QuestDefinitionCollection(logged).Load(path), "A warning, not a refusal.");
            Assert.IsTrue(logged.Entries.Any(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Housing door 90101")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // --- A click ---

    [TestMethod]
    public void OneHouse_IsGoneIntoStraightAway_CompletingTheStepThere()
    {
        _world.Quests.Accept(ThroughTheDoor());
        _houses.Owned.Add(House(31, HouseWorld.Seaside));

        Assert.AreEqual(HouseDoorResult.GoingIn, Click());

        CollectionAssert.AreEqual(new[] { 31UL }, _houses.Entered);
        Assert.IsNull(Player.OpenInteractionMenu, "No menu for one house.");
        Assert.AreEqual(BackToSamantha, _world.Quests.ReadCharacter().Quests.Single().CurrentGoalId, "On to going back to Samantha.");
        Assert.AreEqual(0, _houseListsSent, "Nothing was given, so My Houses has nothing new.");
    }

    /// <summary>
    /// The door is the login's fallback: an account still without its Apartment is given it there, My Houses is told, and
    /// in they go.
    /// </summary>
    [TestMethod]
    public void NoHouse_TheApartmentIsGiven_MyHousesIsTold_AndInTheyGo()
    {
        _houses.Starter = StarterHouseResult.Granted;

        Assert.AreEqual(HouseDoorResult.GoingIn, Click());

        Assert.AreEqual(1, _houseListsSent);
        CollectionAssert.AreEqual(new[] { FakeHouses.GivenApartmentId }, _houses.Entered);
    }

    /// <summary>An account with only other lots is given its Apartment at the door too, and then has two houses.</summary>
    [TestMethod]
    public void OtherLotsOnly_TheApartmentIsGiven_AndBothAreOffered()
    {
        _houses.Owned.Add(House(31, HouseWorld.Seaside));
        _houses.Starter = StarterHouseResult.Granted;

        Assert.AreEqual(HouseDoorResult.Menu, Click());

        Assert.AreEqual(1, _houseListsSent);
        Assert.AreEqual(2, Player.OpenInteractionMenu!.Options.Count);
    }

    [TestMethod]
    public void NoHouseAndNoneGiven_SaysSo_AndGoesNowhere()
    {
        _houses.Starter = StarterHouseResult.Off;

        Assert.AreEqual(HouseDoorResult.NoHouse, Click());

        Assert.AreEqual(0, _houses.Entered.Count);
        Assert.IsNull(Player.OpenInteractionMenu);
        Assert.AreEqual(0, _houseListsSent);
    }

    /// <summary>
    /// Nate's decision 11: with several houses the door offers them, the ranked plot first, then the most recently
    /// visited, at most 6, each under its lot's name and icon. Nothing is gone into, or completed, until one is picked.
    /// </summary>
    [TestMethod]
    public void SeveralHouses_AreOfferedInOrder_AndNothingHappensUntilOneIsPicked()
    {
        var progress = _world.Quests.Accept(ThroughTheDoor());

        _houses.Owned.AddRange(
        [
            House(1, HouseWorld.Apartment, visitedDaysAgo: 9),
            House(2, HouseWorld.Seaside, visitedDaysAgo: 1),
            House(3, HouseWorld.BlacksporeHouse, HousePlot.Ranked, visitedDaysAgo: 30),
            House(4, 1005, visitedDaysAgo: 3),
            House(5, 1019, visitedDaysAgo: 2),
            House(6, 1001, visitedDaysAgo: 20),
            House(7, 1015, visitedDaysAgo: 40),
            House(8, 1016, visitedDaysAgo: 5)
        ]);

        Assert.AreEqual(HouseDoorResult.Menu, Click());

        Assert.AreEqual(0, _houses.Entered.Count);
        Assert.AreEqual(0, progress.GoalIndex, "Nothing is completed until a house is picked.");

        var ordered = HouseDoors.Order(_houses.Owned);
        CollectionAssert.AreEqual(new ulong[] { 3, 2, 5, 4, 8, 1 }, ordered.Select(house => house.Id).ToArray(),
            "The ranked plot, then the five most recently visited.");

        var menu = _doors.MenuFor(_door, ordered);
        CollectionAssert.AreEqual(ordered.Select(house => NameOf(house.ZoneDefinitionId)).ToArray(),
            menu.Select(option => option.ButtonTextId).ToArray());
        Assert.AreEqual(((HousingZoneDefinition)_world.Quests.Resources.Zones[HouseWorld.BlacksporeHouse]).IconId, menu[0].IconId);

        // The buttons the player was sent are those, in that order: the second goes into the most recently visited.
        var buttons = Player.OpenInteractionMenu!.Options.OrderBy(option => option.Key).Select(option => option.Value).ToList();
        Assert.AreEqual(HouseDoors.MenuLimit, buttons.Count);

        buttons[1](Player);

        CollectionAssert.AreEqual(new[] { 2UL }, _houses.Entered);
        Assert.AreEqual(1, progress.GoalIndex, "Picked and gone into: the step is completed.");
    }

    [TestMethod]
    public void FromAfar_TheClientWalksThePlayerUp_AndNothingElseHappens()
    {
        _houses.Owned.Add(House(31, HouseWorld.Seaside));
        _door.UpdatePosition(new Vector4(30, 0, 0, 1), Quaternion.Identity);

        Assert.AreEqual(HouseDoorResult.WalkingUp, Click());

        Assert.AreEqual(0, _houses.StarterAsked);
        Assert.AreEqual(0, _houses.Entered.Count);
    }

    [TestMethod]
    public void ADoorOutOfReach_DoesNothing()
    {
        _houses.Owned.Add(House(31, HouseWorld.Seaside));

        _door.UpdatePosition(new Vector4(150, 0, 0, 1), Quaternion.Identity);
        Assert.AreEqual(HouseDoorResult.OutOfReach, Click(), "Further than anything can be clicked.");

        _door.UpdatePosition(new Vector4(5, 0, 0, 1), Quaternion.Identity);
        _door.Zone = DispatchProxy.Create<IZone, QuestWorld.EmptyZone>();
        Assert.AreEqual(HouseDoorResult.OutOfReach, Click(), "In another zone.");

        Assert.AreEqual(0, _houses.StarterAsked);
        Assert.AreEqual(0, _houses.Entered.Count);
    }

    /// <summary>
    /// The house manager runs the step's completion only for an entry it lets through (<see cref="HouseEntryTests"/>), and
    /// the door does nothing to the quest itself: a refused entry completes nothing, and the player is told why.
    /// </summary>
    [TestMethod]
    public void ARefusedEntry_CompletesNothing()
    {
        var progress = _world.Quests.Accept(ThroughTheDoor());
        _houses.Owned.Add(House(31, HouseWorld.Seaside));
        _houses.EnterResult = EnterHouseResult.NotAuthorized;

        Assert.AreEqual(HouseDoorResult.GoingIn, Click());

        CollectionAssert.AreEqual(new[] { 31UL }, _houses.Entered);
        Assert.AreEqual(0, progress.GoalIndex);
        Assert.AreSame(_door.Zone, Player.Zone, "Still at the door.");
    }

    /// <summary>
    /// Clicking fast makes one move. Once the player has gone in, the door isn't in their zone any more. And while a move
    /// waits its turn (the player came out of a house moments ago, <see cref="ZoneTeleports"/>), another click replaces it.
    /// </summary>
    [TestMethod]
    public void ClicksInQuickSuccession_MakeOneMove()
    {
        _houses.Owned.Add(House(31, HouseWorld.Seaside));

        Assert.AreEqual(HouseDoorResult.GoingIn, Click(now: 1_000));
        Assert.AreEqual(HouseDoorResult.OutOfReach, Click(now: 1_100), "Gone in: the door is behind them.");
        Assert.AreEqual(1, _houses.Entered.Count);

        Player.Zone = _door.Zone;
        Player.NextZoneTeleportAt = 10_000;

        Click(now: 9_000);
        Click(now: 9_100);
        Assert.AreEqual(1, _houses.Entered.Count, "Too soon after the last move: it waits.");

        ZoneTeleports.Tick(Player, 10_000);
        ZoneTeleports.Tick(Player, 20_000);
        Assert.AreEqual(2, _houses.Entered.Count, "One more move, made when its turn came.");
    }

    /// <summary>
    /// The real house manager and quest manager, with the door in the world: an account with no house is given the
    /// Apartment, and the door's step is completed just before the move into it.
    /// </summary>
    [TestMethod]
    public void WithTheRealHouseManager_TheStepIsCompletedJustBeforeTheMoveIn()
    {
        var player = _world.PlayerInTheWorld(Character, _world.Account);
        var door = _world.AddNpc(HouseDoors.CobblestoneVillage, new Vector4(3, 0, 0, 1));
        var doors = new HouseDoorService(_world.Houses, _world.Quests.Quests, _world.Quests.Resources, Options.Create(_world.Options));

        _world.Quests.Add(ThroughTheDoor());
        _world.Quests.Quests.AcceptQuest(player, QuestId);
        Assert.IsTrue(player.Quests.TryGetInProgress(QuestId, out var progress));

        var goalAtTheMove = -1;
        _world.OnMove = () => goalAtTheMove = progress.GoalIndex;

        Assert.AreEqual(HouseDoorResult.GoingIn, doors.Use(player, door, () => _houseListsSent++, now: 1_000));

        Assert.AreEqual(1, goalAtTheMove, "Completed before the player left the world.");
        Assert.AreEqual((HouseWorld.Apartment, (ulong?)Character), _world.Moves.Single());
        Assert.IsInstanceOfType<HousingZone>(player.Zone);
        Assert.AreEqual(1, _houseListsSent, "The Apartment was given at the door.");
        Assert.AreEqual(HouseWorld.Apartment, _world.HousesOf(_world.Account).Single().ZoneDefinitionId);
    }

    private static Vector3 SpawnedAt(string script, int npcId)
    {
        var match = Regex.Match(script, $@"spawnFixedNpc\({npcId}, ([-\d.]+), ([-\d.]+), ([-\d.]+),");
        Assert.IsTrue(match.Success, $"NPC {npcId} isn't spawned.");

        float Coordinate(int group) => float.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

        return new Vector3(Coordinate(1), Coordinate(2), Coordinate(3));
    }

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

    /// <summary>
    /// The house manager as the door uses it: the account's houses; the starter house, given when a test says so; and
    /// each entry asked for, which goes in (running what goes with it, then moving the player out of the world) unless a
    /// test refuses it, as the real one does (<see cref="HouseEntryTests"/>).
    /// </summary>
    private sealed class FakeHouses : IHouseManager
    {
        public const ulong GivenApartmentId = 50;

        public List<DbHouse> Owned { get; } = [];

        public StarterHouseResult Starter { get; set; } = StarterHouseResult.AlreadyOwned;

        public int StarterAsked { get; private set; }

        public EnterHouseResult EnterResult { get; set; } = EnterHouseResult.Success;

        /// <summary>The house ids each entry asked for, in order.</summary>
        public List<ulong> Entered { get; } = [];

        public IReadOnlyList<DbHouse> GetOwnedHouses(ulong characterId) => Owned.ToList();

        public StarterHouseResult EnsureStarterHouse(Player player)
        {
            StarterAsked++;

            if (Starter != StarterHouseResult.Granted)
                return Starter;

            Owned.Add(new DbHouse { Id = GivenApartmentId, ZoneDefinitionId = HouseWorld.Apartment, CharacterId = player.CharacterId });
            Starter = StarterHouseResult.AlreadyOwned;

            return StarterHouseResult.Granted;
        }

        public EnterHouseResult EnterHouse(Player player, ulong houseGuid, Action? beforeTeleport = null)
        {
            Entered.Add(GuidHelper.GetHouseId(houseGuid));

            if (EnterResult != EnterHouseResult.Success)
                return EnterResult;

            beforeTeleport?.Invoke();
            player.Zone = DispatchProxy.Create<IZone, QuestWorld.EmptyZone>();

            return EnterResult;
        }

        public EnterHouseResult EnterOwnedHouse(Player player, int zoneDefinitionId) => throw new NotSupportedException();

        public EnterHouseResult VisitHouse(Player player, int zoneDefinitionId, string ownerName) => throw new NotSupportedException();

        public bool LeaveHouse(Player player) => throw new NotSupportedException();

        public int SendOutLockedOutVisitors(HousingZone zone) => throw new NotSupportedException();

        public HouseInviteResult InviteToHouse(Player inviter, ulong inviteeGuid, out Player? invitee) => throw new NotSupportedException();

        public bool DeclineInvite(Player invitee, ulong houseGuid) => throw new NotSupportedException();
    }
}
