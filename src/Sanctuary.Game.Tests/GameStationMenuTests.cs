using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Interactions;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A click on a world object that opens games (<see cref="GameStationMenu"/>), against the shipped Games menu entries and
/// the games the server serves: the buttons it shows after the object's quest buttons, the game each opens through the
/// Gateway's launcher (recorded, as <see cref="QuestWorld"/> records it), and the click itself as the client's arrives
/// (<see cref="Npc.OnInteract"/>): one game opens at once, several show a menu, and from afar the player is walked over
/// first. The chat line and the walk-over are packets, which aren't seen here.
/// </summary>
[TestClass]
public sealed class GameStationMenuTests
{
    private const int Mailbox = 2375;          // beside Flitter, in Sanctuary
    private const int Table = 2417;            // beside Bordon Cloo, in Sanctuary
    private const int LakesfieldTools = 33484; // Lakeshore Fields, whose game (570) isn't wired
    private const int Simone = 2069;

    private const int MailSorting = 414;
    private const int Steak = 450;
    private const int Sundae = 460;

    // In ClientActivityDefinitions.json: mail sorting's name in the Games menu (NameId) and the games' icons (ImageSetId).
    private const int MailSortingName = 102030;
    private const int MailIcon = 7558;
    private const int CookingIcon = 7546;

    // The recipes Mimic.json serves, in a table's order (by activity id), and their names in the Games menu.
    private static readonly int[] Recipes = [450, 451, 456, 460, 471];
    private static readonly int[] RecipeNames = [386024, 386025, 386030, 386045, 386059];

    private static MimicDefinitions _mimic = null!;
    private static ZoneDefinitionCollection _zones = null!;
    private static DailyWheel _dailyWheel = null!;

    private readonly HashSet<int> _notServed = [];

    private QuestWorld _world = null!;
    private QuestWorld.RecordingLogger _logged = null!;
    private GameStationMenu _menu = null!;
    private IZone _zone = null!;

