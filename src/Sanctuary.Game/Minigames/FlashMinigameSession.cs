using System.Collections.Generic;

namespace Sanctuary.Game.Minigames;

/// <summary>What the server knows about a Flash minigame it can launch.</summary>
/// <param name="ActivityId">The Games menu entry (ClientActivityDefinitions.json).</param>
/// <param name="GameId">The client's MiniGameData.txt row.</param>
/// <param name="Type">The client's MiniGameTypeData.txt type, e.g. 8 for match three.</param>
/// <param name="Swf">The Flash file the client loads, from its packs or the asset server.</param>
public sealed record FlashMinigameDefinition(int ActivityId, int GameId, int Type, string Swf);

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

    /// <summary>True the first time a message name is seen, while the per-session limit allows.</summary>
    public bool ShouldLog(string messageName)
    {
        lock (_loggedMessageNames)
            return _loggedMessageNames.Count < MaxLoggedMessageNames && _loggedMessageNames.Add(messageName);
    }
}
