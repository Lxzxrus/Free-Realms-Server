using System.Collections.Generic;

using Sanctuary.Game.Minigames.MatchThree;

namespace Sanctuary.Game.Minigames;

/// <summary>What the server knows about a Flash minigame it can launch.</summary>
/// <param name="ActivityId">The Games menu entry (ClientActivityDefinitions.json).</param>
/// <param name="GameId">The client's MiniGameData.txt row.</param>
/// <param name="Type">The client's MiniGameTypeData.txt type, e.g. 8 for match three.</param>
/// <param name="Swf">The Flash file the client loads, from its packs or the asset server.</param>
/// <param name="MatchThreeGame">The game in <c>Resources/MatchThree.json</c> the server plays, if it's a match three.</param>
/// <param name="MimicGame">The game in <c>Resources/Mimic.json</c> whose recipe the activity plays, if it's a Mimic game.</param>
public sealed record FlashMinigameDefinition(int ActivityId, int GameId, int Type, string Swf, string? MatchThreeGame = null,
    string? MimicGame = null);

/// <summary>One Flash minigame a player has open: from the start screen to closing it.</summary>
public sealed class FlashMinigameSession
{
    /// <summary>At most this many different message names are logged per session, so a client can't flood the log.</summary>
    public const int MaxLoggedMessageNames = 40;

    private readonly HashSet<string> _loggedMessageNames = [];

    public FlashMinigameSession(int stateId, FlashMinigameDefinition definition)
    {
        StateId = stateId;
        Definition = definition;
    }

    /// <summary>The client's minigame session id: the state id every minigame packet for this game carries.</summary>
    public int StateId { get; }

    public FlashMinigameDefinition Definition { get; }

    public bool Started { get; set; }

    /// <summary>The server's side of the board, for a match-three game, from Go on.</summary>
    public MatchThree.MatchThreeGame? MatchThree { get; set; }

    /// <summary>The result already logged, so each is logged once.</summary>
    public MatchThree.MatchThreeResult LoggedResult { get; set; }

    /// <summary>True once the match-three game's reward has been handed over to be paid.</summary>
    public bool MatchThreeRewardTaken { get; private set; }

    /// <summary>The server's side of a Mimic game (Cooking), from Go on.</summary>
    public Mimic.MimicGame? Mimic { get; set; }

    /// <summary>The Mimic result already logged and paid, so each game pays once.</summary>
    public Mimic.MimicResult SettledMimicResult { get; set; }

    /// <summary>
    /// The match-three game's reward, the first time the server's own game is won: every level passed, as the server
    /// counted the board (<see cref="MatchThreeResult.Won"/>). Nothing for a loss, a stopped game or one left
    /// unfinished, and nothing more after the first time, however often the Flash repeats or replays its end messages:
    /// they come from the client, so none of them decides a win. The caller pays what this hands over.
    /// </summary>
    public bool TryTakeMatchThreeReward(out MinigameReward reward)
    {
        if (MatchThreeRewardTaken || MatchThree is not { Result: MatchThreeResult.Won } game)
        {
            reward = default;
            return false;
        }

        MatchThreeRewardTaken = true;
        reward = game.Reward;
        return true;
    }

    /// <summary>True the first time a message name is seen, while the per-session limit allows.</summary>
    public bool ShouldLog(string messageName)
    {
        lock (_loggedMessageNames)
            return _loggedMessageNames.Count < MaxLoggedMessageNames && _loggedMessageNames.Add(messageName);
    }
}
