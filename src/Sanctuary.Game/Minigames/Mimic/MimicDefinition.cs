using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Sanctuary.Game.Minigames.Mimic;

/// <summary>
/// Evergrove's Mimic minigame recipes (Cooking now; Smelting, Sword and Music share the format), from
/// <c>Resources/Mimic.json</c>. SOE's <c>MimicMiniGameData.txt</c> and <c>MimicMiniGamePhaseData.txt</c> were server
/// data and never shipped with the client, so these are designed here. See docs/research/mimic.md.
/// </summary>
public sealed class MimicDefinitions
{
    public List<MimicGameDefinition> Games { get; set; } = [];

    public MimicGameDefinition? Find(string name) =>
        Games.FirstOrDefault(game => string.Equals(game.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The game and recipe a Games menu entry plays. False if the activity isn't a Mimic recipe.</summary>
    public bool TryFindActivity(int activityId, out MimicGameDefinition game, out MimicRecipeDefinition recipe)
    {
        foreach (var candidate in Games)
        {
            if (candidate.Recipes.FirstOrDefault(r => r.ActivityId == activityId) is { } found)
            {
                (game, recipe) = (candidate, found);
                return true;
            }
        }

        (game, recipe) = (null!, null!);
        return false;
    }

    public IEnumerable<string> Problems()
    {
        foreach (var game in Games)
            foreach (var problem in game.Problems())
                yield return problem;

        foreach (var name in Games.GroupBy(game => game.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            yield return $"game \"{name.Key}\" is defined twice";

        var recipes = Games.SelectMany(game => game.Recipes).ToList();

        foreach (var activity in recipes.GroupBy(recipe => recipe.ActivityId).Where(group => group.Count() > 1))
            yield return $"activity {activity.Key} plays more than one recipe";
    }

    public static bool TryLoad(string filePath, ILogger logger, out MimicDefinitions definitions)
    {
        definitions = new MimicDefinitions();

        try
        {
            using var stream = File.OpenRead(filePath);
            var loaded = JsonSerializer.Deserialize<MimicDefinitions>(stream, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (loaded is null)
            {
                logger.LogError("No Mimic games in \"{file}\".", filePath);
                return false;
            }

            var problems = loaded.Problems().ToList();
            foreach (var problem in problems)
                logger.LogError("Mimic in \"{file}\": {problem}.", filePath, problem);

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

/// <summary>One Mimic Flash game (Cooking...) and its recipes.</summary>
public sealed class MimicGameDefinition
{
    /// <summary>The game's name; "cooking" also turns on the checks in <see cref="CookingStages"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Flash file the client loads: <c>game_cooking.gfx</c>.</summary>
    public string Swf { get; set; } = string.Empty;

    /// <summary>The client's MiniGameTypeData.txt type for the start screen. Cooking's MiniGameData rows are type 9.</summary>
    public int MiniGameType { get; set; }

    /// <summary>The most phases the Flash can show. Cooking's round bar has 8 slots (<c>CookingData.MAX_ROUNDS</c>).</summary>
    public int MaxPhases { get; set; } = 8;

    /// <summary>
    /// True when the Flash reports every finished round (<c>FRServer_RoundTransitionPause true</c>, Cooking and
    /// Smelting): a win then needs one per phase. Sword and Music report nothing per round.
    /// </summary>
    public bool ReportsRounds { get; set; }

    /// <summary>The fewest seconds per phase an honest win takes; a quicker win is refused.</summary>
    public int MinSecondsPerPhase { get; set; } = 3;

    /// <summary>The highest final score taken from the client; more is capped to it.</summary>
    public int MaxScore { get; set; } = 1_000_000;

    /// <summary>The job whose stars a win pays (45 is Chef).</summary>
    public int RewardProfileId { get; set; }

    public List<MimicRecipeDefinition> Recipes { get; set; } = [];

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name))
            yield return "a game has no name";

        if (string.IsNullOrWhiteSpace(Swf))
            yield return $"game \"{Name}\" has no Flash file";

        if (MaxPhases < 1)
            yield return $"game \"{Name}\": MaxPhases must be at least 1";

        if (MinSecondsPerPhase < 0 || MaxScore < 0)
            yield return $"game \"{Name}\": MinSecondsPerPhase and MaxScore can't be negative";

        if (Recipes.Count == 0)
            yield return $"game \"{Name}\" has no recipes";

        foreach (var recipe in Recipes)
            foreach (var problem in recipe.Problems(this))
                yield return $"game \"{Name}\", recipe \"{recipe.Name}\": {problem}";

        foreach (var id in Recipes.GroupBy(recipe => recipe.GameId).Where(group => group.Count() > 1))
            yield return $"game \"{Name}\": game id {id.Key} is used by more than one recipe";
    }
}

/// <summary>One recipe: a Games menu entry, and the phases (rounds) the Flash plays in order.</summary>
public sealed class MimicRecipeDefinition
{
    /// <summary>Cooking shows a promotion banner over every round of game id 47.</summary>
    public const int PromotionGameId = 47;

    public string? Comment { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The Games menu entry (ClientActivityDefinitions.json) that plays this recipe.</summary>
    public int ActivityId { get; set; }

    /// <summary>The client's MiniGameData.txt row: the start screen's name, icon and difficulty.</summary>
    public int MiniGameId { get; set; }

    /// <summary>
    /// The Mimic game id: sent in <c>OnGameDataMsg</c> and written in every phase row's <c>LD_GAME</c> column. The
    /// Flash keeps only the rows that carry it.
    /// </summary>
    public int GameId { get; set; }

    /// <summary>Coins for a win. Placeholder until the economy pass.</summary>
    public int Coins { get; set; }

    /// <summary>Stars on the game's <see cref="MimicGameDefinition.RewardProfileId"/> job for a win. Placeholder.</summary>
    public int Experience { get; set; }

    public List<MimicPhaseDefinition> Phases { get; set; } = [];

    /// <summary>Rows sent with <c>OnCustomDataMsg</c> (Sword's <c>MimicSwordData.txt</c>). Cooking reads none.</summary>
    public List<string> CustomData { get; set; } = [];

    public IEnumerable<string> Problems(MimicGameDefinition game)
    {
        if (string.IsNullOrWhiteSpace(Name))
            yield return "a recipe has no name";

        if (ActivityId <= 0 || MiniGameId <= 0)
            yield return "ActivityId and MiniGameId must be set";

        if (GameId <= 0)
            yield return "GameId must be above 0";
        else if (GameId == PromotionGameId && string.Equals(game.Name, "cooking", StringComparison.OrdinalIgnoreCase))
            yield return $"game id {PromotionGameId} shows Cooking's promotion banner";

        if (Coins < 0 || Experience < 0)
            yield return "rewards can't be negative";

        if (Phases.Count == 0 || Phases.Count > game.MaxPhases)
            yield return $"needs 1 to {game.MaxPhases} phases, has {Phases.Count}";

        for (var i = 0; i < Phases.Count; i++)
            foreach (var problem in Phases[i].Problems(game))
                yield return $"phase {i + 1}: {problem}";

        if (string.Equals(game.Name, "cooking", StringComparison.OrdinalIgnoreCase))
            foreach (var problem in CookingStages.RecipeProblems(this))
                yield return problem;

        foreach (var row in CustomData)
            if (row.IndexOfAny(['\t', '\n', '\r', '\0']) >= 0)
                yield return "a CustomData row holds a tab, line break or NUL";
    }

    /// <summary>
    /// The phase row for <c>MimicMiniGamePhaseData.txt</c>, <c>^</c>-separated: <c>LD_ID</c>, <c>LD_GAME</c>,
    /// <c>LD_SEQUENCE</c> (1 first), <c>LD_DURATION</c>, <c>LD_OBJECTIVE_ID</c>, <c>LD_STARTID</c>, <c>LD_ENDID</c>,
    /// <c>LD_STAGE_TYPE</c>, then <c>LD_DATA1</c> to <c>LD_DATA11</c>, missing ones 0.
    /// </summary>
    public string PhaseRow(int sequence)
    {
        var phase = Phases[sequence - 1];

        var columns = new List<int>
        {
            GameId * 100 + sequence,
            GameId,
            sequence,
            phase.DurationSeconds,
            phase.ObjectiveId,
            phase.StartId,
            phase.EndId,
            phase.StageType
        };

        for (var i = 0; i < MimicPhaseDefinition.DataColumns; i++)
            columns.Add(i < phase.Data.Count ? phase.Data[i] : 0);

        return string.Join('^', columns.Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }
}

/// <summary>One phase (round) of a recipe: a row of <c>MimicMiniGamePhaseData.txt</c>.</summary>
public sealed class MimicPhaseDefinition
{
    /// <summary><c>LD_DATA1</c> to <c>LD_DATA11</c>.</summary>
    public const int DataColumns = 11;

    public string? Comment { get; set; }

    /// <summary><c>LD_STAGE_TYPE</c>: which round. Cooking's are in <see cref="CookingStages"/>.</summary>
    public int StageType { get; set; }

    /// <summary><c>LD_DURATION</c>: the round's timer.</summary>
    public int DurationSeconds { get; set; }

    /// <summary><c>LD_OBJECTIVE_ID</c>, <c>LD_STARTID</c>, <c>LD_ENDID</c>: read by no Mimic Flash; sent as given.</summary>
    public int ObjectiveId { get; set; }

    public int StartId { get; set; }

    public int EndId { get; set; }

    /// <summary>
    /// <c>LD_DATA1</c> onward; missing ones are sent as 0. The Flash drops a column that isn't a number, and several
    /// rounds then compute with "undefined", so every column is always sent.
    /// </summary>
    public List<int> Data { get; set; } = [];

    public IEnumerable<string> Problems(MimicGameDefinition game)
    {
        if (StageType <= 0)
            yield return "StageType must be set";

        if (DurationSeconds < 5 || DurationSeconds > 600)
            yield return "DurationSeconds must be 5 to 600";

        if (Data.Count > DataColumns)
            yield return $"at most {DataColumns} Data columns";

        if (string.Equals(game.Name, "cooking", StringComparison.OrdinalIgnoreCase))
            foreach (var problem in CookingStages.Problems(this))
                yield return problem;
    }
}
