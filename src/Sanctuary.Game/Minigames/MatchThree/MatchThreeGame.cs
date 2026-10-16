using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sanctuary.Game.Minigames.MatchThree;

public enum MatchThreeResult
{
    None,
    Won,
    Lost,
    Stopped
}

/// <summary>
/// The server side of one match-three game (a port of SOE's <c>GameServer</c> and its <c>SoeGameFramework</c>): takes
/// the Flash's messages, runs the levels in order and says when each starts and ends.
/// </summary>
/// <remarks>
/// Flow: the Flash connects and the server says it's ready; the Flash shows its tutorial and asks to start; the server
/// names the next level; the Flash builds its screen and asks to start it; the server sends the board and starts the
/// clock (paused while the client counts down). When time runs out, or the quotas are met on a stop-at-quota level,
/// the server stops the level, and the client asks for the next one if it was won.
///
/// Where SOE's server trusted the client, this one checks: a level is only passed if it was won, a power-up is only used
/// if the player has it, debug messages are ignored, and nothing counts while the game is paused. A client whose board
/// falls out of step gets a fresh one instead of losing the game, a few times per level.
/// </remarks>
public sealed class MatchThreeGame
{
    public const int MaxDesyncsPerLevel = 5;

    /// <summary>The longest list of numbers read from one message; a 14 x 14 board check is 588.</summary>
    public const int MaxListLength = 1024;

    private readonly MatchThreeGameDefinition _definition;
    private readonly IMatchThreeOutput _output;
    private readonly Func<long> _clock;
    private readonly Random _random;
    private readonly ILogger _logger;

    private long _lastTick;
    private bool _serverReadySent;

    public MatchThreeGame(MatchThreeGameDefinition definition, IMatchThreeOutput output, Func<long> clock, Random random, ILogger? logger = null)
    {
        _definition = definition;
        _output = output;
        _clock = clock;
        _random = random;
        _logger = logger ?? NullLogger.Instance;
        _lastTick = clock();
    }

    public string Name => _definition.Name;

    /// <summary>The level being played or just finished, 1 first.</summary>
    public MatchThreeLevel? Level { get; private set; }

    public bool IsInProgress { get; private set; }
    public MatchThreeResult Result { get; private set; }

    /// <summary>Points from every level played this game.</summary>
    public long TotalScore { get; private set; }

    private bool CanPlay => IsInProgress && Level is { IsInProgress: true, IsPaused: false, Grid: not null };

    /// <summary>Handles one message from the Flash (its fields, name first). False if it isn't a game message.</summary>
    public bool Handle(IReadOnlyList<string> message)
    {
        if (message.Count == 0)
            return false;

        string Arg(int index) => index < message.Count ? message[index] : string.Empty;

        switch (message[0])
        {
            case "FRServer_Connect":
            case "OnConnectMsg":
                OnConnect();
                return true;

            case "OnStartGameMsg":
                StartGame();
                return true;

            case "OnStartLevelMsg":
                StartLevel();
                return true;

            case "OnNextLevelMsg":
                OnNextLevel();
                return true;

            case "FRServer_Pause":
            case "OnPauseMsg":
                OnPause(Arg(1) == "true");
                return true;

            case "OnChainMsg":
                if (CanPlay && TryParseList(Arg(1), out var chain))
                {
                    if (!Level!.Grid!.ValidateAndRemoveChain(chain))
                        Desync("a chain that isn't on the board");
                }
                return true;

            case "OnActionMsg":
                if (CanPlay)
                    OnAction(Arg(1), Arg(2));
                return true;

            case "OnIdleMsg":
                if (Level?.Grid is { } grid && TryParseList(Arg(1), out var idle))
                    grid.NowIdle(idle);
                return true;

            case "OnVerifyMsg":
                if (CanPlay && TryParseList(Arg(1), out var board) && !Level!.Grid!.CheckGrid(board))
                    Desync("the client's board differs");
                return true;

            case "OnLevelEventMsg":
                if (CanPlay)
                    OnLevelEvent(Arg(1), Arg(2), Arg(3));
                return true;

            // SOE's debug "skip level" is not for players.
            case "OnDebugMsg":
                return true;
        }

        return false;
    }

    /// <summary>Runs the clock: ends a level when its time is up, and expires score multipliers.</summary>
    public void Tick()
    {
        var now = _clock();
        var delta = now - _lastTick;
        _lastTick = now;

        if (!IsInProgress || Level is not { IsInProgress: true } level || level.IsPaused)
            return;

        level.Tick(delta);

        if (level.CheckDone())
            StopLevel();
    }

    private void OnConnect()
    {
        if (_serverReadySent)
            return;

        _serverReadySent = true;
        _output.Send("OnActiveMsg", false);
        _output.Send("OnServerReadyMsg");
    }

    private void StartGame()
    {
        if (IsInProgress)
            return;

        Level = null;
        TotalScore = 0;
        Result = MatchThreeResult.None;
        IsInProgress = true;

        NextLevel(1);
    }

