using System;

namespace Sanctuary.Game.Minigames.MatchThree;

/// <summary>
/// Rolls new tiles for a level (SOE's <c>ServerTileGenerator</c>): first the kind (rock, ore, gem, power-up...) by the
/// level's spawn chances, then the type within the kind by each type's chance. Tells the client which picture each
/// rock, ore and gem value uses.
/// </summary>
public sealed class MatchThreeTileGenerator
{
    /// <summary>Loot drop odds are scaled by this.</summary>
    private const int LootRate = 10;

    private readonly MatchThreeLevel _level;
    private readonly Random _random;

    private readonly MatchThreeRandomizer _lootDrops;
    private readonly MatchThreeRandomizer _collectables;
    private readonly MatchThreeRandomizer _bonuses;
    private readonly MatchThreeRandomizer _selectables;
    private readonly MatchThreeRandomizer _powerups;
    private readonly MatchThreeRandomizer _hazards;
    private readonly MatchThreeRandomizer _blockers;
    private readonly MatchThreeRandomizer _wildcards;

    private MatchThreeRandomizer _kinds = null!;
    private MatchThreeRandomizer _regularKinds = null!;

    public MatchThreeTileGenerator(MatchThreeLevel level, Random random, IMatchThreeOutput output)
    {
        _level = level;
        _random = random;

        SetBaseRandomizers();

        // No loot drops in Evergrove's levels yet; kept so "any" tiles roll the way the client expects.
        _lootDrops = new MatchThreeRandomizer(random);

        _collectables = new MatchThreeRandomizer(random);
        foreach (var spawn in level.Collectables.Values)
        {
            _collectables.AddElement(spawn.Value, spawn.Definition.Chance);
            output.Send("OnTileInfoMsg", spawn.Value, spawn.Definition.Type);
        }

        _bonuses = new MatchThreeRandomizer(random);
        foreach (var spawn in level.Bonuses.Values)
        {
            _bonuses.AddElement(spawn.Value, spawn.Definition.Chance);
            output.Send("OnTileInfoMsg", spawn.Value, spawn.Definition.Type);
        }

        _selectables = new MatchThreeRandomizer(random);
        foreach (var spawn in level.Selectables.Values)
        {
            _selectables.AddElement(spawn.Value, spawn.Definition.Chance);
            output.Send("OnTileInfoMsg", spawn.Value, spawn.Definition.Type);
        }

        _powerups = new MatchThreeRandomizer(random);
        foreach (var spawn in level.Powerups.Values)
            _powerups.AddElement(spawn.Value, spawn.Definition.Chance);

        _hazards = new MatchThreeRandomizer(random);
        _hazards.AddElement(MatchThreeTiles.HazardLock, 100);

        _blockers = new MatchThreeRandomizer(random);
        _blockers.AddElement(MatchThreeTiles.Blocker, 100);

        _wildcards = new MatchThreeRandomizer(random);
        _wildcards.AddElement(MatchThreeTiles.Wildcard, 100);
    }

    /// <summary>The kind weights, again at each new wave (they can grow with the level's progression).</summary>
    public void SetBaseRandomizers()
    {
        var chances = _level.SpawnChances;

        _kinds = new MatchThreeRandomizer(_random);
        _kinds.AddElement(MatchThreeTiles.Collectable, chances.Collectable);
        _kinds.AddElement(MatchThreeTiles.Bonus, chances.Bonus);
        _kinds.AddElement(MatchThreeTiles.Selectable, chances.Selectable);
        _kinds.AddElement(MatchThreeTiles.Powerup, chances.Powerup);
        _kinds.AddElement(MatchThreeTiles.Blocker, chances.Blocker);
        _kinds.AddElement(MatchThreeTiles.Hazard, chances.Hazard);
        _kinds.AddElement(MatchThreeTiles.Wildcard, chances.Wildcard);

        _regularKinds = new MatchThreeRandomizer(_random);
        _regularKinds.AddElement(MatchThreeTiles.Selectable, chances.Selectable);
        _regularKinds.AddElement(MatchThreeTiles.Hazard, chances.Hazard);
        _regularKinds.AddElement(MatchThreeTiles.Wildcard, chances.Wildcard);
    }

    public MatchThreeTile Generate(int category, int value)
    {
        // A level always has rocks (MatchThreeLevelDefinition.Problems), so a retry with "any" ends; the bound is a
        // backstop against a definition that slipped through.
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            if (category == MatchThreeTiles.Any)
            {
                value = _lootDrops.GetElement();
                category = value >= MatchThreeTiles.LootDrop ? MatchThreeTiles.LootDrop : _kinds.GetElement();
            }
            else if (category is MatchThreeTiles.Regular or MatchThreeTiles.BuildRegular)
                category = _regularKinds.GetElement();
            else if (category == MatchThreeTiles.BuildAny)
                category = _kinds.GetElement();

            var tile = category switch
            {
                MatchThreeTiles.Empty => new MatchThreeTile(category, MatchThreeTiles.Empty),
                MatchThreeTiles.DropZone => new MatchThreeTile(category, MatchThreeTiles.DropZone),
                MatchThreeTiles.Blocker => FromRandomizer(category, _blockers),
                MatchThreeTiles.Collectable => Counted(category, _collectables),
                MatchThreeTiles.Bonus => Counted(category, _bonuses),
                MatchThreeTiles.Powerup => FromRandomizer(category, _powerups),
                MatchThreeTiles.Hazard => GenerateHazard(),
                MatchThreeTiles.Wildcard => FromRandomizer(category, _wildcards),
                MatchThreeTiles.Selectable => Counted(category, _selectables),
                MatchThreeTiles.LootDrop => new MatchThreeTile(category, value),
                _ => null
            };

            if (tile is not null)
                return tile;

            category = MatchThreeTiles.Any;
        }

        throw new InvalidOperationException("The level can't make any tile.");
    }

    public static MatchThreeTile Specific(int category, int value) => new(category, value);

    private static MatchThreeTile? FromRandomizer(int category, MatchThreeRandomizer randomizer)
    {
        var value = randomizer.GetElement();
        return value == -1 ? null : new MatchThreeTile(category, value);
    }

    /// <summary>A type that reaches its level's create limit stops being rolled.</summary>
    private MatchThreeTile? Counted(int category, MatchThreeRandomizer randomizer)
    {
        var value = randomizer.GetElement();
        if (value == -1)
            return null;

        if (!_level.CreatedTile(category, value))
            randomizer.ChangeElementChance(value, 0);

        return new MatchThreeTile(category, value);
    }

    /// <summary>The one hazard: a rock under a lock, which a chain unlocks instead of breaking.</summary>
    private MatchThreeTile? GenerateHazard()
    {
        if (_hazards.GetElement() != MatchThreeTiles.HazardLock)
            return null;

        var tile = Counted(MatchThreeTiles.Selectable, _selectables);
        tile?.Lock();
        return tile;
    }
}
