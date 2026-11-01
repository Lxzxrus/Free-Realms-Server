using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Collections;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Resources;

public class CoinStoreItemCollection : ObservableConcurrentDictionary<int, ItemDefinitionMetaData>
{
    private readonly ILogger _logger;

    // Where each item sits in its category: the category's item ids in the file's order, and the item's index in them.
    // Built once per load, so an item definition request reads the items after the one it asks for without copying or
    // walking the whole coin store (17,000 items) on the packet thread.
    private volatile IReadOnlyDictionary<int, (int[] CategoryItemIds, int Index)> _categoryPositions =
        new Dictionary<int, (int[], int)>();

    public CoinStoreItemCollection(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// <paramref name="itemId"/> and the items after it in its category, in the file's order: at most
    /// <paramref name="count"/> ids, and none when the coin store doesn't sell <paramref name="itemId"/>.
    /// </summary>
    public ReadOnlySpan<int> GetCategoryItemIdsFrom(int itemId, int count)
    {
        if (count <= 0 || !_categoryPositions.TryGetValue(itemId, out var position))
            return [];

        var (categoryItemIds, index) = position;

        return categoryItemIds.AsSpan(index, Math.Min(count, categoryItemIds.Length - index));
    }

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

            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var entries = JsonSerializer.Deserialize<List<ItemDefinitionMetaData>>(fileStream, jsonSerializerOptions);

            if (entries is null)
            {
                _logger.LogError("No entries found in file \"{file}\".", filePath);
                return false;
            }

            foreach (var entry in entries)
            {
                if (!TryAdd(entry.Id, entry))
                {
                    _logger.LogWarning("Failed to add entry. {id} \"{file}\"", entry.Id, filePath);
                    continue;
                }
            }

            IndexCategories(entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse file \"{file}\".", filePath);
            return false;
        }

        if (Count == 0)
        {
            _logger.LogError("No data was loaded. \"{file}\"", filePath);
            return false;
        }

        return true;
    }

    private void IndexCategories(List<ItemDefinitionMetaData> entries)
    {
        var categories = new Dictionary<int, List<int>>();
        var indexedItemIds = new HashSet<int>();

        foreach (var entry in entries)
        {
            // The first entry for an id is the one the collection keeps.
            if (!indexedItemIds.Add(entry.Id))
                continue;

            if (!categories.TryGetValue(entry.CategoryId, out var categoryItemIds))
                categories.Add(entry.CategoryId, categoryItemIds = []);

            categoryItemIds.Add(entry.Id);
        }

        var categoryPositions = new Dictionary<int, (int[], int)>(indexedItemIds.Count);

        foreach (var categoryItemIds in categories.Values)
        {
            var ids = categoryItemIds.ToArray();

            for (var index = 0; index < ids.Length; index++)
                categoryPositions.Add(ids[index], (ids, index));
        }

        _categoryPositions = categoryPositions;
    }
}