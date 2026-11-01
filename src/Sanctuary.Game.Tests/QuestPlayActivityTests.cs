using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Interactions;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A quest step won by playing a game (<see cref="PlayActivityGoal"/>), from the win the server pays to the step
/// ticking and being saved. MimicHandler and FlashMinigameHandler raise <see cref="ActivityWonEvent"/> where
/// they pay a win; the quest manager does the rest. While the step is open, the NPC it names offers the game.
/// </summary>
[TestClass]
public sealed class QuestPlayActivityTests
{
    private const int Steak = 450;  // Cooking: Simone's Spiralmint Steak
    private const int Sundae = 460; // Cooking: Gummyfruit Topped Sundae
    private const int Simone = 2069;
    private const int Samantha = 2045;

    // NameIds in ClientActivityDefinitions.json: each recipe's name in the Games menu.
    private const int SteakOnTheGamesMenu = 386024;
    private const int SundaeOnTheGamesMenu = 386045;

    // The steak's DisplayNameId: the title of the Cooking start screen, which every recipe shares.
    private const int CookingTitle = 20226;

    private const int CookStep = 21837;
    private const int HandInStep = 94536;

    private QuestWorld _world = null!;

    [TestInitialize]
    public void Setup() => _world = new QuestWorld();

    [TestCleanup]
    public void Cleanup() => _world.Dispose();

    /// <summary>
    /// The Cobblestone Village Cafe's two steps: win the steak (or <paramref name="game"/>), which Simone offers (or
    /// <paramref name="offeredBy"/>), then take it to her.
    /// </summary>
    private static QuestDefinition Cafe(int questId = 9103, int wins = 1, int offeredBy = Simone, int game = Steak) => new()
    {
        QuestId = questId,
        GiverNpcId = Simone,
        TargetNpcId = Simone,
        Goals =
        [
            new PlayActivityGoal { NameId = CookStep, ActivityId = game, RequiredCount = wins, NpcId = offeredBy },
            new TalkToNpcGoal { NameId = HandInStep, Targets = [new TalkTarget { NpcId = Simone }] }
        ]
    };

    private void Win(int activityId) => _world.Quests.OnEvent(_world.Player, new ActivityWonEvent(activityId));

    /// <summary>What the NPC's menu offers the player for their quests, as a click on them asks.</summary>
    private List<NpcInteractionOption> Options(int npcId) =>
        _world.Quests.GetInteractionOptions(_world.Player, new Npc(null!) { DefinitionId = npcId });

    /// <summary>The game each of the NPC's buttons opens, by the button's text; no two buttons read the same.</summary>
    private Dictionary<int, int> GamesByButton(int npcId)
    {
        var games = new Dictionary<int, int>();

        foreach (var option in Options(npcId))
        {
            _world.Launched.Clear();
            option.Invoke(_world.Player);

            Assert.IsTrue(games.TryAdd(option.ButtonTextId, _world.Launched.Single()), $"Two buttons read {option.ButtonTextId}.");
        }

        return games;
    }

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

    /// <summary>
    /// A win two quests want goes to the one the player tracks, wherever it sits in their quest log: here the one taken
    /// second, which the log reaches last.
    /// </summary>
    [TestMethod]
    public void AWinTwoQuestsWant_TicksTheTrackedOne()
    {
        var first = _world.Accept(Cafe(questId: 1));
        var second = _world.Accept(Cafe(questId: 2));

        Assert.AreEqual(2, _world.Player.Quests.ActiveQuestId, "Taking a quest tracks it.");

        Win(Steak);

        Assert.AreEqual(1, second.GoalIndex, "The tracked quest's step ticks.");
        Assert.AreEqual(0, first.GoalIndex);
    }

    /// <summary>When the tracked quest doesn't want the win, it goes to the others by quest id, not by the log's order.</summary>
    [TestMethod]
    public void AWinTheTrackedQuestDoesntWant_GoesToTheLowestQuestId()
    {
        var higher = _world.Accept(Cafe(questId: 2));
        var lower = _world.Accept(Cafe(questId: 1));

        // Taken last, so tracked: a talk with Samantha.
        _world.Accept(new QuestDefinition
        {
            QuestId = 3,
            TargetNpcId = Samantha,
            Goals = [new TalkToNpcGoal { NameId = 30, Targets = [new TalkTarget { NpcId = Samantha }] }]
        });

        Win(Steak);

        Assert.AreEqual(1, lower.GoalIndex);
        Assert.AreEqual(0, higher.GoalIndex);
    }

    /// <summary>
    /// No world object starts a game yet, so while the cooking step is open Simone offers the steak herself: one option,
    /// under the game's name in the Games menu, that opens it the way the Games menu does (the Gateway's launcher,
    /// recorded here).
    /// </summary>
    [TestMethod]
    public void WhileTheCookingStepIsOpen_SimoneOffersTheSteak()
    {
        _world.Accept(Cafe());

        var option = Options(Simone).Single();

        Assert.AreEqual(ContextIcons.QuestTurnIn, option.IconId, "The icon of a quest in progress, as a hand-in shows.");
        Assert.AreEqual(SteakOnTheGamesMenu, option.ButtonTextId);

        option.Invoke(_world.Player);

        CollectionAssert.AreEqual(new[] { Steak }, _world.Launched);
        Assert.AreEqual(0, _world.Player.Quests.TryGetInProgress(9103, out var progress) ? progress.GoalIndex : -1,
            "Opening the game ticks nothing: the win does.");
    }

