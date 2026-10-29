using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Sanctuary.Game.Minigames.MatchThree;

/// <summary>
/// Evergrove's match-three levels, from <c>Resources/MatchThree.json</c>. SOE's own level tables
/// (<c>MatchThree*.txt</c>) were server data and never shipped with the client, so these are designed here.
/// </summary>
public sealed class MatchThreeDefinitions
{
    public List<MatchThreeGameDefinition> Games { get; set; } = [];

    public MatchThreeGameDefinition? Find(string name) =>
        Games.FirstOrDefault(game => string.Equals(game.Name, name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<string> Problems()
    {
        foreach (var game in Games)
        {
            if (string.IsNullOrWhiteSpace(game.Name))
                yield return "a game has no name";

            if (game.GameNumber <= 0)
                yield return $"game \"{game.Name}\" needs its GameNumber";

            if (game.Levels.Count == 0)
                yield return $"game \"{game.Name}\" has no levels";

            if (game.Coins < 0 || game.Experience < 0 || game.RewardProfileId < 0)
                yield return $"game \"{game.Name}\": rewards can't be negative";

            if (game.Experience > 0 && game.RewardProfileId == 0)
                yield return $"game \"{game.Name}\": Experience needs a RewardProfileId, the job its stars go to";

            if (game.MinSecondsPerLevel < 0)
                yield return $"game \"{game.Name}\": MinSecondsPerLevel can't be negative";

            for (var i = 0; i < game.Levels.Count; i++)
                foreach (var problem in game.Levels[i].Problems())
                    yield return $"game \"{game.Name}\", level {i + 1}: {problem}";
        }

        foreach (var name in Games.GroupBy(game => game.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            yield return $"game \"{name.Key}\" is defined twice";

        foreach (var number in Games.Where(game => game.GameNumber > 0).GroupBy(game => game.GameNumber).Where(group => group.Count() > 1))
            yield return $"game number {number.Key} is used twice";
    }

    public static bool TryLoad(string filePath, ILogger logger, out MatchThreeDefinitions definitions)
    {
        definitions = new MatchThreeDefinitions();

        try
        {
            using var stream = File.OpenRead(filePath);
            var loaded = JsonSerializer.Deserialize<MatchThreeDefinitions>(stream, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (loaded is null)
            {
                logger.LogError("No match-three games in \"{file}\".", filePath);
                return false;
            }

            var problems = loaded.Problems().ToList();
            foreach (var problem in problems)
                logger.LogError("Match three in \"{file}\": {problem}.", filePath, problem);

            if (problems.Count > 0)
                return false;

            definitions = loaded;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load \"{file}\".", filePath);
            return false;
        }
    }
}

public sealed class MatchThreeGameDefinition
{
    /// <summary>
    /// The client's name for the game (<c>GameData.sGAME</c>, set by each Flash's first frame): it picks the pictures.
    /// "mining", "farming", "forging" and "mail".
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// SOE's number for the game (the Flash's <c>ConfigServer.GAME_ID</c>): Mining 1, Mail Sorting 12, Farming 60,
    /// Forging 62. It picked the game's rows in SOE's level tables; the client never asks for it.
    /// </summary>
    public int GameNumber { get; set; }

    /// <summary>Played in order; winning the last wins the game.</summary>
    public List<MatchThreeLevelDefinition> Levels { get; set; } = [];

    /// <summary>Coins for winning the whole game, every level. A placeholder until the economy pass.</summary>
    public int Coins { get; set; }

    /// <summary>Stars for a win, on the <see cref="RewardProfileId"/> job, paid only if the player has it. A placeholder.</summary>
    public int Experience { get; set; }

    /// <summary>The job a win's stars go to (Profiles.json: 14 Miner, 4 Postman, 16 Blacksmith, 45 Chef); 0 for none.</summary>
    public int RewardProfileId { get; set; }

    /// <summary>
    /// The fewest seconds an honest player spends on a level. A game won in less than this times its levels, from its
    /// start to the win on the server's clock, still ends as a win for the client but pays nothing: only a client that
    /// skips the Flash's own animations gets there. 0 turns the check off.
    /// </summary>
    public int MinSecondsPerLevel { get; set; } = 5;

    /// <summary>What winning the game pays.</summary>
    public MinigameReward Reward => new(Coins, RewardProfileId, Experience);
}

public sealed class MatchThreeLevelDefinition
{
    public string? Comment { get; set; }

    /// <summary>Seconds on the clock at the start. Chains add time.</summary>
    public int DurationSeconds { get; set; } = 60;

    /// <summary>Seconds a chain adds per tile in it (SOE's: 2). Zero for no time from chains.</summary>
    public double ChainSecondsPerTile { get; set; } = 2;

    /// <summary>The level ends as soon as every quota is met, not only when time runs out.</summary>
    public bool StopAtQuota { get; set; }

    /// <summary>Shows the client's "Bonus" banner instead of "Ready".</summary>
    public bool BonusLevel { get; set; }

    /// <summary>The client's mold meter picture, where the game has one.</summary>
    public int MoldId { get; set; }

    public MatchThreeSpawnChances SpawnChances { get; set; } = new();

    /// <summary>
    /// Rows of cell codes, top row first: 0 nothing, 1 a rock (or a hazard or wildcard), 2 anything the level spawns,
    /// 3 a blocker, 4 the drop zone ore falls out through. Row 0 and column 0 must be 0: the board's falls start
    /// from the row above the first full one and never look at column 0 (both the client's and SOE's server's).
    /// The cells also decide where the board appears: see <see cref="BoardCentreColumn"/>.
    /// </summary>
    public List<int[]> Grid { get; set; } = [];

    /// <summary>
    /// The column a board must be centred on. Each match-three Flash draws cell (x, y) at (40x, 40y) in its game clip
    /// and never centres the board itself; its frame (rock, field, mail rack, furnace), bins and meters are drawn around
    /// columns 1 to 12, where SOE's boards sat (2009 screenshots). So an 8-wide board uses columns 3 to 10. One that
    /// starts at column 1 sits two columns left of its frame, and left of the middle of the screen.
    /// </summary>
    public const double BoardCentreColumn = 6.5;

    /// <summary>
    /// The tiles the player chains: rocks 1 to 5 in Mining and Forging, crops 1 to 16 in Farming, letters 1 to 8 in Mail
    /// Sorting. With a quota, chained tiles fill a bin.
    /// </summary>
    public List<MatchThreeSpawnDefinition> Selectables { get; set; } = [];

    /// <summary>
    /// Pieces that fall out through the drop zone: ore 0 to 14 in Mining (iron, tin, silver, gold, copper), weapon pieces
    /// 0 to 26 in Forging. Farming and Mail Sorting have none.
    /// </summary>
    public List<MatchThreeSpawnDefinition> Collectables { get; set; } = [];

    /// <summary>Gems: 0 to 9 in Mining. The other games have none.</summary>
    public List<MatchThreeSpawnDefinition> Bonuses { get; set; } = [];

    /// <summary>
    /// Power-ups by type: 0 more time, 1 double score, 2 five times score, 50 dynamite, 51 wild tiles, 52 destruct,
    /// 53 shuffle.
    /// </summary>
    public List<MatchThreeSpawnDefinition> Powerups { get; set; } = [];

    public MatchThreeLevelType TypeFlags =>
        (StopAtQuota ? MatchThreeLevelType.StopAtQuota : MatchThreeLevelType.None) |
        (BonusLevel ? MatchThreeLevelType.BonusLevel : MatchThreeLevelType.None);

    public IEnumerable<string> Problems()
    {
        if (DurationSeconds <= 0)
            yield return "DurationSeconds must be above 0";

        if (ChainSecondsPerTile < 0)
            yield return "ChainSecondsPerTile can't be negative";

        if (Grid.Count < 3 || Grid[0].Length < 3)
            yield return "the grid needs at least 3 rows of 3";
        else
        {
            if (Grid.Any(row => row.Length != Grid[0].Length))
                yield return "every grid row must be as long as the first";

            if (Grid[0].Any(cell => cell != MatchThreeTiles.ShapeEmpty) || Grid.Any(row => row[0] != MatchThreeTiles.ShapeEmpty))
                yield return "row 0 and column 0 of the grid must be 0";

            if (Grid.SelectMany(row => row).Any(cell => cell is < MatchThreeTiles.ShapeEmpty or > MatchThreeTiles.ShapeDropZone))
                yield return "grid cells must be 0 to 4";

            var columns = Enumerable.Range(0, Grid[0].Length)
                .Where(x => Grid.Any(row => x < row.Length && row[x] != MatchThreeTiles.ShapeEmpty))
                .ToList();

            if (columns.Count > 0 && Math.Abs((columns[0] + columns[^1]) / 2.0 - BoardCentreColumn) > 0.5)
            {
                var width = columns[^1] - columns[0] + 1;
                var first = (int)Math.Ceiling(BoardCentreColumn - (width - 1) / 2.0);

                yield return string.Create(CultureInfo.InvariantCulture,
                    $"the board spans columns {columns[0]} to {columns[^1]}, off the middle of the Flash's frame: " +
                    $"centre it on column {BoardCentreColumn} (columns {first} to {first + width - 1})");
            }
        }

        if (SpawnChances.Selectable <= 0 || Selectables.All(spawn => spawn.Chance <= 0))
            yield return "the level needs rocks to chain (a selectable spawn chance and at least one selectable)";

        foreach (var (list, name, max) in new[] { (Selectables, "selectable", 99), (Collectables, "collectable", 99), (Bonuses, "bonus", 99) })
        {
            if (list.Any(spawn => spawn.Type < 0 || spawn.Type > max))
                yield return $"{name} types must be 0 to {max}";

            if (list.GroupBy(spawn => spawn.Type).Any(group => group.Count() > 1))
                yield return $"a {name} type is listed twice";
        }

        if (Powerups.Any(spawn => !MatchThreeTiles.IsPowerup(MatchThreeTiles.Powerup + spawn.Type)))
            yield return "power-up types must be 0, 1, 2, 50, 51, 52 or 53";

        if (Powerups.GroupBy(spawn => spawn.Type).Any(group => group.Count() > 1))
            yield return "a power-up type is listed twice";

        if (Powerups.Any(spawn => spawn.Type >= 50 && spawn.Power <= 0))
            yield return "dynamite, wild tiles, destruct and shuffle need a Power above 0";

        if (Selectables.Concat(Collectables).Concat(Bonuses).Concat(Powerups).Any(spawn => spawn.Chance < 0 || spawn.Quota < 0))
            yield return "chances and quotas can't be negative";
    }
}

/// <summary>How often each kind of tile is rolled, as weights against each other.</summary>
public sealed class MatchThreeSpawnChances
{
    public int Collectable { get; set; }
    public int Bonus { get; set; }
    public int Selectable { get; set; } = 80;
    public int Powerup { get; set; }
    public int Hazard { get; set; }
    public int Blocker { get; set; }
    public int Wildcard { get; set; }
}

/// <summary>One tile type a level can spawn (SOE's <c>SpawnData</c>).</summary>
public sealed class MatchThreeSpawnDefinition
{
    public string? Comment { get; set; }

    public int Type { get; set; }

    /// <summary>Weight against the other types of its kind.</summary>
    public int Chance { get; set; }

    /// <summary>How many the player must collect; 0 for no quota.</summary>
    public int Quota { get; set; }

    /// <summary>
    /// Shown on the quota bin. A type gets a bin if it has a quota or this is above 0 (gems always do); a quota with
    /// this left at 0 shows 1.
    /// </summary>
    public int RequiredPerReward { get; set; }

    /// <summary>The icon on the quota bin.</summary>
    public int IconId { get; set; }

    /// <summary>At most this many are made in a level; 0 for no limit.</summary>
    public int MaxCreate { get; set; }

    /// <summary>At most this many count; 0 for no limit.</summary>
    public int MaxCollect { get; set; }

    /// <summary>Power-ups: seconds the effect lasts (more time, score multipliers).</summary>
    public int Duration { get; set; }

    /// <summary>Power-ups: how many tiles dynamite, wild tiles, destruct and shuffle touch.</summary>
    public int Power { get; set; }
}
