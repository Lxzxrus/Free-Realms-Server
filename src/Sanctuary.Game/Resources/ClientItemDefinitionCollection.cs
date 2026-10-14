using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Collections;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Resources;

public class ClientItemDefinitionCollection : ObservableConcurrentDictionary<int, ClientItemDefinition>
{
    private readonly ILogger _logger;

    public ClientItemDefinitionCollection(ILogger logger)
    {
        _logger = logger;
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

            var entries = JsonSerializer.Deserialize<List<ClientItemDefinition>>(fileStream, jsonSerializerOptions);

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

    /// <summary>
    /// The version of <paramref name="item"/> made for <paramref name="gender"/> (1 boys, 2 girls), or the item itself
    /// when it fits anyone or no other version exists. Clothes come in a boys' and a girls' item with the same name,
    /// colour and trade rules: collection rewards from the 2014 capture are the boys' ones, which a girl can't wear
    /// (Ariel, 2026-10-13: Traveler's Boots, Seeker's Hat).
    /// </summary>
    public ClientItemDefinition ForGender(ClientItemDefinition item, int gender)
    {
        if (item.GenderUsage == 0 || item.GenderUsage == gender)
            return item;

        ClientItemDefinition? sameColour = null;
        ClientItemDefinition? anyColour = null;

        foreach (var candidate in Values)
        {
            if (candidate.NameId != item.NameId || candidate.GenderUsage != gender || candidate.Slot != item.Slot)
                continue;

            // Some girls' versions don't come in every boys' colour (Pathfinder's Boots); a wearable one beats none.
            if (candidate.Icon.TintId != item.Icon.TintId)
            {
                if (anyColour is null || candidate.Id < anyColour.Id)
                    anyColour = candidate;
                continue;
            }

            if (candidate.NoTrade == item.NoTrade && candidate.NoSale == item.NoSale)
                return candidate;

            sameColour ??= candidate;
        }

        return sameColour ?? anyColour ?? item;
    }
}
