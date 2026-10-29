using System;
using System.Collections.Generic;
using System.Globalization;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sanctuary.Game.Minigames.Mimic;

/// <summary>Messages to the Flash: the name, then its fields.</summary>
public interface IMimicOutput
{
    void Send(string name, params object[] args);
}

public enum MimicResult
{
    None,
    Won,
    Lost,

    /// <summary>The client said it won, but the game couldn't have been played that way; nothing is paid.</summary>
    Refused
}

/// <summary>
/// The server side of one Mimic game (Cooking, and later Smelting, Sword and Music): sends the recipe's data when the
/// Flash asks for it, and takes the result the Flash reports.
/// </summary>
/// <remarks>
/// The Mimic Flash runs its own small server inside itself (<c>GameServer</c>, on <c>SoeGameFramework</c>, over a
/// local connection), which plays the rounds, so the real server never sees a round start or stop. It only supplies
/// the two data files that local server would have read, row by row (<c>OnGameDataMsg</c>, <c>OnPhaseDataMsg</c>,
/// <c>OnDataComplete</c>), and hears the score and the result. See docs/research/mimic.md.
///
/// SOE trusted the result. This server refuses a win reported before the recipe was sent, faster than the phases
/// could be played, or, for a game that reports its rounds, with rounds missing; a score above the game's maximum is
/// capped; and only the first result of a game counts.
/// </remarks>
public sealed class MimicGame
{
    // GameData.STAT_*: what FRServer_StatUpdate counts.
    public const int StatGameTime = 4;

    private readonly MimicGameDefinition _game;
    private readonly MimicRecipeDefinition _recipe;
    private readonly IMimicOutput _output;
    private readonly Func<long> _clock;
    private readonly ILogger _logger;

    private long _dataSentAt;
    private int _roundTransitions;
    private int _completedLevels;

    public MimicGame(MimicGameDefinition game, MimicRecipeDefinition recipe, IMimicOutput output, Func<long> clock, ILogger? logger = null)
    {
        _game = game;
        _recipe = recipe;
        _output = output;
        _clock = clock;
        _logger = logger ?? NullLogger.Instance;
    }

    public MimicGameDefinition Game => _game;

    public MimicRecipeDefinition Recipe => _recipe;

    /// <summary>What a win pays: the recipe's coins, and its stars on the game's job.</summary>
    public MinigameReward Reward => new(_recipe.Coins, _game.RewardProfileId, _recipe.Experience);

    public bool DataSent { get; private set; }

    /// <summary>Rounds the Flash said it finished, by whichever of its two signals it uses.</summary>
    public int RoundsFinished => Math.Max(_roundTransitions, _completedLevels);

    /// <summary>The running total the Flash last reported, then the final score, capped.</summary>
    public long Score { get; private set; }

    /// <summary>The game's play time in seconds, as the Flash reported it at the end; -1 until then.</summary>
    public int ReportedSeconds { get; private set; } = -1;

    public MimicResult Result { get; private set; }

    /// <summary>Why a win was refused, for the log.</summary>
    public string? RefusedBecause { get; private set; }

    /// <summary>Handles one message from the Flash (its fields, name first). False if it isn't a Mimic message.</summary>
    public bool Handle(IReadOnlyList<string> message)
    {
        if (message.Count == 0)
            return false;

        string Arg(int index) => index < message.Count ? message[index] : string.Empty;

        switch (message[0])
        {
            // The Flash's connection to the game (its external network says OnConnectMsg); nothing to answer.
            case "OnConnectMsg":
            case "FRServer_Connect":
                return true;

            case "FRServer_ReadyForGameData":
                SendData();
                return true;

            // The round's scene is ready (false) or the round was won (true). Cooking and Smelting only.
            case "FRServer_RoundTransitionPause":
                if (DataSent && Result == MimicResult.None && IsTrue(Arg(1)))
                    _roundTransitions = Math.Min(_roundTransitions + 1, _recipe.Phases.Count);
                return true;

            // The base client's end of a round (Cooking replaces it and never sends this).
            case "FRServer_CompleteLevel":
                if (DataSent && Result == MimicResult.None)
                    _completedLevels = Math.Min(_completedLevels + 1, _recipe.Phases.Count);
                return true;

            case "FRServer_StatUpdate":
                if (TryParseNumber(Arg(1), out var stage) && stage == -1 &&
                    TryParseNumber(Arg(2), out var stat) && stat == StatGameTime &&
                    TryParseNumber(Arg(3), out var seconds))
                {
                    ReportedSeconds = (int)Math.Clamp(seconds, 0, int.MaxValue);
                }
                return true;

            case "FRServer_ScoreSet":
                if (Result == MimicResult.None && TryParseNumber(Arg(1), out var total))
                    Score = CapScore(total);
                return true;

            case "FRServer_GameWon":
                Finish(won: true, Arg(1));
                return true;

            case "FRServer_GameLost":
                Finish(won: false, Arg(1));
                return true;

            // Pauses, the level the client stopped, and its own "game over" after the result: nothing to do.
            case "FRServer_Pause":
            case "FRServer_StopLevel":
            case "FRServer_GameOver":
                return true;
        }

        return false;
    }

    /// <summary>
    /// The recipe as the two data files: the game's id (which row of <c>MimicMiniGameData.txt</c> to use; the Flash
    /// asks for nothing else of it), each phase row, any custom rows, then "that's all".
    /// </summary>
    private void SendData()
    {
        if (DataSent)
            return;

        DataSent = true;
        _dataSentAt = _clock();

        _output.Send("OnGameDataMsg", _recipe.GameId);

        for (var sequence = 1; sequence <= _recipe.Phases.Count; sequence++)
            _output.Send("OnPhaseDataMsg", _recipe.PhaseRow(sequence));

        foreach (var row in _recipe.CustomData)
            _output.Send("OnCustomDataMsg", row);

        _output.Send("OnDataComplete");
    }

    private void Finish(bool won, string scoreText)
    {
        if (Result != MimicResult.None || !DataSent)
            return;

        if (TryParseNumber(scoreText, out var score))
            Score = CapScore(score);

        if (won && WhyRefuse() is { } reason)
        {
            Result = MimicResult.Refused;
            RefusedBecause = reason;
            _logger.LogWarning("Mimic: refused a win of {recipe}: {reason}.", _recipe.Name, reason);
        }
        else
            Result = won ? MimicResult.Won : MimicResult.Lost;

        // The Flash ends its game on either; it then sends FRServer_GameOver.
        _output.Send(Result == MimicResult.Won ? "OnGameWonMsg" : "OnGameLostMsg");
    }

    private string? WhyRefuse()
    {
        var phases = _recipe.Phases.Count;

        if (_game.ReportsRounds && RoundsFinished < phases)
            return $"{RoundsFinished} of {phases} rounds finished";

        var seconds = (_clock() - _dataSentAt) / 1000.0;
        var fewest = phases * _game.MinSecondsPerPhase;

        if (seconds < fewest)
            return $"won in {seconds:0.#} s, under the {fewest} s its {phases} rounds take";

        return null;
    }

    private long CapScore(double score) => (long)Math.Clamp(Math.Round(score), 0, _game.MaxScore);

    private static bool IsTrue(string text) => text.Trim().Trim('"', ')').Trim() is "true" or "1";

    /// <summary>
    /// A number from the Flash. Some of its messages are built without their closing quote, so the client may pass a
    /// trailing <c>"</c> or <c>)</c> along.
    /// </summary>
    public static bool TryParseNumber(string text, out double value)
    {
        var trimmed = text.Trim().Trim('"', ')').Trim();

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }
}