    private void NextLevel(int number)
    {
        if (number > _definition.Levels.Count)
        {
            IsInProgress = false;
            Result = MatchThreeResult.Won;
            _output.Send("OnGameWonMsg");
            return;
        }

        Level = new MatchThreeLevel(number, _definition.Levels[number - 1], _output, _clock, _random);
        _output.Send("OnNextLevelMsg", number, 0, 0);
    }

    private void StartLevel()
    {
        if (!IsInProgress || Level is not { IsStarted: false } level)
            return;

        level.Start();
        _output.Send("OnActiveMsg", true);
    }

    private void StopLevel()
    {
        var level = Level!;
        if (!level.Stop())
            return;

        TotalScore += level.Score;

        _output.Send("OnActiveMsg", false);
        _output.Send("OnStopLevel", level.Number, level.Succeeded);

        // The client shows the loss itself and asks for nothing more.
        if (!level.Succeeded)
        {
            IsInProgress = false;
            Result = MatchThreeResult.Lost;
        }
    }

    /// <summary>The client finished its end-of-level animation and wants the next level.</summary>
    private void OnNextLevel()
    {
        if (!IsInProgress || Level is not { IsDone: true } level)
            return;

        if (!level.Succeeded)
        {
            IsInProgress = false;
            Result = MatchThreeResult.Lost;
            _output.Send("OnGameLostMsg");
            return;
        }

        NextLevel(level.Number + 1);
    }

    private void OnPause(bool pause)
    {
        if (!IsInProgress || Level is not { IsInProgress: true } level)
            return;

        // The answer is the new running state: false while paused, true when running again.
        if (pause)
        {
            if (level.Pause())
                _output.Send("OnPauseMsg", false);
        }
        else if (level.Resume())
            _output.Send("OnPauseMsg", true);
    }

    private void OnAction(string indexText, string listText)
    {
        var grid = Level!.Grid!;

        if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            || !TryParseList(listText, out var positions) || positions.Count < 2)
        {
            Desync("an unreadable power-up");
            return;
        }

        // The list starts with the power-up the player clicked: it must be on the board and be that power-up.
        if (grid.At(positions[0], positions[1]) is not { } clicked || clicked.Value != index
            || !Level.Powerups.TryGetValue(index, out var powerup))
        {
            Desync($"power-up {index} that isn't on the board");
            return;
        }

        var power = powerup.Definition.Power;

        var played = index switch
        {
            MatchThreeTiles.PowerupDynamite => grid.StartShatter(positions, power),
            MatchThreeTiles.PowerupWildTile => grid.StartWildTiles(positions, power),
            MatchThreeTiles.PowerupDestruct => grid.StartDestruction(positions, power),
            MatchThreeTiles.PowerupShuffle => grid.StartShuffle(positions, power),
            _ => false
        };

        if (!played)
            Desync($"power-up {index} played differently");
    }

    private void OnLevelEvent(string typeText, string first, string second)
    {
        var grid = Level!.Grid!;

        if (!int.TryParse(typeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type))
            return;

        int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x);
        int.TryParse(second, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y);

        switch (type)
        {
            case MatchThreeEvents.ClientFall:
                if (!grid.FallOffBottom(x, y))
                    Desync($"ore falling out at {x},{y} that the server doesn't have there");
                break;

            case MatchThreeEvents.ClientConvert:
                if (!grid.Convert(x, y))
                    Desync($"a conversion at {x},{y}");
                break;

            case MatchThreeEvents.ClientTileQueueAccept:
                grid.TileQueueAccept();
                break;

            case MatchThreeEvents.ClientTileQueueReject:
                grid.TileQueueReject();
                break;
        }
    }

    /// <summary>The client's board is out of step: rebuild it, or stop a game that keeps falling out.</summary>
    private void Desync(string reason)
    {
        var level = Level!;
        level.Desyncs++;

        _logger.LogWarning("Match three ({game}, level {level}): {reason}; desync {count} of {max}.",
            Name, level.Number, reason, level.Desyncs, MaxDesyncsPerLevel);

        if (level.Desyncs > MaxDesyncsPerLevel)
        {
            level.Stop();
            IsInProgress = false;
            Result = MatchThreeResult.Stopped;
            _output.Send("OnGameStoppedMsg");
            return;
        }

        level.Grid!.LevelReshuffle();
    }

    /// <summary>Comma-separated numbers; the client ends most lists with a comma.</summary>
    public static bool TryParseList(string text, out List<int> numbers)
    {
        numbers = [];

        foreach (var part in text.Split(','))
        {
            if (part.Length == 0)
                continue;

            if (numbers.Count >= MaxListLength || !int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                numbers = [];
                return false;
            }

            numbers.Add(number);
        }

        return true;
    }

    /// <summary>A message as the Flash reads it: tab-separated, booleans as "true" and "false", numbers invariant.</summary>
    public static string Format(string name, IReadOnlyList<object> args)
    {
        var parts = new string[args.Count + 1];
        parts[0] = name;

        for (var i = 0; i < args.Count; i++)
        {
            parts[i + 1] = args[i] switch
            {
                bool value => value ? "true" : "false",
                double value => value.ToString("0.###", CultureInfo.InvariantCulture),
                IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
                var value => value?.ToString() ?? string.Empty
            };
        }

        return string.Join('\t', parts);
    }
}