    /// <summary>The world zone as a click reaches it: an NPC's buttons are the quest manager's, then its games.</summary>
    public class StationZone : DispatchProxy
    {
        public Func<Player, Npc, IReadOnlyList<NpcInteractionOption>> Options { get; set; } = null!;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IZone.GetInteractionOptions):
                    return Options((Player)args![0]!, (Npc)args[1]!);
                case "get_" + nameof(IZone.Npcs):
                    return Array.Empty<Npc>();
                case nameof(IZone.TryGetNpc):
                    args![1] = null;
                    return false;
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }

    [ClassInitialize]
    public static void LoadServedGames(TestContext _)
    {
        var resources = Path.GetDirectoryName(FindSourceFile("Resources", "Mimic.json"))!;

        Assert.IsTrue(MimicDefinitions.TryLoad(Path.Combine(resources, "Mimic.json"), NullLogger.Instance, out _mimic));

        _zones = new ZoneDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_zones.Load(Path.Combine(resources, "Zones")));

        Assert.IsTrue(DailyWheel.TryLoad(Path.Combine(resources, "DailyWheel.json"), NullLogger.Instance, out _dailyWheel));
    }

    [TestInitialize]
    public void Setup()
    {
        _world = new QuestWorld();
        _logged = new QuestWorld.RecordingLogger();

        // As WorldZone builds it, asking at each click whether the server serves a game.
        _menu = new GameStationMenu(_world.Resources.ClientActivityDefinitions,
            activityId => !_notServed.Contains(activityId) && PlayableActivities.IsPlayable(activityId, _mimic, _zones, _dailyWheel),
            _world.Launcher, _logged);

        // WorldZone asks the quest manager only for an NPC a quest names; it has no buttons for anyone else either.
        _zone = DispatchProxy.Create<IZone, StationZone>();
        ((StationZone)(object)_zone).Options = (player, npc) => _menu.OptionsAt(npc, _world.Quests.GetInteractionOptions(player, npc));

        _world.Player.Zone = _zone;
        _world.Player.UpdatePosition(new Vector4(0f, 0f, 0f, 1f), Quaternion.Identity);
    }

    [TestCleanup]
    public void Cleanup() => _world.Dispose();

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

    /// <summary>The object as BaseZone spawns it (<see cref="GameStations.SetUp"/>), <paramref name="distance"/> from the player.</summary>
    private Npc Spawn(int npcId, float distance = 5f)
    {
        var npc = new Npc(_zone)
        {
            Guid = GuidHelper.GetNpcGuid((ulong)npcId, GuidHelper.OpenWorldInstanceId),
            DefinitionId = npcId
        };

        GameStations.SetUp(npc);
        npc.UpdatePosition(new Vector4(distance, 0f, 0f, 1f), Quaternion.Identity);

        return npc;
    }

    /// <summary>The buttons a click on the object shows the player.</summary>
    private IReadOnlyList<NpcInteractionOption> Options(int npcId) => _zone.GetInteractionOptions(_world.Player, Spawn(npcId));

    /// <summary>The game each button opens, pressed one after another.</summary>
    private int[] GamesOpenedBy(IEnumerable<NpcInteractionOption> options) => options
        .Select(option =>
        {
            _world.Launched.Clear();
            option.Invoke(_world.Player);
            return _world.Launched.Single();
        })
        .ToArray();

    /// <summary>A cooking step (the steak) whose game <paramref name="offeredBy"/> offers, then back to Simone.</summary>
    private static QuestDefinition CookAt(int offeredBy) => new()
    {
        QuestId = 1,
        GiverNpcId = Simone,
        TargetNpcId = Simone,
        Goals =
        [
            new PlayActivityGoal { NameId = 21837, ActivityId = Steak, NpcId = offeredBy },
            new TalkToNpcGoal { NameId = 94536, Targets = [new TalkTarget { NpcId = Simone }] }
        ]
    };

    [TestMethod]
    public void TheMailboxBesideFlitter_OffersSanctuarysMailSorting()
    {
        var option = Options(Mailbox).Single();

        Assert.AreEqual(MailSortingName, option.ButtonTextId, "The game's name in the Games menu.");
        Assert.AreEqual(MailIcon, option.IconId, "The game's icon in the Games menu.");
        Assert.AreEqual(MailSorting, option.ActivityId);

        option.Invoke(_world.Player);

        CollectionAssert.AreEqual(new[] { MailSorting }, _world.Launched);
    }

    [TestMethod]
    public void ATable_OffersTheRecipesTheServerCooks_InOrder_EachOpeningItsOwn()
    {
        var options = Options(Table);

        CollectionAssert.AreEqual(RecipeNames, options.Select(option => option.ButtonTextId).ToArray());
        Assert.IsTrue(options.All(option => option.IconId == CookingIcon));
        CollectionAssert.AreEqual(Recipes, GamesOpenedBy(options));
    }

    [TestMethod]
    public void AnObjectWhoseGameIsntWired_AndAnyoneElse_OfferNothing()
    {
        Assert.AreEqual(0, Options(LakesfieldTools).Count);
        Assert.AreEqual(0, Options(Simone).Count);
    }

    /// <summary>Whether the server serves a game is asked at each click, so a reloaded Mimic.json changes the menu at once.</summary>
    [TestMethod]
    public void AGameTheServerNoLongerServes_IsLeftOut()
    {
        _notServed.Add(451);

        CollectionAssert.AreEqual(new[] { 450, 456, 460, 471 }, Options(Table).Select(option => option.ActivityId).ToArray());

        _notServed.UnionWith(Recipes);

        Assert.AreEqual(0, Options(Table).Count);
    }

    [TestMethod]
    public void AGameAQuestButtonOpens_ShowsOnce_AsTheQuests()
    {
        var questButton = new NpcInteractionOption
        {
            IconId = ContextIcons.QuestTurnIn,
            ButtonTextId = RecipeNames[0],
            ActivityId = Steak,
            Invoke = _ => { }
        };

        var options = _menu.OptionsAt(Spawn(Table), [questButton]);

        Assert.AreSame(questButton, options[0]);
        CollectionAssert.AreEqual(Recipes.Skip(1).ToArray(), options.Skip(1).Select(option => option.ActivityId).ToArray());
    }

    /// <summary>
    /// The round menu holds 24 buttons. Were all 28 Cooking recipes served, as a Mimic.json edit can do on the live server,
    /// a table would show its quest's buttons, then the recipes in order until the menu is full.
    /// </summary>
    [TestMethod]
    public void ATableServingEveryRecipe_FillsTheRoundMenu_AndNoMore()
    {
        var everyRecipe = new GameStationMenu(_world.Resources.ClientActivityDefinitions, _ => true, _world.Launcher, _logged);
        var table = Spawn(Table);

        // 24 is radial_menu.gfx's maxButtonCount, not GameStationMenu.MaxButtons: the client's limit is pinned here.
        CollectionAssert.AreEqual(Enumerable.Range(450, 24).ToArray(),
            everyRecipe.OptionsAt(table, []).Select(option => option.ActivityId).ToArray());

        var questButtons = Enumerable.Range(1, 3)
            .Select(id => new NpcInteractionOption { IconId = ContextIcons.QuestTurnIn, ButtonTextId = id, Invoke = _ => { } })
            .ToList();

        var options = everyRecipe.OptionsAt(table, questButtons);

        CollectionAssert.AreEqual(questButtons, options.Take(3).ToList());
        CollectionAssert.AreEqual(Enumerable.Range(450, 21).ToArray(), options.Skip(3).Select(option => option.ActivityId).ToArray());

        var fullMenu = Enumerable.Repeat(questButtons[0], 24).ToList();

        Assert.AreSame(fullMenu, everyRecipe.OptionsAt(table, fullMenu), "No room left: the quest's buttons, as they are.");
    }

    /// <summary>
    /// With the real quest manager: a cooking step whose game a table offers (PlayActivityGoal.NpcId) puts the steak first
    /// on the table's menu, under the quest's icon and opened for the quest; the other recipes follow, the steak not among
    /// them.
    /// </summary>
    [TestMethod]
    public void AQuestsGameAtATable_ComesFirst_AndShowsOnce()
    {
        _world.Accept(CookAt(Table));

        var options = Options(Table);

        Assert.AreEqual(ContextIcons.QuestTurnIn, options[0].IconId, "The quest's button.");
        Assert.IsTrue(options.Skip(1).All(option => option.IconId == CookingIcon));
        CollectionAssert.AreEqual(Recipes, options.Select(option => option.ActivityId).ToArray());
        CollectionAssert.AreEqual(Recipes, GamesOpenedBy(options));
    }

    /// <summary>
    /// A quest's talk step at an object (as Jonelle's Mailbag has one) keeps its button, first, with the object's game
    /// after it: two buttons, so the click shows the menu, and each button does its own.
    /// </summary>
    [TestMethod]
    public void AQuestsTalkStepAtAnObject_KeepsItsButton_BeforeTheGame()
    {
        var progress = _world.Accept(new QuestDefinition
        {
            QuestId = 1,
            TargetNpcId = Simone,
            Goals =
            [
                new TalkToNpcGoal { NameId = 30, Targets = [new TalkTarget { NpcId = Mailbox }] },
                new TalkToNpcGoal { NameId = 31, Targets = [new TalkTarget { NpcId = Simone }] }
            ]
        });

        var mailbox = Spawn(Mailbox);
        mailbox.OnInteract(_world.Player);

        var menu = _world.Player.OpenInteractionMenu;

        Assert.IsNotNull(menu, "Two buttons: a menu.");
        Assert.AreEqual(mailbox.Guid, menu.Guid);
        CollectionAssert.AreEqual(new[] { 1_000_000, 1_000_001 }, menu.Options.Keys.Order().ToArray());
        Assert.AreEqual(0, _world.Launched.Count, "The click alone opens nothing.");

        menu.Options[1_000_001](_world.Player);

        CollectionAssert.AreEqual(new[] { MailSorting }, _world.Launched, "The second button opens the game.");
        Assert.AreEqual(0, progress.GoalIndex);

        menu.Options[1_000_000](_world.Player);

        Assert.AreEqual(1, progress.GoalIndex, "The first is the quest's: the talk step is done.");
    }

    /// <summary>
    /// When the Gateway doesn't open the game, what stopped it is logged as what it is: the player's other game (they are
    /// told, below), a player who has left, or a game the server stopped serving after the button was shown.
    /// </summary>
    [TestMethod]
    [DataRow(ActivityLaunchResult.AnotherGameOpen, LogLevel.Debug, "another game is open")]
    [DataRow(ActivityLaunchResult.NotConnected, LogLevel.Information, "no longer connected")]
    [DataRow(ActivityLaunchResult.NotAGamesMenuGame, LogLevel.Warning, "isn't a game the Games menu opens")]
    public void AGameThatDidntOpen_IsLoggedForWhatStoppedIt(ActivityLaunchResult result, LogLevel level, string reason)
    {
        _world.LaunchResult = result;

        Options(Mailbox).Single().Invoke(_world.Player);

        var logged = _logged.Entries.Single();

        Assert.AreEqual(level, logged.Level);
        StringAssert.Contains(logged.Message, reason);
        StringAssert.Contains(logged.Message, "mailbox 2375");
        StringAssert.Contains(logged.Message, "activity 414");
    }

    [TestMethod]
    public void AGameThatOpened_LogsNothing()
    {
        Options(Mailbox).Single().Invoke(_world.Player);

        CollectionAssert.AreEqual(new[] { MailSorting }, _world.Launched);
        Assert.AreEqual(0, _logged.Entries.Count, "The Gateway logs the game it opens.");
    }

    /// <summary>
    /// Only what the player can fix is told to them, in the chat: another game open, or a game picked inside a house. A
    /// quest's NPC says the same, and the Games menu the house's line. Every result is listed, so a new one is decided here.
    /// </summary>
    [TestMethod]
    public void OnlyWhatThePlayerCanFix_IsToldToThem()
    {
        var told = new Dictionary<ActivityLaunchResult, string?>
        {
            [ActivityLaunchResult.Opened] = null,
            [ActivityLaunchResult.NotConnected] = null,
            [ActivityLaunchResult.NotAGamesMenuGame] = null,
            [ActivityLaunchResult.AnotherGameOpen] = "Finish or close the game you have open first.",
            [ActivityLaunchResult.InsideAHouse] = "Games can't be played inside a house. Step outside to play."
        };

        CollectionAssert.AreEquivalent(Enum.GetValues<ActivityLaunchResult>(), told.Keys.ToArray());

        foreach (var (result, line) in told)
            Assert.AreEqual(line, ActivityLaunches.Explain(result), result.ToString());
    }

    [TestMethod]
    public void OneGame_OpensAtTheClick_WithNoMenu()
    {
        Spawn(Mailbox).OnInteract(_world.Player);

        CollectionAssert.AreEqual(new[] { MailSorting }, _world.Launched, "The game's start screen is the confirmation.");
        Assert.IsNull(_world.Player.OpenInteractionMenu);
    }

    [TestMethod]
    public void SeveralGames_ShowAMenu_AndThePickOpensIt()
    {
        var table = Spawn(Table);

        table.OnInteract(_world.Player);

        var menu = _world.Player.OpenInteractionMenu;

        Assert.IsNotNull(menu);
        Assert.AreEqual(table.Guid, menu.Guid);
        CollectionAssert.AreEqual(new[] { 1_000_000, 1_000_001, 1_000_002, 1_000_003, 1_000_004 }, menu.Options.Keys.Order().ToArray());
        Assert.AreEqual(0, _world.Launched.Count);

        // The fourth button, as the select handler gets it back (CommandPacketInteractionSelect).
        menu.Options[1_000_003](_world.Player);

        CollectionAssert.AreEqual(new[] { Sundae }, _world.Launched);
    }

    /// <summary>
    /// Farther than the walk-in range, a click opens nothing yet: the client is told to walk the player over and click
    /// again (CommandPacketMoveAndInteract, not seen here). From there, the same click opens the game.
    /// </summary>
    [TestMethod]
    public void FromFartherAway_TheClickWalksThePlayerOverFirst()
    {
        Spawn(Mailbox, distance: 30f).OnInteract(_world.Player);
        Spawn(Table, distance: GameStations.WalkInRange + 0.5f).OnInteract(_world.Player);

        Assert.AreEqual(0, _world.Launched.Count);
        Assert.IsNull(_world.Player.OpenInteractionMenu);

        Spawn(Mailbox, distance: GameStations.WalkInRange).OnInteract(_world.Player);

        CollectionAssert.AreEqual(new[] { MailSorting }, _world.Launched);
    }

    [TestMethod]
    public void AStation_WalksThePlayerOver_FromWhereAQuestsNpcDoes()
    {
        Assert.AreEqual(GameStations.WalkInRange, new TalkToNpcGoal().InteractRange, "A quest's NPC walks the player in from 12.");

        Assert.AreEqual(GameStations.WalkInRange, Spawn(Table).QuestInteractRange);
        Assert.AreEqual(GameStations.WalkInRange, Spawn(LakesfieldTools).QuestInteractRange);
        Assert.AreEqual(0, Spawn(Simone).QuestInteractRange, "Anyone else is left as they are.");
    }

    [TestMethod]
    public void AnObjectThatCantBeClicked_OrIsOutOfReach_OpensNothing()
    {
        var mailbox = Spawn(Mailbox);
        mailbox.IsInteractable = false;

        mailbox.OnInteract(_world.Player);

        // Beyond its InteractRange (100), a click is answered with nothing, not a walk.
        Spawn(Table, distance: 150f).OnInteract(_world.Player);

        Assert.AreEqual(0, _world.Launched.Count);
        Assert.IsNull(_world.Player.OpenInteractionMenu);
    }

    /// <summary>An object with nothing to open does what it did before: its own behaviour (none, in the world).</summary>
    [TestMethod]
    public void AnObjectWithNothingToOpen_FallsThroughToItsOwnBehaviour()
    {
        var tools = Spawn(LakesfieldTools);
        var ownBehaviour = 0;
        tools.InteractAction = _ => ownBehaviour++;

        tools.OnInteract(_world.Player);

        Assert.AreEqual(1, ownBehaviour);
        Assert.AreEqual(0, _world.Launched.Count);
        Assert.IsNull(_world.Player.OpenInteractionMenu);
    }
}
