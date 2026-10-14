using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Collections;
using Sanctuary.Game.Collections;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Resources;

public sealed class CollectionDefinitionCollection : ObservableConcurrentDictionary<int, CollectionDefinition>
{
    private readonly ILogger _logger;
    private Dictionary<int, CollectionEntryMatch> _entriesById = new();

    public CollectionDefinitionCollection(ILogger logger)
    {
        _logger = logger;
    }

    public readonly record struct CollectionEntryMatch(
        CollectionDefinition Definition,
        CollectionEntryDefinition Entry,
        int Index);

    public bool Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("Failed to find file \"{file}\"", filePath);
            return false;
        }

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var entries = JsonSerializer.Deserialize<List<CollectionDefinition>>(fileStream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (entries is null || entries.Count == 0)
            {
                _logger.LogError("No entries found in file \"{file}\".", filePath);
                return false;
            }

            if (!Validate(entries, filePath))
                return false;

            var entriesById = new Dictionary<int, CollectionEntryMatch>();

            foreach (var definition in entries)
            {
                for (var index = 0; index < definition.Entries.Count; index++)
                    entriesById.Add(definition.Entries[index].Id, new CollectionEntryMatch(definition, definition.Entries[index], index));
            }

            Clear();

            foreach (var entry in entries)
                TryAdd(entry.Id, entry);

            _entriesById = entriesById;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse file \"{file}\".", filePath);
            return false;
        }
    }

    /// <summary>
    /// Finds the collection entry with the supplied id. Entry ids are unique across all collections.
    /// </summary>
    public bool TryGetEntry(int entryId, out CollectionEntryMatch match)
    {
        return _entriesById.TryGetValue(entryId, out match);
    }

    /// <summary>
    /// Finds the collection entry that an item marks collected, if any.
    /// </summary>
    public bool TryGetEntryByItem(int itemDefinitionId, out CollectionEntryMatch match)
    {
        foreach (var candidate in _entriesById.Values)
        {
            if (!candidate.Entry.IsDirect && candidate.Entry.ItemDefinitionId == itemDefinitionId)
            {
                match = candidate;
                return true;
            }
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Every collection, started or not, as SOE's server sent them (the 2014 capture gives one character 253). The
    /// client's collections window needs this: it sizes its table by the longest collection it was sent and then
    /// reads eight entry columns, so a player whose collections all had five entries got a Lua error, and a window
    /// that couldn't be closed (Ariel and Nate, 2026-10-12; CollectionsBrowser.lua:134).
    /// </summary>
    public List<ClientCollection> CreateClientCollections(ulong playerGuid, CollectionProgress progress,
        Func<int, ClientItemDefinition?> itemDefinitions, int gender = 0)
    {
        return Values
            .OrderBy(definition => definition.CategoryId)
            .ThenBy(definition => definition.Id)
            .Select(definition => definition.CreateClientCollection(playerGuid, progress, itemDefinitions, gender))
            .ToList();
    }

    private bool Validate(List<CollectionDefinition> definitions, string filePath)
    {
        if (definitions.Select(definition => definition.Id).Distinct().Count() != definitions.Count)
        {
            _logger.LogError("Duplicate collection ids found in \"{file}\".", filePath);
            return false;
        }

        var allEntries = definitions.SelectMany(definition => definition.Entries).ToList();

        if (allEntries.Select(entry => entry.Id).Distinct().Count() != allEntries.Count)
        {
            _logger.LogError("Duplicate collection entry ids found in \"{file}\".", filePath);
            return false;
        }

        var itemEntries = allEntries.Where(entry => !entry.IsDirect).ToList();

        if (itemEntries.Select(entry => entry.ItemDefinitionId).Distinct().Count() != itemEntries.Count)
        {
            _logger.LogError("Two collection entries share an item definition in \"{file}\".", filePath);
            return false;
        }

        foreach (var definition in definitions)
        {
            if (definition.Id <= 0 || definition.NameId <= 0 || definition.CategoryId <= 0 ||
                definition.Entries.Count == 0 ||
                definition.RewardCoins < 0 || definition.RewardExperience < 0 || definition.RewardProfileId < 0 ||
                definition.RewardItemIds.Any(itemId => itemId <= 0) ||
                definition.RewardItemIds.Distinct().Count() != definition.RewardItemIds.Count ||
                definition.Entries.Any(entry => entry.Id <= 0 || entry.ItemDefinitionId < 0))
            {
                _logger.LogError("Collection {id} has invalid required data in \"{file}\".", definition.Id, filePath);
                return false;
            }
        }

        return true;
    }
}
