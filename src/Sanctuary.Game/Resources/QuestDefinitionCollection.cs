using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Resources;

public class QuestDefinitionCollection
{
    private readonly ILogger _logger;

    // The quests and their npc indexes, replaced as one when the file is (re)loaded, so a reload never leaves a mix
    // of old and new quests, and a broken file leaves the loaded quests as they were.
    private sealed record Index(
        ConcurrentDictionary<int, QuestDefinition> Quests,
        ConcurrentDictionary<ulong, List<int>> ByGiver,
        ConcurrentDictionary<ulong, List<int>> ByTarget);

    private volatile Index _index = new(new(), new(), new());

    public ConcurrentDictionary<int, QuestDefinition> Quests => _index.Quests;

    public ConcurrentDictionary<ulong, List<int>> ByGiver => _index.ByGiver;
    public ConcurrentDictionary<ulong, List<int>> ByTarget => _index.ByTarget;

    public QuestDefinitionCollection(ILogger logger)
    {
        _logger = logger;
    }

    public bool TryGet(int questId, out QuestDefinition definition) => Quests.TryGetValue(questId, out definition!);

    public bool TryGetNpcCursorId(ulong npcGuid, out byte cursorId)
    {
        cursorId = 0;

        foreach (var index in new[] { ByGiver, ByTarget })
        {
            if (!index.TryGetValue(npcGuid, out var questIds))
                continue;

            foreach (var questId in questIds)
                if (TryGet(questId, out var quest))
                    foreach (var goal in quest.Goals)
                        if (goal.CursorId != 0)
                        {
                            cursorId = goal.CursorId;
                            return true;
                        }
        }

        return false;
    }
    public bool TryGetNpcInteractRange(ulong npcGuid, out int interactRange)
    {
        interactRange = int.MaxValue;

        foreach (var index in new[] { ByGiver, ByTarget })
        {
            if (!index.TryGetValue(npcGuid, out var questIds))
                continue;

            foreach (var questId in questIds)
                if (TryGet(questId, out var quest))
                    foreach (var goal in quest.Goals)
                        if (goal.InteractRange > 0)
                            interactRange = Math.Min(interactRange, goal.InteractRange);
        }

        if (interactRange == int.MaxValue)
        {
            interactRange = 0;
            return false;
        }

        return true;
    }

    public bool Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Failed to find file \"{file}\". No quests will be loaded.", filePath);
            return true;
        }

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            var entries = JsonSerializer.Deserialize<List<QuestDefinition>>(fileStream, jsonSerializerOptions);

            if (entries is null)
            {
                _logger.LogError("No entries found in file \"{file}\".", filePath);
                return false;
            }

            var index = new Index(new(), new(), new());

            foreach (var entry in entries)
            {
                if (entry.Goals.Count == 0)
                {
                    _logger.LogError("Quest {id} has no goals. \"{file}\"", entry.QuestId, filePath);
                    return false;
                }

                if (!index.Quests.TryAdd(entry.QuestId, entry))
                {
                    _logger.LogError("Quest {id} is in the file twice. \"{file}\"", entry.QuestId, filePath);
                    return false;
                }

                if (entry.GiverGuid != 0)
                    index.ByGiver.GetOrAdd(entry.GiverGuid, _ => []).Add(entry.QuestId);

                if (entry.TargetGuid != 0)
                    index.ByTarget.GetOrAdd(entry.TargetGuid, _ => []).Add(entry.QuestId);

                IndexGoals(index, entry, filePath);
            }

            if (!HasOnlyKnownQuestLinks(index, filePath))
                return false;

            _index = index;

            _logger.LogInformation("Loaded {count} quest definitions from \"{file}\".", index.Quests.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse file \"{file}\".", filePath);
            return false;
        }

        return true;
    }

    /// <summary>
    /// A quest that names a prerequisite, next or excluded quest the file doesn't have can't be offered or chained
    /// as written, so the whole file is refused, naming every broken link.
    /// </summary>
    private bool HasOnlyKnownQuestLinks(Index index, string filePath)
    {
        var valid = true;

        foreach (var quest in index.Quests.Values)
        {
            foreach (var (field, linkedId) in QuestLinks(quest))
            {
                if (linkedId == 0 || index.Quests.ContainsKey(linkedId))
                    continue;

                _logger.LogError("Quest {id}'s {field} is quest {linkedId}, which isn't in \"{file}\".", quest.QuestId, field, linkedId, filePath);
                valid = false;
            }
        }

        return valid;
    }

    private static IEnumerable<(string Field, int QuestId)> QuestLinks(QuestDefinition quest)
    {
        yield return (nameof(QuestDefinition.PrerequisiteQuestId), quest.PrerequisiteQuestId);
        yield return (nameof(QuestDefinition.NextQuestId), quest.NextQuestId);

        foreach (var excluded in quest.ExcludesQuestIds)
            yield return (nameof(QuestDefinition.ExcludesQuestIds), excluded);
    }

    private void IndexGoals(Index index, QuestDefinition quest, string filePath)
    {
        var goalNameIds = new HashSet<int>();

        foreach (var goal in quest.Goals)
        {
            foreach (var targetGuid in goal.AllTalkTargetGuids())
            {
                var questIds = index.ByTarget.GetOrAdd(targetGuid, _ => []);

                if (!questIds.Contains(quest.QuestId))
                    questIds.Add(quest.QuestId);
            }

            if (!goalNameIds.Add(goal.NameId))
                _logger.LogWarning("Duplicate goal NameId {nameId} on quest {id} in \"{file}\".", goal.NameId, quest.QuestId, filePath);

            if (goal.Type == QuestGoalType.Collect)
            {
                if (string.IsNullOrEmpty(goal.CollectNodeType))
                    _logger.LogWarning("Collect goal on quest {id} has no CollectNodeType in \"{file}\"; it can never be credited.", quest.QuestId, filePath);

                if (goal.RequiredCount <= 0)
                    _logger.LogWarning("Collect goal on quest {id} has no RequiredCount in \"{file}\"; it can never be completed.", quest.QuestId, filePath);
            }
        }
    }
}
