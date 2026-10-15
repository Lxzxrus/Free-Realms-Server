using System;

namespace Sanctuary.Game.Minigames.Fishing;

/// <summary>
/// What a cast will bring up, rolled when the line lands. From edenfps' fishing (upstream PR #31).
/// </summary>
public sealed record FishingCatch
{
    /// <summary>The underwater fish models: thin, medium and fat. The minigame has no model per species.</summary>
    public const int ThinFishModelId = 1670;
    public const int MediumFishModelId = 1671;
    public const int FatFishModelId = 1672;

    /// <summary>Composite effects played when the catch is held up: a sparkle for a fish, a burst for the chest.</summary>
    public const int FishEffectId = 15880;
    public const int ChestEffectId = 15881;

    public required string Name { get; init; }

    /// <summary>The item granted.</summary>
    public required int ItemId { get; init; }

    /// <summary>The name shown, a string id.</summary>
    public required int NameId { get; init; }

    public required int IconId { get; init; }

    /// <summary>1 small to 4 extra large.</summary>
    public required int Size { get; init; }

    /// <summary>The fish that swims in and bites.</summary>
    public required int UnderwaterModelId { get; init; }

    /// <summary>The model held up when it's caught.</summary>
    public required int HeldModelId { get; init; }

    /// <summary>Treasure or junk: held up as an item, without a size and weight.</summary>
    public required bool IsItem { get; init; }

    /// <summary>Already on the hook when the line lands (the chest): no bite, and it doesn't get away.</summary>
    public required bool WaitsOnHook { get; init; }

    public required int EffectId { get; init; }

    public required int Difficulty { get; init; }
    public required int Rarity { get; init; }
    public required int Score { get; init; }
    public required float Weight { get; init; }

    public static int UnderwaterModelForShape(int shape) => shape switch
    {
        1 => ThinFishModelId,
        2 => MediumFishModelId,
        _ => FatFishModelId
    };

    /// <summary>
    /// Rolls a catch in a hole. One cast in <see cref="FishTables.MiscCatchChance"/> brings up treasure or junk;
    /// otherwise a fish of the hole, where the higher a fish's level the rarer it is. A lure adds a tenth of the
    /// total chance to each of its fish; the Treasure Magnet adds a tenth to treasure and junk and quadruples the
    /// chest's share of them.
    /// </summary>
    public static FishingCatch Roll(int activityId, int lureItemId, Random random)
    {
        var magnet = lureItemId == FishingGear.TreasureMagnetItemId;
        var miscChance = magnet ? FishTables.MiscCatchChance + 0.10f : FishTables.MiscCatchChance;

        if (random.NextDouble() < miscChance)
            return RollMisc(magnet, random);

        var table = FishTables.ForHole(activityId);
        var lureFish = FishingGear.LureFish.TryGetValue(lureItemId, out var named) ? named : [];

        var weights = new float[table.Length];
        var total = 0f;

        for (var index = 0; index < table.Length; index++)
        {
            // About 1 at level 1, falling to about 0.24 at level 20.
            weights[index] = 6f / (table[index].MinLevel + 5f);
            total += weights[index];
        }

        var boost = 0.10f * total;

        for (var index = 0; index < table.Length; index++)
        {
            if (Array.IndexOf(lureFish, table[index].Name) < 0)
                continue;

            weights[index] += boost;
            total += boost;
        }

        var fish = table[Pick(weights, total, random)];
        var size = fish.Size;
        var model = UnderwaterModelForShape(fish.Shape);

        return new FishingCatch
        {
            Name = fish.Name,
            ItemId = fish.ItemId,
            NameId = fish.NameId,
            IconId = fish.IconId,
            Size = size,
            UnderwaterModelId = model,
            HeldModelId = model,
            IsItem = false,
            WaitsOnHook = false,
            EffectId = FishEffectId,
            Difficulty = 1,
            Rarity = Math.Clamp(fish.MinLevel / 5 + 1, 1, 5),
            Score = 10 * size,
            Weight = size * 1.5f + (float)random.NextDouble() * size * 2f
        };
    }

    private static FishingCatch RollMisc(bool magnet, Random random)
    {
        var catches = FishTables.MiscCatches;

        var weights = new float[catches.Length];
        var total = 0f;

        for (var index = 0; index < catches.Length; index++)
        {
            weights[index] = magnet && catches[index].CatchModel == FishTables.TreasureChestModelId
                ? catches[index].Weight * 4f
                : catches[index].Weight;
            total += weights[index];
        }

        var pick = catches[Pick(weights, total, random)];
        var waitsOnHook = pick.CatchModel > 0;

        return new FishingCatch
        {
            Name = pick.Name,
            ItemId = pick.ItemId,
            NameId = pick.NameId,
            IconId = pick.IconId,
            Size = pick.Size,
            UnderwaterModelId = MediumFishModelId,
            HeldModelId = waitsOnHook ? pick.CatchModel : MediumFishModelId,
            IsItem = true,
            WaitsOnHook = waitsOnHook,
            EffectId = waitsOnHook ? ChestEffectId : FishEffectId,
            Difficulty = 1,
            Rarity = 1,
            Score = 5 * pick.Size,
            Weight = pick.Size * 1.5f + (float)random.NextDouble() * pick.Size
        };
    }

    private static int Pick(float[] weights, float total, Random random)
    {
        var roll = (float)random.NextDouble() * total;

        for (var index = 0; index < weights.Length - 1; index++)
        {
            if (roll < weights[index])
                return index;

            roll -= weights[index];
        }

        return weights.Length - 1;
    }
}
