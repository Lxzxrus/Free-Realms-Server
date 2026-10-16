using System.Collections.Generic;

namespace Sanctuary.Game.Minigames.Fishing;

/// <summary>
/// A fish a hole can give. <see cref="NameId"/> is the plain species name the catch banner and the Fish Finder show
/// (not the size-prefixed name of the item); <see cref="ItemId"/> is the item granted. <see cref="Size"/> is 1 small to
/// 4 extra large, <see cref="MinLevel"/> the Fisherman level the wiki gives for it (it only weights the roll for now),
/// and <see cref="Shape"/> picks the underwater model: 1 thin, 2 medium, 3 fat.
/// </summary>
public readonly record struct FishEntry(string Name, int ItemId, int NameId, int IconId, int Size, int MinLevel, int Shape);

/// <summary>
/// A catch that isn't a fish: treasure or junk. <see cref="CatchModel"/> is the model held up; one with a model of
/// its own (the treasure chest) waits on the hook to be reeled up, the rest bite like a fish.
/// </summary>
public readonly record struct MiscCatch(string Name, int ItemId, int NameId, int IconId, int Size, int CatchModel, float Weight);

/// <summary>
/// What each fishing hole gives, from the wiki's hole pages, with item, name and icon ids from the client's item
/// definitions. Keyed by the hole's activity id, which is also its zone definition id. From edenfps' fishing
/// (upstream PR #31; the data is in <c>docs/research/fishing/FISHING_WIKI.md</c>).
/// </summary>
public static class FishTables
{
    public const int TreasureChestModelId = 1624;

    /// <summary>Sacred Grove Shallows (563).</summary>
    public static readonly FishEntry[] SacredGroveShallows =
    [
        new("Slugmud Skipper", 68049, 99924, 4276, 1, 1, 1),
        new("Tickled Trout", 68001, 99963, 4422, 1, 1, 2),
        new("Flutterfish", 2868, 23278, 3737, 1, 1, 2),
        new("Butter Flyfish", 68053, 99925, 4254, 2, 5, 3),
        new("Cheery Salmon", 68063, 99927, 4257, 2, 10, 2),
        new("Chipsen Fish", 68067, 99928, 4258, 3, 14, 2),
        new("Feral Catfish", 68056, 99926, 4262, 3, 17, 3),
        new("Baconfish", 68069, 99929, 4249, 4, 20, 3),
        new("Tangletooth Shark", 68113, 407895, 4421, 4, 20, 1),
    ];

    /// <summary>Rainbow Lake (562).</summary>
    public static readonly FishEntry[] RainbowLake =
    [
        new("Flutterfish", 2868, 23278, 3737, 1, 1, 2),
        new("Calico Catfish", 2149, 6171, 4595, 1, 1, 3),
        new("Tickled Trout", 68001, 99963, 4422, 1, 1, 2),
        new("Toothy Tetra", 68066, 99923, 4281, 2, 5, 1),
        new("Peachy Perch", 68051, 99920, 4341, 2, 10, 2),
        new("Finless Fish", 68046, 99919, 4263, 3, 14, 1),
        new("Lady Tetra", 68061, 99922, 4270, 3, 17, 1),
        new("Tangletooth Shark", 68113, 407895, 4421, 4, 20, 1),
    ];

    /// <summary>Brambleback's Bayou (561).</summary>
    public static readonly FishEntry[] BramblebacksBayou =
    [
        new("Creeping Cod", 68081, 99930, 4260, 1, 1, 2),
        new("Bitter Betta", 68097, 99935, 4250, 1, 1, 2),
        new("Globfish", 2147, 6169, 4593, 1, 1, 3),
        new("Blind Swurglefish", 2865, 23275, 195, 2, 5, 1),
        new("Changed Salmon", 68094, 99940, 4256, 2, 10, 2),
        new("Fanged Grouper", 68091, 99939, 4261, 3, 14, 3),
        new("Briar Nibbler", 68101, 99941, 4556, 3, 17, 3),
    ];

