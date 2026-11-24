using System;
using System.Collections.Generic;
using System.IO;

using Microsoft.Extensions.Logging;

using nietras.SeparatedValues;

namespace Sanctuary.Game.Resources;

/// <summary>
/// The clothes a new character may start in: the outfits the game's character creator offers
/// (<c>Resources/CharacterCreate/StarterClothing.txt</c>, one outfit per line: feet, legs and chest item ids).
/// The creator lets a player pick each piece, so a piece is allowed in its own slot from any outfit.
/// </summary>
public class StarterClothingCollection
{
    private readonly ILogger _logger;

    // Replaced whole on every load, so a reader never sees a half-loaded list.
    private Slots _slots = new([], [], []);

    private sealed record Slots(HashSet<int> Feet, HashSet<int> Legs, HashSet<int> Chest);

    public StarterClothingCollection(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>How many outfits the last load read.</summary>
    public int Count { get; private set; }

    /// <summary>Whether a new character may start wearing these three items, each in its own slot.</summary>
    public bool Allows(int feet, int legs, int chest)
    {
        var slots = _slots;

        return slots.Feet.Contains(feet) && slots.Legs.Contains(legs) && slots.Chest.Contains(chest);
    }

    /// <summary>
    /// Reads the list. A missing file, a line that can't be read or a list with no outfits fails the load and keeps the
    /// list from before: an empty list would refuse every new character.
    /// </summary>
    public bool Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _logger.LogError("Failed to find file \"{file}\"", filePath);
            return false;
        }

        var slots = new Slots([], [], []);
        var count = 0;

        try
        {
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            using var reader = Sep.New('^')
                .Reader()
                .From(fileStream);

            foreach (var row in reader)
            {
                if (row.ColCount < 4
                    || !int.TryParse(row[0].Span, out _)
                    || !int.TryParse(row[1].Span, out var feet)
                    || !int.TryParse(row[2].Span, out var legs)
                    || !int.TryParse(row[3].Span, out var chest)
                    || feet <= 0 || legs <= 0 || chest <= 0)
                {
                    _logger.LogError("Failed to parse a line of \"{file}\".", filePath);
                    return false;
                }

                slots.Feet.Add(feet);
                slots.Legs.Add(legs);
                slots.Chest.Add(chest);
                count++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse file \"{file}\".", filePath);
            return false;
        }

        if (count == 0)
        {
            _logger.LogError("No starter outfits in \"{file}\".", filePath);
            return false;
        }

        _slots = slots;
        Count = count;

        return true;
    }
}
