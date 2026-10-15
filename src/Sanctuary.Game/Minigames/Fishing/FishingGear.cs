using System.Collections.Generic;

namespace Sanctuary.Game.Minigames.Fishing;

/// <summary>How far a rod casts: the client allows a cast only when the water it aims at is between the two.</summary>
public readonly record struct CastRange(string RodName, float Min, float Max);

/// <summary>
/// Rods and lures. From edenfps' fishing (upstream PR #31); ids from the client's item definitions, effects from the
/// wiki (<c>docs/research/fishing/FISHING_WIKI.md</c>).
/// </summary>
public static class FishingGear
{
    /// <summary>The Fisherman's rod is equipped in this slot of the profile.</summary>
    public const int RodSlot = 7;

    /// <summary>
    /// The range without a known rod, and of the two "short distance" rods: the server's own default (3 to 20), the
    /// range that was played in game. Better rods only cast further; the distances are estimates.
    /// </summary>
    public static readonly CastRange DefaultCastRange = new("(no rod)", 3f, 20f);

    /// <summary>
    /// Whether the item is a fishing rod. Each of the five rods has 35 tints (68185 to 68359, 35 ids each, in the order
    /// below) and one more id (76687 to 76691).
    /// </summary>
    public static bool IsRod(int itemDefinitionId) =>
        itemDefinitionId is >= 68185 and <= 68359 or >= 76687 and <= 76691;

    /// <summary>The rod's cast range; anything that isn't a rod gets <see cref="DefaultCastRange"/>.</summary>
    public static CastRange CastRangeFor(int rodItemDefinitionId)
    {
        if (rodItemDefinitionId is >= 68185 and <= 68219 || rodItemDefinitionId == 76687)
            return DefaultCastRange with { RodName = "Simple Bamboo Fishing Rod" };

        if (rodItemDefinitionId is >= 68220 and <= 68254 || rodItemDefinitionId == 76688)
            return DefaultCastRange with { RodName = "Golden Reel Fishing Rod" };

        if (rodItemDefinitionId is >= 68255 and <= 68289 || rodItemDefinitionId == 76689)
            return new("Metal Fishing Rod", 3f, 25f);

        if (rodItemDefinitionId is >= 68290 and <= 68324 || rodItemDefinitionId == 76690)
            return new("Red Scoped Fishing Rod", 3f, 25f);

        if (rodItemDefinitionId is >= 68325 and <= 68359 || rodItemDefinitionId == 76691)
            return new("Golden Scoped Fishing Rod", 3f, 32f);

        return DefaultCastRange;
    }

    /// <summary>The Treasure Magnet raises the share of treasure instead of favouring fish.</summary>
    public const int TreasureMagnetItemId = 68164;

    /// <summary>Each lure's three fish, which it makes more likely. Names match <see cref="FishEntry.Name"/>.</summary>
    public static readonly IReadOnlyDictionary<int, string[]> LureFish = new Dictionary<int, string[]>
    {
        [68152] = ["Winter Piranha", "Toothy Tetra", "Blind Swurglefish"],          // 16oz Steak
        [68153] = ["Chilly Grouper", "Butter Flyfish", "Briar Nibbler"],            // Flyfisher 3000
        [68154] = ["Blubracuda", "Baconfish", "Changed Salmon"],                    // French Fry
        [68155] = ["Frostgill Smelt", "Flutterfish", "Ink Cod"],                    // Frostflies
        [68156] = ["Goofy Grouper", "Finless Fish", "Fanged Grouper"],              // Mega Slider
        [68157] = ["Plattypus", "Lady Tetra", "Roach Loach"],                       // Nightcrawlers
        [68158] = ["Pacu Pacu", "Peachy Perch", "Globfish"],                        // Perch Pinpointer
        [68159] = ["Coach Loach", "Feral Catfish", "Creeping Cod"],                 // Shiny Crankbait
        [68160] = ["Frozen Char", "Slugmud Skipper", "Old Sole"],                   // Skipper Seeker
        [68161] = ["Talking Bass", "Cheery Salmon", "Bitter Betta"],                // Sleek Clicker
        [68162] = ["Blue Thornfin", "Chipsen Fish", "Thorny Trout"],                // Thorn Jig
        [68163] = ["Spineless Stickleback", "Calico Catfish", "Purplenosed Shark"], // Tiny Rib
    };

    /// <summary>Whether the item is a lure: the twelve fish lures or the Treasure Magnet.</summary>
    public static bool IsLure(int itemDefinitionId) =>
        itemDefinitionId == TreasureMagnetItemId || LureFish.ContainsKey(itemDefinitionId);
}