    /// <summary>Darklit Lagoon (560).</summary>
    public static readonly FishEntry[] DarklitLagoon =
    [
        new("Ink Cod", 68073, 99931, 4635, 1, 1, 2),
        new("Creeping Cod", 68081, 99930, 4260, 1, 1, 2),
        new("Golden Scaled Nettler", 68009, 99971, 4420, 1, 1, 2),
        new("Thorny Trout", 68090, 99934, 4280, 2, 5, 2),
        new("Old Sole", 68087, 99933, 4271, 2, 10, 2),
        new("Purplenosed Shark", 68075, 99932, 4274, 3, 14, 1),
        new("Roach Loach", 68099, 99936, 4275, 3, 17, 1),
    ];

    /// <summary>Wintery Basin (564).</summary>
    public static readonly FishEntry[] WinteryBasin =
    [
        new("Frozen Char", 68027, 99945, 4266, 1, 1, 2),
        new("Winter Piranha", 68022, 99942, 4282, 1, 1, 2),
        new("Frostgill Smelt", 2864, 23274, 196, 2, 5, 1),
        new("Spineless Stickleback", 68039, 99947, 4277, 2, 10, 1),
        new("Blubracuda", 68036, 99946, 4252, 3, 14, 3),
        new("Coach Loach", 68024, 99944, 4259, 3, 17, 1),
        new("Ferocious Fangler", 68008, 99970, 4419, 4, 20, 3),
    ];

    /// <summary>Frostbitten Banks (565).</summary>
    public static readonly FishEntry[] FrostbittenBanks =
    [
        new("Chilly Grouper", 68011, 99948, 4267, 1, 1, 3),
        new("Winter Piranha", 68022, 99942, 4282, 1, 1, 2),
        new("Frostgill Smelt", 2864, 23274, 196, 2, 5, 1),
        new("Pacu Pacu", 68028, 99951, 4272, 2, 5, 2),
        new("Blue Thornfin", 68014, 99949, 4253, 2, 10, 1),
        new("Spineless Stickleback", 68039, 99947, 4277, 2, 10, 1),
        new("Goofy Grouper", 68030, 99952, 4269, 3, 14, 3),
        new("Talking Bass", 68019, 99950, 4279, 3, 17, 2),
        new("Ferocious Fangler", 68008, 99970, 4419, 4, 20, 3),
        new("Plattypus", 68033, 99953, 4273, 4, 20, 3),
    ];

    /// <summary>The fish of each hole, by activity id.</summary>
    public static readonly IReadOnlyDictionary<int, FishEntry[]> ByActivity = new Dictionary<int, FishEntry[]>
    {
        [560] = DarklitLagoon,
        [561] = BramblebacksBayou,
        [562] = RainbowLake,
        [563] = SacredGroveShallows,
        [564] = WinteryBasin,
        [565] = FrostbittenBanks,
    };

    /// <summary>
    /// Treasure and junk, the same in every hole. Junk has no model of its own, so it bites like a fish and turns out
    /// to be an old boot. The weights add up to <see cref="MiscCatchChance"/> in per cent, so each is its share of
    /// all casts: the chest (a house decoration the coin store sells for 50,000) comes up once in 50.
    /// </summary>
    public static readonly MiscCatch[] MiscCatches =
    [
        new("Treasure Chest", 3016, 5091, 6347, 3, TreasureChestModelId, 2f),
        new("Old Boot", 68103, 99954, 4430, 1, 0, 5f),
        new("Soggy Socks", 68104, 99955, 4435, 1, 0, 5f),
        new("Soggy Stick of Dynamite", 68002, 99964, 4436, 1, 0, 3f),
    ];

    /// <summary>The share of casts that bring up treasure or junk instead of a fish.</summary>
    public const float MiscCatchChance = 0.15f;

    /// <summary>The fish of the hole with this activity id; an unknown hole gets Sacred Grove Shallows'.</summary>
    public static FishEntry[] ForHole(int activityId) =>
        ByActivity.TryGetValue(activityId, out var fish) ? fish : SacredGroveShallows;
}
