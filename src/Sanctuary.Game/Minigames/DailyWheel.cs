using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Sanctuary.Game.Minigames;

/// <summary>One wedge of the daily wheel: the picture it shows (a frame of the wheel's Flash) and what it pays.</summary>
public sealed class DailyWheelSlot
{
    /// <summary>Frame of the wheel's <c>mcCategory</c> sprite (1 to 25) drawn on this wedge.</summary>
    public int Category { get; set; }

    public int Coins { get; set; }

    /// <summary>Relative chance of landing here; a slot with weight 0 never pays out.</summary>
    public int Weight { get; set; }
}

/// <summary>
/// Spin For The Win, the daily wheel (activity 8, <c>game_wheel.gfx</c>). The prize table is Evergrove's own: the
/// live game's tables were server data and aren't in the client. Loaded from <c>Resources/DailyWheel.json</c>.
/// </summary>
public sealed class DailyWheel
{
    public const int ActivityId = 8;

    /// <summary>Wheel type 1 is the ten-wedge wheel in the Flash file (<c>mcCategory1</c> to <c>mcCategory10</c>).</summary>
    public const int TenSlotType = 1;

    public const string RepeatingActivityName = "wheel";

    /// <summary>
    /// Off until the wheel can be drawn: 82 of the 83 images inside game_wheel.gfx are in neither the client's packs nor
    /// its asset list (OSFR never had them), so the wheel opened as an empty frame. Off, no spin is offered and nothing
    /// opens; everything else stays ready.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public int WheelId { get; set; } = 1;
    public int WheelType { get; set; } = TenSlotType;

    /// <summary>The wheel's title, description and message: "Spin For The Win!", "Welcome to the Free Realms Daily
    /// Rewards wheel!..." and "Come back every day...".</summary>
    public int NameId { get; set; }
    public int DescriptionId { get; set; }
    public int MessageId { get; set; }

    /// <summary>Picture, name and tooltip of a coin prize in the reward window.</summary>
    public int PrizeIconId { get; set; }
    public int PrizeNameId { get; set; }

    public List<DailyWheelSlot> Slots { get; set; } = [];

    /// <summary>Whether a character who last spun at <paramref name="lastSpin"/> may spin now: once per UTC day.</summary>
    public static bool CanSpin(DateTimeOffset? lastSpin, DateTimeOffset now)
        => lastSpin is null || lastSpin.Value.UtcDateTime.Date < now.UtcDateTime.Date;

    /// <summary>Time until the next spin is allowed: zero when one is, else until the next UTC midnight.</summary>
    public static TimeSpan UntilNextSpin(DateTimeOffset? lastSpin, DateTimeOffset now)
        => CanSpin(lastSpin, now) ? TimeSpan.Zero : now.UtcDateTime.Date.AddDays(1) - now.UtcDateTime;

    /// <summary>The wedges' pictures, space separated, as the wheel's <c>OnWheelDataMsg</c> takes them.</summary>
    public string SlotList() => string.Join(' ', Slots.Select(slot => slot.Category)) + " ";

    /// <summary>Picks a wedge by weight.</summary>
    public int Roll(Random random)
    {
        var total = Slots.Sum(slot => slot.Weight);
        var pick = random.Next(total);

        for (var index = 0; index < Slots.Count; index++)
        {
            pick -= Slots[index].Weight;
            if (pick < 0)
                return index;
        }

        return Slots.Count - 1;
    }

    /// <summary>Mistakes that would draw a broken wheel or one that can't pay.</summary>
    public IEnumerable<string> Problems()
    {
        if (WheelType == TenSlotType && Slots.Count != 10)
            yield return $"wheel type {TenSlotType} has 10 wedges, but {Slots.Count} slots are listed";

        if (Slots.Any(slot => slot.Category is < 1 or > 25))
            yield return "a slot's Category is outside the wheel's 25 pictures";

        if (Slots.Any(slot => slot.Coins < 0 || slot.Weight < 0))
            yield return "a slot has negative Coins or Weight";

        if (Slots.Sum(slot => slot.Weight) <= 0)
            yield return "no slot has any weight, so a spin can't land anywhere";
    }

    public static bool TryLoad(string filePath, ILogger logger, out DailyWheel wheel)
    {
        wheel = new DailyWheel();

        try
        {
            using var stream = File.OpenRead(filePath);
            var loaded = JsonSerializer.Deserialize<DailyWheel>(stream, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (loaded is null)
            {
                logger.LogError("No daily wheel in \"{file}\".", filePath);
                return false;
            }

            var problems = loaded.Problems().ToList();
            foreach (var problem in problems)
                logger.LogError("Daily wheel in \"{file}\": {problem}.", filePath, problem);

            if (problems.Count > 0)
                return false;

            wheel = loaded;
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load the daily wheel from \"{file}\".", filePath);
            return false;
        }
    }
}
