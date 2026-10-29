using System.Collections.Generic;

namespace Sanctuary.Game.Minigames;

/// <summary>The match-three Flash games the server plays; see docs/research/mining.md.</summary>
public static class FlashMinigames
{
    /// <summary>
    /// Activity id → game: the activity's MiniGameData row (its <c>Unknown</c> in ClientActivityDefinitions.json), type 8
    /// (match three). One entry-level activity per game; each game's other activities (the other ores, farms, towns and
    /// weapons) aren't wired yet.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, FlashMinigameDefinition> Definitions = new Dictionary<int, FlashMinigameDefinition>
    {
        // Mining Practice.
        [1113] = new(ActivityId: 1113, GameId: 375, Type: 8, Swf: "game_mining.gfx", MatchThreeGame: "mining"),
        // Queensfields Harvesting.
        [410] = new(ActivityId: 410, GameId: 109, Type: 8, Swf: "game_farming.gfx", MatchThreeGame: "farming"),
        // Sanctuary Mail Sorting.
        [414] = new(ActivityId: 414, GameId: 376, Type: 8, Swf: "game_mailsorting.gfx", MatchThreeGame: "mail"),
        // Forging: Quick Shot Bow.
        [420] = new(ActivityId: 420, GameId: 138, Type: 8, Swf: "game_forging.gfx", MatchThreeGame: "forging")
    };
}