    [TestMethod]
    public void OnceTheSteakIsWon_SimoneTakesItInstead()
    {
        _world.Accept(Cafe());
        var shownBeforeTheWin = Options(Simone).Single();

        Win(Steak);

        // Picked from a menu shown before the steak was won (from the Games menu, say): nothing opens.
        shownBeforeTheWin.Invoke(_world.Player);
        Assert.AreEqual(0, _world.Launched.Count);

        Options(Simone).Single().Invoke(_world.Player);

        Assert.AreEqual(0, _world.Launched.Count, "Her one option now is the hand-in.");
        Assert.AreEqual(9103, _world.Player.Quests.PendingTurnInQuestId, "Handed in: her thanks and the rewards are shown.");
    }

    /// <summary>
    /// When the Gateway doesn't open the game, what stopped it is logged as what it is: the player's other game (one at a
    /// time; they are told in the chat, which isn't seen here), a player who has left, or a step naming a game the Games
    /// menu doesn't open, the only one that is a problem with the quest.
    /// </summary>
    [TestMethod]
    [DataRow(ActivityLaunchResult.AnotherGameOpen, LogLevel.Debug, "another game is open")]
    [DataRow(ActivityLaunchResult.NotConnected, LogLevel.Information, "no longer connected")]
    [DataRow(ActivityLaunchResult.NotAGamesMenuGame, LogLevel.Warning, "isn't a game the Games menu opens")]
    public void AGameThatDidntOpen_IsLoggedForWhatStoppedIt(ActivityLaunchResult result, LogLevel level, string reason)
    {
        _world.Accept(Cafe());
        _world.Logged.Entries.Clear();
        _world.LaunchResult = result;

        Options(Simone).Single().Invoke(_world.Player);

        var logged = _world.Logged.Entries.Single();

        Assert.AreEqual(level, logged.Level);
        StringAssert.Contains(logged.Message, reason);
        StringAssert.Contains(logged.Message, "activity 450");
        Assert.AreEqual(0, _world.Player.Quests.TryGetInProgress(9103, out var progress) ? progress.GoalIndex : -1);
    }

    [TestMethod]
    public void AGameThatOpened_LogsNothing()
    {
        _world.Accept(Cafe());
        _world.Logged.Entries.Clear();

        Options(Simone).Single().Invoke(_world.Player);

        CollectionAssert.AreEqual(new[] { Steak }, _world.Launched);
        Assert.AreEqual(0, _world.Logged.Entries.Count, "The Gateway logs the game it opens.");
    }

    [TestMethod]
    public void OnlyTheNpcTheStepNames_OffersTheGame()
    {
        _world.Accept(Cafe());

        Assert.AreEqual(0, Options(Samantha).Count);
    }

    /// <summary>
    /// Two games at one NPC (two quests' steps) are two buttons, each naming its recipe as the Games menu does. The start
    /// screen's title, which every Cooking recipe shares, would make them two alike.
    /// </summary>
    [TestMethod]
    public void TwoGamesAtOneNpc_AreTwoButtons_EachNamingItsRecipe()
    {
        _world.Accept(Cafe(questId: 1, game: Steak));
        _world.Accept(Cafe(questId: 2, game: Sundae));

        var games = GamesByButton(Simone);

        Assert.AreEqual(2, games.Count);
        Assert.AreEqual(Steak, games[SteakOnTheGamesMenu]);
        Assert.AreEqual(Sundae, games[SundaeOnTheGamesMenu]);
    }

    /// <summary>
    /// A game the Games menu doesn't name is called by its start screen's title, and one the server doesn't know by the
    /// step's own row.
    /// </summary>
    [TestMethod]
    public void AGameWithNoGamesMenuName_IsCalledByItsStartScreen_ElseByTheStep()
    {
        const int unnamed = 90_001, unknown = 90_002;
        _world.Resources.ClientActivityDefinitions.TryAdd(unnamed,
            new ClientActivityDefinition { Id = unnamed, DisplayNameId = CookingTitle });

        _world.Accept(Cafe(questId: 1, game: unnamed));
        _world.Accept(Cafe(questId: 2, game: unknown));

        var games = GamesByButton(Simone);

        Assert.AreEqual(2, games.Count);
        Assert.AreEqual(unnamed, games[CookingTitle]);
        Assert.AreEqual(unknown, games[CookStep]);
    }

    [TestMethod]
    public void AStepNoNpcOffers_IsPlayedFromTheGamesMenuAlone()
    {
        _world.Accept(Cafe(offeredBy: 0));

        Assert.AreEqual(0, Options(Simone).Count);

        Win(Steak);

        Assert.AreEqual(1, _world.Player.Quests.TryGetInProgress(9103, out var progress) ? progress.GoalIndex : -1);
    }
}
