namespace Sanctuary.Game.Minigames.MatchThree;

/// <summary>
/// Tile values and categories of the match-three Flash games (Mining, Forging, Farming...). A tile's category is its
/// value rounded down to the hundred; within a category the rest picks the picture. These are the client's own numbers
/// (the games' <c>GameData</c> class), so they must not change.
/// </summary>
public static class MatchThreeTiles
{
    // Codes the level's grid shape uses: what to build in a cell when the board is made.
    public const int ShapeEmpty = 0;
    public const int ShapeRegular = 1;
    public const int ShapeAny = 2;
    public const int ShapeBlocker = 3;
    public const int ShapeDropZone = 4;

    // Asked-for categories: "any" and "regular" are rolled by the tile generator.
    public const int BuildRegular = -4;
    public const int BuildAny = -3;
    public const int Regular = -2;
    public const int Any = -1;

    public const int Empty = 0;
    public const int DropZone = 100;
    public const int Visible = 200;
    public const int Wildcard = 200;
    public const int Hazard = 300;
    public const int HazardLock = 301;
    public const int Selectable = 400;
    public const int SelectableEnd = 499;
    public const int Locked = 500;
    public const int LockDelta = Locked - Selectable;
    public const int FallTypeStart = 600;
    public const int Collectable = 600;
    public const int CollectableEnd = 699;
    public const int Bonus = 700;
    public const int BonusEnd = 799;
    public const int Blocker = 800;
    public const int Powerup = 900;
    public const int PowerupFreeze = 900;
    public const int PowerupScore2X = 901;
    public const int PowerupScore5X = 902;
    public const int FallTypeEnd = 949;

    /// <summary>Power-ups from here on are used by clicking them, not by chaining.</summary>
    public const int PowerupPressMe = 950;

    public const int PowerupDynamite = 950;
    public const int PowerupWildTile = 951;
    public const int PowerupDestruct = 952;
    public const int PowerupShuffle = 953;
    public const int TransformTypeStart = 999;
    public const int TransformTypeEnd = 1000;
    public const int LootDrop = 1000;
    public const int LootDropCommon = 1000;
    public const int LootDropSpecial = 1001;
    public const int LootDropRare = 1002;

    public static int CategoryOf(int value) => value / 100 * 100;

    public static bool IsPowerup(int value) => value is PowerupFreeze or PowerupScore2X or PowerupScore5X
        or PowerupDynamite or PowerupWildTile or PowerupDestruct or PowerupShuffle;
}

/// <summary>The level type bits the client reads from the level init message.</summary>
[System.Flags]
public enum MatchThreeLevelType
{
    None = 0,
    Drag = 1,
    StopAtQuota = 2,
    Meter = 4,
    StopGame = 8,
    BonusLevel = 16
}

/// <summary>Event types inside <c>OnLevelEventMsg</c>, both ways.</summary>
public static class MatchThreeEvents
{
    public const int LevelReshuffle = 11;
    public const int LevelNextWave = 12;
    public const int ScoreValue = 20;
    public const int ScoreIncrement = 21;
    public const int ScoreMultiplier = 22;
    public const int ScoreMultiplierExpired = 23;
    public const int TimerIncrement = 25;
    public const int MeterUpdate = 30;
    public const int MeterInit = 32;
    public const int PowerupFreeze = 40;
    public const int ClientFall = 50;
    public const int ClientConvert = 51;
    public const int ClientTileQueueAccept = 60;
    public const int ClientTileQueueReject = 61;

    public const int ReshuffleNoMoves = 1;

    public const int TimerChain = 1;

    public const int ScoreChain = 0;
    public const int ScoreBonus = 1;
    public const int ScorePowerup = 2;
}
