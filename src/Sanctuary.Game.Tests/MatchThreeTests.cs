using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Minigames.MatchThree;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class MatchThreeTests
{
    private sealed class Recorder : IMatchThreeOutput
    {
        public List<(string Name, object[] Args)> Messages { get; } = [];

        public void Send(string name, params object[] args) => Messages.Add((name, args));

        public IEnumerable<(string Name, object[] Args)> Named(string name) => Messages.Where(message => message.Name == name);

        public IEnumerable<object[]> Events(int type) =>
            Named("OnLevelEventMsg").Select(message => message.Args).Where(args => (int)args[0] == type);

        public void Clear() => Messages.Clear();
    }

    private sealed class Clock
    {
        public long Now { get; set; } = 1_000;
    }

    private static string FindShipped()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", "MatchThree.json");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("src/Resources/MatchThree.json not found above the test output.");
    }

    private static MatchThreeGameDefinition Game(string name)
    {
        Assert.IsTrue(MatchThreeDefinitions.TryLoad(FindShipped(), NullLogger.Instance, out var definitions));
        return definitions.Find(name) ?? throw new AssertFailedException($"No {name} game.");
    }

    private static MatchThreeGameDefinition Mining() => Game("mining");

    /// <summary>One shipped level on its own, as level 1 of a game.</summary>
    private static MatchThreeGameDefinition OnlyLevel(string name, int level)
    {
        var game = Game(name);
        return new MatchThreeGameDefinition { Name = game.Name, GameNumber = game.GameNumber, Levels = [game.Levels[level - 1]] };
    }

    private static (MatchThreeGame Game, Recorder Output, Clock Clock) Start(MatchThreeGameDefinition definition, int seed = 7)
    {
        var output = new Recorder();
        var clock = new Clock();
        var game = new MatchThreeGame(definition, output, () => clock.Now, new Random(seed));

        game.Handle(["FRServer_Connect"]);
        game.Handle(["OnStartGameMsg", "0"]);
        game.Handle(["OnStartLevelMsg", "0"]);

        return (game, output, clock);
    }

    /// <summary>
    /// Plays like the client: accepts queue resets, reports ore falling out, marks its tiles idle after animating,
    /// then chains three touching rocks of one kind (wildcards join any), or else clicks a dynamite, picking its
    /// tiles with the shared random numbers the way the client's own code does.
    /// </summary>
    /// <remarks>A new level builds a new board, so a client per level reads only what was sent from <paramref name="from"/> on.</remarks>
    private sealed class ScriptedClient(MatchThreeGame game, Recorder output, int from = 0)
    {
        private int _seen = from;
        private int _randomIndex;

        public MatchThreeGrid Grid => game.Level!.Grid!;

        /// <summary>One move. False if the board has no move the client could make.</summary>
        public bool PlayTurn()
        {
            AcceptResets();
            DropOre();

            if (!game.Level!.IsInProgress)
                return true;

            AllIdle();

            if (FindChain(Grid) is { } chain)
            {
                game.Handle(["OnChainMsg", Chain(chain)]);
                return true;
            }

            if (Grid.Tiles.FirstOrDefault(tile => tile.Value == MatchThreeTiles.PowerupDynamite) is { } dynamite)
            {
                Dynamite(dynamite, game.Level.Powerups[MatchThreeTiles.PowerupDynamite].Definition.Power);
                return true;
            }

            return false;
        }

        public void AcceptResets()
        {
            var resets = output.Messages.Skip(_seen).Count(message => message.Name == "OnTileQueueMsg" && (int)message.Args[0] >= 0);
            _seen = output.Messages.Count;

            for (var i = 0; i < resets; i++)
                game.Handle(["OnLevelEventMsg", "60"]);
        }

        public void DropOre()
        {
            foreach (var tile in Grid.Tiles.Where(tile => tile.IsFallOffBottom).ToList())
                game.Handle(["OnLevelEventMsg", "50", tile.X.ToString(), tile.Y.ToString()]);
        }

        public void AllIdle() =>
            game.Handle(["OnIdleMsg", Chain(Grid.Tiles.SelectMany(tile => new[] { tile.X, tile.Y }))]);

        private int NextRandom()
        {
            var numbers = output.Messages.Skip(from).Where(message => message.Name == "OnRandomQueueMsg").SelectMany(message => ((string)message.Args[0]).Split(',')).Select(int.Parse).ToList();
            return numbers[_randomIndex++];
        }

        /// <summary>The client's startShatter: the dynamite, then a random idle neighbour of what's gone, ring by ring.</summary>
        private void Dynamite(MatchThreeTile dynamite, int power)
        {
            var gone = new HashSet<MatchThreeTile>();
            var next = new List<MatchThreeTile>();
            var later = new List<MatchThreeTile>();
            var listed = new List<int>();
            MatchThreeTile? tile = dynamite;

            MatchThreeTile? At(int x, int y) => Grid.At(x, y) is { } at && !gone.Contains(at) ? at : null;

            while (power > 0 && tile is not null)
            {
                if (tile.State == MatchThreeTileState.Idle)
                {
                    gone.Add(tile);
                    listed.AddRange([tile.X, tile.Y]);

                    foreach (var neighbour in new[] { At(tile.X, tile.Y + 1), At(tile.X, tile.Y - 1), At(tile.X + 1, tile.Y), At(tile.X - 1, tile.Y) })
                    {
                        if (neighbour is { State: MatchThreeTileState.Idle, Category: MatchThreeTiles.Selectable or MatchThreeTiles.Wildcard or MatchThreeTiles.Blocker }
                            && !later.Contains(neighbour))
                            later.Add(neighbour);
                    }

                    power--;
                }

                if (power > 0)
                {
                    if (next.Count == 0)
                    {
                        next = [.. later];
                        later = [];
                    }

                    if (next.Count == 0)
                        tile = null;
                    else
                    {
                        var pick = NextRandom() % next.Count;
                        tile = next[pick];
                        next.RemoveAt(pick);
                    }
                }
            }

            game.Handle(["OnActionMsg", MatchThreeTiles.PowerupDynamite.ToString(), Chain(listed)]);
        }
    }

    /// <summary>
    /// A chain on the server's board: three touching tiles, all rocks of one kind or wildcards (which join any), as
    /// x, y pairs. It may start on a wildcard, as the client allows.
    /// </summary>
    private static List<int>? FindChain(MatchThreeGrid grid)
    {
        for (var y = 1; y < grid.Height; y++)
        {
            for (var x = 1; x < grid.Width; x++)
            {
                if (grid.At(x, y) is not { } start || start.Category is not (MatchThreeTiles.Selectable or MatchThreeTiles.Wildcard))
                    continue;

                var path = new List<MatchThreeTile> { start };
                if (Extend(grid, path, start.Category == MatchThreeTiles.Selectable ? start.Value : null))
                    return path.SelectMany(tile => new[] { tile.X, tile.Y }).ToList();
            }
        }

        return null;
    }

    private static bool Extend(MatchThreeGrid grid, List<MatchThreeTile> path, int? value)
    {
        if (path.Count >= 3)
            return true;

        var last = path[^1];
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            if (grid.At(last.X + dx, last.Y + dy) is not { } next || path.Contains(next))
                continue;

            int? nextValue;
            if (next.Value == MatchThreeTiles.Wildcard)
                nextValue = value;
            else if (next.Category == MatchThreeTiles.Selectable && (value is null || next.Value == value))
                nextValue = next.Value;
            else
                continue;

            path.Add(next);
            if (Extend(grid, path, nextValue))
                return true;

            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private static string Chain(IEnumerable<int> positions) => string.Join(',', positions) + ",";

    [TestMethod]
    public void ShippedLevels_Load()
    {
        var mining = Mining();

        Assert.AreEqual(1, mining.Levels.Count);
        Assert.AreEqual(0, mining.Levels[0].Problems().Count());
    }

    [TestMethod]
    public void Problems_CatchABrokenLevel()
    {
        var level = Mining().Levels[0];

        level.Grid[0][3] = 1;
        Assert.IsTrue(level.Problems().Any(problem => problem.Contains("row 0")), "Row 0 must stay empty.");

        level = Mining().Levels[0];
        level.Selectables.Clear();
        Assert.IsTrue(level.Problems().Any(problem => problem.Contains("rocks")));

        level = Mining().Levels[0];
        level.Powerups.Add(new MatchThreeSpawnDefinition { Type = 7, Chance = 1 });
        Assert.IsTrue(level.Problems().Any(problem => problem.Contains("power-up types")));
    }

    [TestMethod]
    public void Connect_SaysReadyOnce()
    {
        var output = new Recorder();
        var game = new MatchThreeGame(Mining(), output, () => 1, new Random(1));

        game.Handle(["FRServer_Connect"]);
        game.Handle(["OnConnectMsg"]);

        CollectionAssert.AreEqual(new[] { "OnActiveMsg", "OnServerReadyMsg" }, output.Messages.Select(message => message.Name).ToArray());
        Assert.AreEqual("OnActiveMsg\tfalse", MatchThreeGame.Format("OnActiveMsg", output.Messages[0].Args));
    }

    [TestMethod]
    public void StartingALevel_SendsTheBoardInTheClientsOrder()
    {
        var (game, output, _) = Start(Mining());
        var names = output.Messages.Select(message => message.Name).ToList();

        Assert.AreEqual("OnNextLevelMsg\t1\t0\t0", MatchThreeGame.Format("OnNextLevelMsg", output.Named("OnNextLevelMsg").Single().Args));

        // The pictures first, then the board's tiles, the board, the refill queue and the random numbers.
        var firstQueue = names.IndexOf("OnTileQueueMsg");
        Assert.IsTrue(names.IndexOf("OnTileInfoMsg") < firstQueue);
        Assert.AreEqual(firstQueue + 1, names.IndexOf("OnBuildMapMsg"));
        Assert.IsTrue(names.IndexOf("OnRandomQueueMsg") < names.IndexOf("OnLevelInitMsg"));
        Assert.AreEqual("OnActiveMsg", names[^1]);

        var grid = game.Level!.Grid!;
        var buildMap = output.Named("OnBuildMapMsg").Single().Args;
        Assert.AreEqual(grid.Width, buildMap[0]);
        Assert.AreEqual(grid.Height, buildMap[1]);
        Assert.AreEqual(grid.Width * grid.Height, ((string)buildMap[2]).Split(',').Length);

        // The board's own tiles go first (one per cell), with -1: append, don't reset.
        var boardTiles = output.Named("OnTileQueueMsg").First().Args;
        Assert.AreEqual(-1, boardTiles[0]);
        Assert.AreEqual(grid.Width * grid.Height, ((string)boardTiles[1]).Split(',').Length);

        Assert.AreEqual(5 + 1, output.Named("OnTileInfoMsg").Count(), "Five rocks and one ore.");
        Assert.AreEqual("OnLevelInitMsg\t1\t60\t2", MatchThreeGame.Format("OnLevelInitMsg", output.Named("OnLevelInitMsg").Single().Args));
        Assert.AreEqual("OnQuotaInitMsg\t0\t613\t5\t1\t698", MatchThreeGame.Format("OnQuotaInitMsg", output.Named("OnQuotaInitMsg").Single().Args));
        Assert.AreEqual(2, output.Named("OnPowerupInitMsg").Count());

        // Every playable cell holds a tile the client can see.
        for (var y = 1; y < grid.Height - 1; y++)
            for (var x = 1; x < grid.Width; x++)
                Assert.IsTrue(grid.At(x, y)?.Category >= MatchThreeTiles.Visible, $"{x},{y} is empty.");

        Assert.IsTrue(grid.HasMove());
    }

    [TestMethod]
    public void AValidChain_ScoresAndRefills()
    {
        var (game, output, _) = Start(Mining());
        var grid = game.Level!.Grid!;
        var chain = FindChain(grid) ?? throw new AssertInconclusiveException("No chain on this board.");
        output.Clear();

        game.Handle(["OnChainMsg", Chain(chain)]);

        Assert.AreEqual(0, game.Level.Desyncs);
        Assert.AreEqual(MatchThreeLevel.ChainScore, game.Level.Score);
        Assert.AreEqual(1, output.Events(MatchThreeEvents.ScoreIncrement).Count());

        var time = output.Events(MatchThreeEvents.TimerIncrement).Single();
        Assert.AreEqual(3.0, (double)time[1], "One second per tile on this level.");

        for (var y = 1; y < grid.Height - 1; y++)
            for (var x = 1; x < grid.Width; x++)
                Assert.IsNotNull(grid.At(x, y), $"{x},{y} wasn't refilled.");
    }

    [TestMethod]
    public void ABadChain_RebuildsTheBoard_AndTooManyStopTheGame()
    {
        var (game, output, _) = Start(Mining());
        output.Clear();

        // Three cells in a row that aren't one kind (or not touching): out of step, or a forged message.
        game.Handle(["OnChainMsg", "1,1,3,3,5,5,"]);

        Assert.AreEqual(1, game.Level!.Desyncs);
        Assert.AreEqual(1, output.Events(MatchThreeEvents.LevelReshuffle).Count(), "The client gets a fresh board.");
        Assert.AreEqual(0, game.Level.Score);

        for (var i = 0; i < MatchThreeGame.MaxDesyncsPerLevel; i++)
            game.Handle(["OnChainMsg", "1,1,3,3,5,5,"]);

        Assert.AreEqual(MatchThreeResult.Stopped, game.Result);
        Assert.AreEqual(1, output.Named("OnGameStoppedMsg").Count());
    }

    [TestMethod]
    public void TimeRunsOut_AndPausingStopsTheClock()
    {
        var (game, output, clock) = Start(Mining());
        output.Clear();

        game.Handle(["FRServer_Pause", "true"]);
        Assert.AreEqual("OnPauseMsg\tfalse", MatchThreeGame.Format("OnPauseMsg", output.Named("OnPauseMsg").Single().Args));

        clock.Now += 600_000;
        game.Tick();
        Assert.IsTrue(game.Level!.IsInProgress, "Ten paused minutes don't count.");

        // Chains don't count while paused, either.
        var chain = FindChain(game.Level.Grid!)!;
        game.Handle(["OnChainMsg", Chain(chain)]);
        Assert.AreEqual(0, game.Level.Score);

        game.Handle(["FRServer_Pause", "false"]);
        clock.Now += 59_000;
        game.Tick();
        Assert.IsTrue(game.Level.IsInProgress);

        clock.Now += 2_000;
        game.Tick();

        Assert.IsFalse(game.Level.IsInProgress);
        Assert.AreEqual("OnStopLevel\t1\tfalse", MatchThreeGame.Format("OnStopLevel", output.Named("OnStopLevel").Single().Args));
        Assert.AreEqual(MatchThreeResult.Lost, game.Result, "No copper ore was collected.");

        // Asking for the next level anyway changes nothing.
        game.Handle(["OnNextLevelMsg"]);
        Assert.AreEqual(0, output.Named("OnNextLevelMsg").Count());
    }

    [TestMethod]
    public void OreFallingOut_FillsTheQuota_AndWinsTheGame()
    {
        // A level where ore comes often, so a short scripted game reaches the quota.
        var definition = Mining();
        definition.Levels[0].SpawnChances.Collectable = 60;

        var (game, output, _) = Start(definition, seed: 11);
        var client = new ScriptedClient(game, output);

        for (var turn = 0; turn < 400 && game.Level!.IsInProgress; turn++)
        {
            Assert.IsTrue(client.PlayTurn(), $"No move on turn {turn}.");
            game.Tick();
        }

        var level = game.Level!;
        Assert.AreEqual(0, level.Desyncs);
        Assert.IsFalse(level.IsInProgress, "The quota ends the level (StopAtQuota).");
        Assert.IsTrue(level.Succeeded);
        // Two pieces can fall out in one move, so the last move may take the count past the quota.
        var copper = level.Collectables[13].Collected;
        Assert.IsTrue(copper >= 5, $"{copper} copper collected.");
        Assert.AreEqual(copper, output.Named("OnQuotaUpdateMsg").Count());
        Assert.AreEqual("OnStopLevel\t1\ttrue", MatchThreeGame.Format("OnStopLevel", output.Named("OnStopLevel").Single().Args));

        game.Handle(["OnNextLevelMsg"]);
        Assert.AreEqual(MatchThreeResult.Won, game.Result);
        Assert.AreEqual(1, output.Named("OnGameWonMsg").Count());
    }

    [TestMethod]
    public void OreThatIsntFallingOut_IsRefused()
    {
        var (game, _, _) = Start(Mining());

        game.Handle(["OnLevelEventMsg", "50", "3", "3"]);

        Assert.AreEqual(1, game.Level!.Desyncs);
        Assert.AreEqual(0, game.Level.Collectables[13].Collected);
    }

    [TestMethod]
    public void APowerUpThePlayerDoesntHave_IsRefused()
    {
        var (game, output, _) = Start(Mining());
        new ScriptedClient(game, output).AllIdle();
        output.Clear();

        // Dynamite "clicked" on a rock: SOE's server would have blown it up.
        var rock = game.Level!.Grid!.Tiles.First(tile => tile.Category == MatchThreeTiles.Selectable);
        game.Handle(["OnActionMsg", MatchThreeTiles.PowerupDynamite.ToString(), $"{rock.X},{rock.Y},"]);

        Assert.AreEqual(1, game.Level.Desyncs);
        Assert.AreEqual(1, output.Events(MatchThreeEvents.LevelReshuffle).Count(), "Refused, and the client's board rebuilt.");
        Assert.AreEqual(0, game.Level.Score);
    }

    [TestMethod]
    public void DebugMessages_AreIgnored()
    {
        var (game, _, _) = Start(Mining());

        game.Handle(["OnDebugMsg", "SkipLevel"]);

        Assert.IsTrue(game.Level!.IsInProgress);
        Assert.AreEqual(MatchThreeResult.None, game.Result);
    }

    [TestMethod]
    public void AnUnaskedQueueAccept_IsIgnored()
    {
        var (game, _, _) = Start(Mining());
        var grid = game.Level!.Grid!;

        // A client accepting a reset that was never sent would empty the server's queue in SOE's server.
        game.Handle(["OnLevelEventMsg", "60"]);

        for (var i = 0; i < 30; i++)
        {
            var chain = FindChain(grid);
            if (chain is null)
                break;

            game.Handle(["OnChainMsg", Chain(chain)]);
        }

        Assert.AreEqual(0, game.Level.Desyncs);
    }

    [TestMethod]
    public void TileQueueRejects_AreResentAFewTimesOnly()
    {
        var (game, output, _) = Start(Mining());
        output.Clear();

        for (var i = 0; i < MatchThreeGrid.MaxTileQueueRejects * 3; i++)
            game.Handle(["OnLevelEventMsg", "61"]);

        Assert.IsTrue(output.Named("OnTileQueueMsg").Count() <= MatchThreeGrid.MaxTileQueueRejects);
    }

    [TestMethod]
    public void UnreadableLists_AreRefused()
    {
        Assert.IsFalse(MatchThreeGame.TryParseList("1,2,x,", out _));
        Assert.IsFalse(MatchThreeGame.TryParseList(string.Join(',', Enumerable.Repeat("1", MatchThreeGame.MaxListLength + 1)), out _));

        Assert.IsTrue(MatchThreeGame.TryParseList("1,2,3,", out var numbers));
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, numbers);
    }

    [TestMethod]
    public void Format_MatchesTheFlash()
    {
        Assert.AreEqual("OnLevelEventMsg\t25\t6.5\t1", MatchThreeGame.Format("OnLevelEventMsg", [25, 6.5, 1]));
        Assert.AreEqual("OnLevelEventMsg\t25\t6\t1", MatchThreeGame.Format("OnLevelEventMsg", [25, 6.0, 1]));
        Assert.AreEqual("OnStopLevel\t1\ttrue", MatchThreeGame.Format("OnStopLevel", [1, true]));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void LongRandomPlay_KeepsTheBoardWhole(int seed)
    {
        var definition = Mining();
        definition.Levels[0].DurationSeconds = 100_000;
        definition.Levels[0].StopAtQuota = false;

        var (game, output, _) = Start(definition, seed);
        var client = new ScriptedClient(game, output);
        var grid = game.Level!.Grid!;

        for (var turn = 0; turn < 600; turn++)
        {
            Assert.IsTrue(client.PlayTurn(), $"No move on turn {turn}, and the board wasn't reshuffled.");

            for (var y = 1; y < grid.Height - 1; y++)
                for (var x = 1; x < grid.Width; x++)
                    Assert.IsNotNull(grid.At(x, y), $"Turn {turn}: {x},{y} is empty.");
        }

        Assert.AreEqual(0, game.Level.Desyncs);
        Assert.IsTrue(game.Level.Wave > 1, "Waves come every hundred tiles.");
        Assert.IsTrue(output.Named("OnRandomQueueMsg").Any());
    }

    private static int[] Range(int first, int last) => [.. Enumerable.Range(first, last - first + 1)];

    /// <summary>
    /// What each game's Flash can draw, read from its .gfx: the frame labels of its <c>Selectable_Combined</c>,
    /// <c>Collectible_Combined_Reg</c> and <c>_Bonus</c> clips, under the names its <c>TileGenerator</c> gives each type.
    /// Farming and Mail Sorting have no ore or gem clips at all, and Forging no gem clip. Then the quota bins on screen
    /// (<c>mBin1</c> to <c>mBin5</c>; Forging has two).
    /// </summary>
    private static readonly Dictionary<string, (int[] Selectables, int[] Collectables, int[] Bonuses, int Bins)> Art = new()
    {
        ["mining"] = (Range(1, 5), Range(0, 14), Range(0, 9), 5),
        ["farming"] = (Range(1, 16), [], [], 5),
        ["mail"] = (Range(1, 8), [], [], 5),
        ["forging"] = (Range(1, 5), Range(0, 26), [], 2)
    };

    /// <summary>Every game's <c>PowerUp_Combined</c> draws more time, dynamite, wild tiles, destruct and shuffle; no score multipliers.</summary>
    private static readonly int[] PowerupArt = [0, 50, 51, 52, 53];

    [TestMethod]
    [DataRow("mining", 1, 1)]
    [DataRow("farming", 60, 3)]
    [DataRow("forging", 62, 3)]
    [DataRow("mail", 12, 3)]
    public void ShippedGames_HaveTheirNumbersAndLevels(string name, int number, int levels)
    {
        var game = Game(name);

        Assert.AreEqual(number, game.GameNumber, "The Flash's ConfigServer.GAME_ID.");
        Assert.AreEqual(levels, game.Levels.Count);
    }

    [TestMethod]
    public void ShippedLevels_OnlyUseWhatTheirGameCanDraw()
    {
        Assert.IsTrue(MatchThreeDefinitions.TryLoad(FindShipped(), NullLogger.Instance, out var definitions));

        foreach (var game in definitions.Games)
        {
            Assert.IsTrue(Art.TryGetValue(game.Name, out var art), $"{game.Name} isn't a match-three Flash the client has.");

            foreach (var (level, number) in game.Levels.Select((level, i) => (level, i + 1)))
            {
                var where = $"{game.Name} level {number}";

                CollectionAssert.IsSubsetOf(level.Selectables.Select(spawn => spawn.Type).ToList(), art.Selectables, where);
                CollectionAssert.IsSubsetOf(level.Collectables.Select(spawn => spawn.Type).ToList(), art.Collectables, where);
                CollectionAssert.IsSubsetOf(level.Bonuses.Select(spawn => spawn.Type).ToList(), art.Bonuses, where);
                CollectionAssert.IsSubsetOf(level.Powerups.Select(spawn => spawn.Type).ToList(), PowerupArt, where);

                var bins = level.Selectables.Concat(level.Collectables).Count(spawn => spawn.Quota > 0 || spawn.RequiredPerReward > 0) + level.Bonuses.Count;
                Assert.IsTrue(bins <= art.Bins, $"{where}: {bins} quota bins, the screen has {art.Bins}.");

                // Ore and the more-time power-up leave through the drop zone.
                if (level.Collectables.Count > 0 || level.Powerups.Any(spawn => spawn.Type < 50))
                    Assert.IsTrue(level.Grid.Any(row => row.Contains(MatchThreeTiles.ShapeDropZone)), $"{where} needs a drop zone.");
            }
        }
    }

    [TestMethod]
    public void MiningsCopperBin_SaysCopper()
    {
        // The bin's label is the client's "miningValue<value>": 612 says Gold, 613 and 614 say Copper.
        foreach (var level in Mining().Levels)
            foreach (var ore in level.Collectables.Where(spawn => spawn.Quota > 0))
                CollectionAssert.Contains(new[] { 13, 14 }, ore.Type);
    }

    [TestMethod]
    public void ForgingsMold_FillsExactly()
    {
        // MoldId 14 is the bow's mold (N is weapon piece N - 1, the bow is 13), and the meter moves 100 / total per piece.
        foreach (var level in Game("forging").Levels)
        {
            var total = level.Collectables.Sum(spawn => spawn.Quota) + level.Selectables.Sum(spawn => spawn.Quota);

            Assert.AreEqual(14, level.MoldId);
            Assert.IsTrue(level.Collectables.All(spawn => spawn.Type == 13), "Bow pieces for the bow's mold.");
            Assert.AreEqual(0, 100 % total, $"A total quota of {total} leaves the mold part-filled.");
        }
    }

    [TestMethod]
    public void Problems_NeedAGameNumberUsedOnce()
    {
        var definitions = new MatchThreeDefinitions { Games = [Game("farming"), Game("mail")] };
        Assert.AreEqual(0, definitions.Problems().Count());

        definitions.Games[1].GameNumber = 0;
        Assert.IsTrue(definitions.Problems().Any(problem => problem.Contains("GameNumber")));

        definitions.Games[1].GameNumber = definitions.Games[0].GameNumber;
        Assert.IsTrue(definitions.Problems().Any(problem => problem.Contains("used twice")));
    }

    [TestMethod]
    [DataRow("farming", 1)]
    [DataRow("farming", 2)]
    [DataRow("farming", 3)]
    [DataRow("forging", 1)]
    [DataRow("forging", 2)]
    [DataRow("forging", 3)]
    [DataRow("mail", 1)]
    [DataRow("mail", 2)]
    [DataRow("mail", 3)]
    public void EveryLevel_StartsWithAWholeBoard(string name, int number)
    {
        var definition = OnlyLevel(name, number);
        var level = definition.Levels[0];
        var (game, output, _) = Start(definition);
        var grid = game.Level!.Grid!;

        Assert.AreEqual(level.Selectables.Count + level.Collectables.Count + level.Bonuses.Count, output.Named("OnTileInfoMsg").Count());
        Assert.AreEqual(level.Selectables.Concat(level.Collectables).Count(spawn => spawn.Quota > 0), output.Named("OnQuotaInitMsg").Count());
        Assert.AreEqual(level.MoldId, output.Events(MatchThreeEvents.MeterInit).Single()[1]);
        Assert.AreEqual(level.Powerups.Count, output.Named("OnPowerupInitMsg").Count());

        // The bins come in tile-value order, one each.
        var bins = output.Named("OnQuotaInitMsg").Select(message => (int)message.Args[0]).ToList();
        CollectionAssert.AreEqual(Enumerable.Range(0, bins.Count).ToList(), bins);

        for (var y = 1; y < grid.Height - 1; y++)
            for (var x = 1; x < grid.Width; x++)
                Assert.IsTrue(grid.At(x, y)?.Category >= MatchThreeTiles.Visible, $"{x},{y} is empty.");

        Assert.IsTrue(grid.HasMove());
        Assert.AreEqual("OnActiveMsg", output.Messages[^1].Name);
    }

    [TestMethod]
    [DataRow("farming", 3)]
    [DataRow("forging", 5)]
    [DataRow("mail", 7)]
    public void AWholeGame_IsWonLevelByLevel_InLockstep(string name, int seed)
    {
        // The shipped quotas, with time that never runs out: each level ends when its bins are full.
        var definition = Game(name);
        foreach (var level in definition.Levels)
            level.DurationSeconds = 100_000;

        var (game, output, _) = Start(definition, seed);
        var from = 0;

        for (var number = 1; ; number++)
        {
            var client = new ScriptedClient(game, output, from);

            for (var turn = 0; turn < 3_000 && game.Level!.IsInProgress; turn++)
            {
                Assert.IsTrue(client.PlayTurn(), $"Level {number}: no move on turn {turn}.");
                game.Tick();
            }

            var level = game.Level!;
            Assert.AreEqual(number, level.Number);
            Assert.AreEqual(0, level.Desyncs, $"Level {number} fell out of step.");
            Assert.IsFalse(level.IsInProgress, $"Level {number} never filled its bins.");
            Assert.IsTrue(level.Succeeded, $"Level {number} wasn't won.");

            foreach (var spawn in level.Selectables.Values.Concat(level.Collectables.Values).Where(spawn => spawn.Definition.Quota > 0))
                Assert.IsTrue(spawn.Collected >= spawn.Definition.Quota, $"Level {number}: bin {spawn.Value} isn't full.");

            // The client's end-of-level animation asks for the next level.
            game.Handle(["OnNextLevelMsg"]);
            if (number == definition.Levels.Count)
                break;

            Assert.AreEqual(number + 1, (int)output.Named("OnNextLevelMsg").Last().Args[0]);
            from = output.Messages.Count;
            game.Handle(["OnStartLevelMsg", "0"]);
        }

        Assert.AreEqual(MatchThreeResult.Won, game.Result);
        Assert.AreEqual(1, output.Named("OnGameWonMsg").Count());
        Assert.AreEqual(definition.Levels.Count, output.Named("OnStopLevel").Count());

        var summary = game.Summarize();
        Assert.AreEqual(name, summary.Name);
        Assert.AreEqual(definition.GameNumber, summary.GameNumber);
        Assert.AreEqual(MatchThreeResult.Won, summary.Result);
        Assert.AreEqual(definition.Levels.Count, summary.LevelsWon);
        Assert.AreEqual(game.TotalScore, summary.Score);
        Assert.AreEqual(0, summary.Desyncs);
    }

    [TestMethod]
    [DataRow("farming", 1)]
    [DataRow("farming", 2)]
    [DataRow("farming", 3)]
    [DataRow("forging", 1)]
    [DataRow("forging", 2)]
    [DataRow("forging", 3)]
    [DataRow("mail", 1)]
    [DataRow("mail", 2)]
    [DataRow("mail", 3)]
    public void LongRandomPlay_KeepsEveryLevelsBoardWhole(string name, int number)
    {
        var definition = OnlyLevel(name, number);
        definition.Levels[0].DurationSeconds = 100_000;
        definition.Levels[0].StopAtQuota = false;

        var (game, output, _) = Start(definition, seed: number);
        var client = new ScriptedClient(game, output);
        var grid = game.Level!.Grid!;

        for (var turn = 0; turn < 400; turn++)
        {
            Assert.IsTrue(client.PlayTurn(), $"No move on turn {turn}, and the board wasn't reshuffled.");

            for (var y = 1; y < grid.Height - 1; y++)
                for (var x = 1; x < grid.Width; x++)
                    Assert.IsNotNull(grid.At(x, y), $"Turn {turn}: {x},{y} is empty.");
        }

        Assert.AreEqual(0, game.Level.Desyncs);
        Assert.IsTrue(game.Level.Wave > 1, "Waves come every hundred tiles.");
    }

    [TestMethod]
    public void Summary_OfAGameLeftMidLevel()
    {
        var (game, _, clock) = Start(Mining());
        var chain = FindChain(game.Level!.Grid!) ?? throw new AssertInconclusiveException("No chain on this board.");

        game.Handle(["OnChainMsg", Chain(chain)]);
        clock.Now += 12_500;

        var summary = game.Summarize();
        Assert.AreEqual(new MatchThreeSummary("mining", 1, MatchThreeResult.None, Level: 1, Levels: 1, LevelsWon: 0,
            Score: MatchThreeLevel.ChainScore, Desyncs: 0, Seconds: 12.5), summary);
    }
}
