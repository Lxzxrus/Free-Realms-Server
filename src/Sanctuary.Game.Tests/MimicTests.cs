using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Minigames.MatchThree;
using Sanctuary.Game.Minigames.Mimic;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class MimicTests
{
    /// <summary>What the server sent, as the Flash receives it: one tab-separated line per message.</summary>
    private sealed class Wire : IMimicOutput
    {
        public List<string> Lines { get; } = [];

        public void Send(string name, params object[] args) => Lines.Add(MatchThreeGame.Format(name, args));

        public IEnumerable<string> Named(string name) => Lines.Where(line => line.Split('\t')[0] == name);
    }

    private sealed class Clock
    {
        public long Now { get; set; } = 1_000;
    }

    private static string FindResource(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", name);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"src/Resources/{name} not found above the test output.");
    }

    private static MimicDefinitions Shipped()
    {
        Assert.IsTrue(MimicDefinitions.TryLoad(FindResource("Mimic.json"), NullLogger.Instance, out var definitions),
            "The shipped Mimic.json loads with no problems.");
        return definitions;
    }

    private static MimicGameDefinition Cooking() => Shipped().Find("cooking") ?? throw new AssertFailedException("No cooking game.");

    /// <summary>
    /// The Flash's side, as its ActionScript does it: <c>SoeNetworkTypeFreeRealms.OnData</c> splits each message on tabs
    /// and turns numbers into numbers; <c>GameServer.OnLevelDataFromFR</c> appends each phase row and a line break to a
    /// field that starts out undefined; on <c>OnDataComplete</c>, <c>CookingGameServer.OnGameData</c> checks the id and
    /// <c>OnLevelData</c> keeps the rows whose <c>LD_GAME</c> is that id, keyed by <c>LD_SEQUENCE</c>. Then it plays the
    /// rounds the way <c>CookingGameClient</c> reports them.
    /// </summary>
    private sealed class ScriptedCookingClient(MimicGame game, Wire wire, Clock clock)
    {
        private int _read;
        private object? _gameId;
        private string? _levelData;

        public bool Ready { get; private set; }
        public bool Ended { get; private set; }
        public string? EndedWith { get; private set; }

        /// <summary>Levels by sequence: stage type, duration, LD_DATA1 to LD_DATA11.</summary>
        public SortedDictionary<int, (int StageType, int Duration, int[] Data)> Levels { get; } = [];

        public void Send(params string[] message)
        {
            Assert.IsTrue(game.Handle(message), $"The server knows {message[0]}.");
            Receive();
        }

        private void Receive()
        {
            for (; _read < wire.Lines.Count; _read++)
            {
                var fields = wire.Lines[_read].Split('\t');
                var args = fields.Skip(1).Select(ConvertArg).ToArray();

                switch (fields[0])
                {
                    case "OnGameDataMsg":
                        _gameId = args[0];
                        break;
                    case "OnPhaseDataMsg":
                        // "undefined" + row + "\n" the first time: AS2 string concatenation onto an unset field.
                        _levelData = (_levelData ?? "undefined") + (string)args[0]! + "\n";
                        break;
                    case "OnDataComplete":
                        LoadLevels();
                        break;
                    case "OnGameWonMsg":
                    case "OnGameLostMsg":
                        Ended = true;
                        EndedWith = fields[0];
                        break;
                    default:
                        Assert.Fail($"The Cooking Flash has no handler for {fields[0]}.");
                        break;
                }
            }
        }

        private static object? ConvertArg(string arg) =>
            arg switch
            {
                "undefined" => null,
                "false" => false,
                "true" => true,
                _ => double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : arg
            };

        private void LoadLevels()
        {
            Assert.IsInstanceOfType<double>(_gameId, "OnGameDataMsg carries the game id as a number.");
            var id = (int)(double)_gameId!;

            // CookingGameServer.OnGameData gets String(mGameId) and looks for the row whose first field is the id.
            Assert.AreEqual(id, int.Parse(id.ToString(CultureInfo.InvariantCulture).Split('^')[0], CultureInfo.InvariantCulture));

            foreach (var line in (_levelData ?? string.Empty).Split('\n'))
            {
                var columns = line.Split('^');
                if (columns.Length < 2 || !int.TryParse(columns[1], out var lineGame) || lineGame != id)
                    continue;

                Assert.AreEqual(19, columns.Length, "LD_ID to LD_DATA11.");
                Assert.IsTrue(columns.Skip(2).All(column => int.TryParse(column, out _)),
                    "Every column after LD_ID is a number, so no round reads an undefined value.");

                var sequence = int.Parse(columns[2]);
                Levels[sequence] = (int.Parse(columns[7]), int.Parse(columns[3]), columns.Skip(8).Select(int.Parse).ToArray());
            }

            Ready = true;
        }

        /// <summary>One round won, as CookingGameClient reports it: scene ready, round won, its stats, the new total.</summary>
        public void PlayRound(int sequence, double seconds, long total)
        {
            Send("FRServer_RoundTransitionPause", "false");
            clock.Now += (long)(seconds * 1000);
            Send("FRServer_RoundTransitionPause", "true");

            // SoeInterface.UpdateStats builds its message without the closing quote.
            var stage = Levels[sequence].StageType.ToString(CultureInfo.InvariantCulture);
            Send("FRServer_StatUpdate", stage, "0", "1)");
            Send("FRServer_StatUpdate", stage, "1", "3)");
            Send("FRServer_StatUpdate", stage, "2", "4)");
            Send("FRServer_StatUpdate", stage, "3", "12)");

            // The end-of-round window stays a few seconds before the player closes it.
            clock.Now += 3_000;
            Send("FRServer_ScoreSet", total.ToString(CultureInfo.InvariantCulture));
        }

        public void Win(long total, int seconds)
        {
            Send("FRServer_StatUpdate", "-1", "5", "2)");
            Send("FRServer_StatUpdate", "-1", "6", "10)");
            Send("FRServer_StatUpdate", "-1", "7", "12)");
            Send("FRServer_StatUpdate", "-1", "4", seconds.ToString(CultureInfo.InvariantCulture) + ")");
            Send("FRServer_GameWon", total.ToString(CultureInfo.InvariantCulture), "0");
        }
    }

    private static (MimicGame Game, Wire Wire, Clock Clock, ScriptedCookingClient Client) Start(MimicRecipeDefinition recipe)
    {
        var wire = new Wire();
        var clock = new Clock();
        var game = new MimicGame(Cooking(), recipe, wire, () => clock.Now);
        var client = new ScriptedCookingClient(game, wire, clock);

        // GameClient.GameSetupComplete: the external connection, then the request for data.
        client.Send("OnConnectMsg");
        client.Send("FRServer_ReadyForGameData");

        return (game, wire, clock, client);
    }

    [TestMethod]
    public void ShippedRecipes_PlayClientActivities_ThatAreCookingGames()
    {
        var cooking = Cooking();
        Assert.IsTrue(cooking.Recipes.Count >= 3, "A few recipes.");

        using var activities = JsonDocument.Parse(File.ReadAllText(FindResource("ClientActivityDefinitions.json")));
        var byId = activities.RootElement.EnumerateArray().ToDictionary(activity => activity.GetProperty("Id").GetInt32());

        foreach (var recipe in cooking.Recipes)
        {
            Assert.IsTrue(byId.TryGetValue(recipe.ActivityId, out var activity), $"{recipe.Name}: activity {recipe.ActivityId} exists.");
            StringAssert.StartsWith(activity.GetProperty("Comment").GetString(), "Cooking: ", recipe.Name);
            Assert.AreEqual(recipe.MiniGameId, activity.GetProperty("Unknown").GetInt32(),
                $"{recipe.Name}: MiniGameId is the activity's MiniGameData row.");
        }
    }

    [TestMethod]
    public void PhaseRow_HasEveryColumn_InTheFlashsOrder()
    {
        var recipe = new MimicRecipeDefinition
        {
            Name = "Test",
            GameId = 12,
            Phases = [new MimicPhaseDefinition { StageType = 10, DurationSeconds = 45, ObjectiveId = 3, Data = [12, 5, 100] }]
        };

        Assert.AreEqual("1201^12^1^45^3^0^0^10^12^5^100^0^0^0^0^0^0^0^0", recipe.PhaseRow(1));
    }

    [TestMethod]
    public void Problems_CatchRecipesTheFlashCantDraw()
    {
        var cooking = Cooking();

        MimicRecipeDefinition Recipe(params MimicPhaseDefinition[] phases) => new()
        {
            Name = "Bad", ActivityId = 1, MiniGameId = 1, GameId = 900, Phases = [.. phases]
        };

        static MimicPhaseDefinition Phase(int stage, params int[] data) => new() { StageType = stage, DurationSeconds = 30, Data = [.. data] };

        // No pour scene; a fish can't be tenderized; adding needs something gathered; a stew needs a measured broth.
        Assert.IsTrue(Recipe(Phase(5, 19)).Problems(cooking).Any(p => p.Contains("no scene")));
        Assert.IsTrue(Recipe(Phase(CookingStages.Tenderize, 0)).Problems(cooking).Any(p => p.Contains("no picture")));
        Assert.IsTrue(Recipe(Phase(CookingStages.AddIngredients, 42)).Problems(cooking).Any(p => p.Contains("gather")));
        Assert.IsTrue(Recipe(Phase(CookingStages.Chop, 26), Phase(CookingStages.Stir, 46)).Problems(cooking).Any(p => p.Contains("broth")));
        Assert.IsTrue(new MimicRecipeDefinition
        {
            Name = "Promo", ActivityId = 1, MiniGameId = 1, GameId = MimicRecipeDefinition.PromotionGameId,
            Phases = [Phase(CookingStages.Chop, 26)]
        }.Problems(cooking).Any(p => p.Contains("promotion")));

        Assert.IsFalse(Recipe(Phase(CookingStages.Measure, 48), Phase(CookingStages.Chop, 26), Phase(CookingStages.Stir, 46))
            .Problems(cooking).Any(), "A valid little stew.");
    }

    [TestMethod]
    public void ScriptedClient_PlaysEveryShippedRecipe_ToAWin()
    {
        foreach (var recipe in Cooking().Recipes)
        {
            var (game, wire, clock, client) = Start(recipe);

            Assert.IsTrue(client.Ready, $"{recipe.Name}: the Flash has its data.");
            Assert.AreEqual(1, wire.Named("OnGameDataMsg").Count());
            Assert.AreEqual(recipe.Phases.Count, client.Levels.Count, $"{recipe.Name}: every phase reached the Flash.");
            CollectionAssert.AreEqual(Enumerable.Range(1, recipe.Phases.Count).ToList(), client.Levels.Keys.ToList(),
                "Sequences run 1 to N, as GameClient.initRoundsArray walks them.");

            for (var i = 1; i <= recipe.Phases.Count; i++)
            {
                var phase = recipe.Phases[i - 1];
                Assert.AreEqual(phase.StageType, client.Levels[i].StageType);
                Assert.AreEqual(phase.DurationSeconds, client.Levels[i].Duration);
                Assert.AreEqual(phase.Data[0], client.Levels[i].Data[0], "LD_DATA1, the ingredient.");
            }

            long total = 0;
            for (var i = 1; i <= recipe.Phases.Count; i++)
            {
                total += 2_500;
                client.PlayRound(i, seconds: 20, total);
            }

            Assert.AreEqual(MimicResult.None, game.Result, "No result before the Flash reports one.");
            Assert.AreEqual(recipe.Phases.Count, game.RoundsFinished);

            client.Win(total, seconds: 23 * recipe.Phases.Count);

            Assert.AreEqual(MimicResult.Won, game.Result, recipe.Name);
            Assert.AreEqual(total, game.Score);
            Assert.AreEqual(23 * recipe.Phases.Count, game.ReportedSeconds);
            Assert.IsTrue(client.Ended);
            Assert.AreEqual("OnGameWonMsg", client.EndedWith);

            // The Flash's EndOfGame answers with FRServer_GameOver; a second "won" pays nothing more.
            client.Send("FRServer_GameOver");
            client.Send("FRServer_GameWon", "999999", "0");
            Assert.AreEqual(1, wire.Named("OnGameWonMsg").Count(), "One result per game.");
            Assert.AreEqual(total, game.Score);
        }
    }

    [TestMethod]
    public void DataIsSentOnce()
    {
        var recipe = Cooking().Recipes[0];
        var (_, wire, _, client) = Start(recipe);

        client.Send("FRServer_ReadyForGameData");

        Assert.AreEqual(1, wire.Named("OnDataComplete").Count());
        Assert.AreEqual(recipe.Phases.Count, wire.Named("OnPhaseDataMsg").Count());
    }

    [TestMethod]
    public void AWinBeforeTheData_IsIgnored()
    {
        var wire = new Wire();
        var game = new MimicGame(Cooking(), Cooking().Recipes[0], wire, () => 0);

        game.Handle(["FRServer_GameWon", "5000", "0"]);

        Assert.AreEqual(MimicResult.None, game.Result);
        Assert.AreEqual(0, wire.Lines.Count);
    }

    [TestMethod]
    public void AWinWithRoundsMissing_IsRefused()
    {
        var recipe = Cooking().Recipes[0];
        var (game, _, clock, client) = Start(recipe);

        client.PlayRound(1, seconds: 20, 2_500);
        clock.Now += 600_000;
        client.Win(2_500, seconds: 600);

        Assert.AreEqual(MimicResult.Refused, game.Result);
        StringAssert.Contains(game.RefusedBecause, "rounds finished");
        Assert.AreEqual("OnGameLostMsg", client.EndedWith, "The Flash still ends its game.");
    }

    [TestMethod]
    public void AWinFasterThanTheRoundsCanBePlayed_IsRefused()
    {
        var recipe = Cooking().Recipes[0];
        var (game, _, _, client) = Start(recipe);

        // Every round reported, but with no time passing.
        for (var i = 1; i <= recipe.Phases.Count; i++)
        {
            client.Send("FRServer_RoundTransitionPause", "true");
        }

        client.Win(50_000, seconds: 1);

        Assert.AreEqual(MimicResult.Refused, game.Result);
        StringAssert.Contains(game.RefusedBecause, "under the");
    }

    [TestMethod]
    public void RoundReports_CountAtMostOncePerPhase()
    {
        var recipe = Cooking().Recipes[0];
        var (game, _, _, client) = Start(recipe);

        for (var i = 0; i < 50; i++)
            client.Send("FRServer_RoundTransitionPause", "true");

        Assert.AreEqual(recipe.Phases.Count, game.RoundsFinished);
    }

    [TestMethod]
    public void AScoreAboveTheMaximum_IsCapped()
    {
        var cooking = Cooking();
        var recipe = cooking.Recipes[0];
        var (game, _, _, client) = Start(recipe);

        for (var i = 1; i <= recipe.Phases.Count; i++)
            client.PlayRound(i, seconds: 20, 1);

        client.Win(long.MaxValue / 2, seconds: 100);

        Assert.AreEqual(MimicResult.Won, game.Result);
        Assert.AreEqual(cooking.MaxScore, game.Score);
    }

    [TestMethod]
    public void ALoss_IsALoss()
    {
        var (game, _, _, client) = Start(Cooking().Recipes[0]);

        client.Send("FRServer_GameLost", "1200");

        Assert.AreEqual(MimicResult.Lost, game.Result);
        Assert.AreEqual(1200, game.Score);
        Assert.AreEqual("OnGameLostMsg", client.EndedWith);
    }

    [TestMethod]
    public void UnknownMessages_AreNotTheGames()
    {
        var game = new MimicGame(Cooking(), Cooking().Recipes[0], new Wire(), () => 0);

        Assert.IsFalse(game.Handle(["OnChainMsg", "1,2"]));
        Assert.IsFalse(game.Handle([]));
    }

    [TestMethod]
    [DataRow("12", 12.0)]
    [DataRow("12)", 12.0)]
    [DataRow(" \"7\" ", 7.0)]
    [DataRow("-1", -1.0)]
    [DataRow("2.5", 2.5)]
    public void Numbers_FromTheFlash_AreReadLeniently(string text, double expected)
    {
        Assert.IsTrue(MimicGame.TryParseNumber(text, out var value));
        Assert.AreEqual(expected, value);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    [DataRow("lots")]
    public void NotNumbers_AreRefused(string text) => Assert.IsFalse(MimicGame.TryParseNumber(text, out _));
}
