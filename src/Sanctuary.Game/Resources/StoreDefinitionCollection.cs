using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Collections;
using Sanctuary.Packet.Common.GameCommerce;

namespace Sanctuary.Game.Resources;

public class StoreDefinitionCollection : ObservableConcurrentDictionary<int, StoreDefinition>
{
    private readonly ILogger _logger;

    // The items of every tintable bundle, gathered when the bundles load, so a client asking for item definitions
    // doesn't walk all of every store's bundles for each id it names.
    private volatile IReadOnlySet<int> _tintableBundleItemIds = new HashSet<int>();

    public StoreDefinitionCollection(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Whether a tintable bundle, one the buyer picks a colour for, holds <paramref name="marketingItemId"/>.</summary>
    public bool IsInTintableBundle(int marketingItemId) => _tintableBundleItemIds.Contains(marketingItemId);

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

            var entries = JsonSerializer.Deserialize<List<StoreDefinition>>(fileStream, jsonSerializerOptions);

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

    public bool LoadBundles(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("Failed to find file \"{file}\"", filePath);
            return false;
        }

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var streamReader = new StreamReader(fileStream);

            var entries = JsonSerializer.Deserialize<List<AppStoreBundleDefinition>>(streamReader.ReadToEnd());

            if (entries is null)
            {
                _logger.LogError("No entries found in file \"{file}\".", filePath);
                return false;
            }

            foreach (var entry in entries)
            {
                if (!TryGetValue(entry.StoreId, out var storeDefinition))
                {
                    _logger.LogWarning("Failed to add entry. {id} {storeId} \"{file}\"", entry.Id, entry.StoreId, filePath);
                    continue;
                }

                storeDefinition.Bundles.Add(entry.Id, entry);
            }

            _tintableBundleItemIds = Values
                .SelectMany(storeDefinition => storeDefinition.Bundles.Values)
                .Where(appStoreBundleDefinition => appStoreBundleDefinition.IsTintable)
                .SelectMany(appStoreBundleDefinition => appStoreBundleDefinition.Entries)
                .Select(entry => entry.MarketingItemId)
                .ToHashSet();
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
}