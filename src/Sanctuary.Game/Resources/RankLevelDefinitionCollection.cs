using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Collections;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Resources;

/// <summary>
/// The one level curve every job shares (<c>RankLevels.json</c>): how many stars each level takes to finish. The top
/// level takes none, since there is nothing after it. From upstream OSFR PR #120 by ImAlko.
/// </summary>
public sealed class RankLevelDefinitionCollection : ObservableConcurrentDictionary<int, RankLevelDefinition>
{
    private readonly ILogger _logger;
    private readonly object _writeLock = new();

    public RankLevelDefinitionCollection(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>The top level, or 0 until a curve has loaded (so nobody earns anything without one).</summary>
    public int MaxLevel { get; private set; }

    public bool Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("Failed to find file \"{file}\"", filePath);
            return false;
        }

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var entries = JsonSerializer.Deserialize<List<RankLevelDefinition>>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (entries is null || entries.Count == 0)
            {
                _logger.LogError("No rank levels found in \"{file}\".", filePath);
                return false;
            }

            var loaded = new Dictionary<int, RankLevelDefinition>();

            foreach (var entry in entries)
            {
                if (!loaded.TryAdd(entry.Level, entry))
                {
                    _logger.LogError("Duplicate rank level {level} in \"{file}\".", entry.Level, filePath);
                    return false;
                }
            }

            // Levels run 1, 2, 3... with no gaps, every level but the top one takes stars to finish, and the top
            // one takes none.
            var maxLevel = loaded.Count;

            for (var level = 1; level <= maxLevel; level++)
            {
                if (!loaded.TryGetValue(level, out var entry))
                {
                    _logger.LogError("Rank level {level} is missing from \"{file}\"; levels must run from 1 with no gaps.",
                        level, filePath);
                    return false;
                }

                if (level < maxLevel ? entry.StarsToNextLevel <= 0 : entry.StarsToNextLevel != 0)
                {
                    _logger.LogError("Rank level {level} in \"{file}\" has {stars} stars to the next level; every level " +
                        "but the top one needs more than 0, and the top one needs 0.", level, filePath, entry.StarsToNextLevel);
                    return false;
                }
            }

            lock (_writeLock)
            {
                foreach (var entry in loaded)
                    this[entry.Key] = entry.Value;

                foreach (var key in Keys.Where(key => !loaded.ContainsKey(key)).ToArray())
                    Remove(key);

                MaxLevel = maxLevel;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse file \"{file}\".", filePath);
            return false;
        }
    }

    /// <summary>
    /// Adds <paramref name="stars"/> to a job at <paramref name="level"/> that is already <paramref name="levelXp"/>
    /// stars into it, and levels up as many times as they cover. What's left over carries into the new level; at the
    /// top level there is nothing left to fill, so it is dropped.
    /// </summary>
    public (int Level, int LevelXp) Accrue(int level, int levelXp, int stars)
    {
        if (stars <= 0 || level >= MaxLevel)
            return (level, levelXp);

        var xp = (long)levelXp + stars;

        while (level < MaxLevel && TryGetValue(level, out var rankLevel) && xp >= rankLevel.StarsToNextLevel)
        {
            xp -= rankLevel.StarsToNextLevel;
            level++;
        }

        if (level >= MaxLevel)
            xp = 0;

        return (level, (int)Math.Min(xp, int.MaxValue));
    }

    /// <summary>How far through <paramref name="level"/> a job is, 0 to 100, for the client's level bar.</summary>
    public int GetRankPercent(int level, int levelXp)
    {
        if (!TryGetValue(level, out var rankLevel) || rankLevel.StarsToNextLevel <= 0)
            return 0;

        return (int)Math.Clamp(100.0 * levelXp / rankLevel.StarsToNextLevel, 0, 100);
    }
}
