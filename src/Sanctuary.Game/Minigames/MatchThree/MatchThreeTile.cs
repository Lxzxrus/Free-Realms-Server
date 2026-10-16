using System;
using System.Collections.Generic;

namespace Sanctuary.Game.Minigames.MatchThree;

public enum MatchThreeTileState
{
    Create,
    Idle,

    /// <summary>Landed or appeared; idle again once the client says its animation finished (<c>OnIdleMsg</c>).</summary>
    IdlePending,

    Appear,
    Disappear,
    Fall,
    Explode,
    Destroy
}

/// <summary>
/// One tile on the server's copy of the board (SOE's <c>ServerTile</c>). Changes of state take effect on the next
/// <see cref="Update"/>, which the board runs over every tile whenever it settles, so falls and explosions resolve at
/// once on the server while the client animates them.
/// </summary>
public sealed class MatchThreeTile
{
    private MatchThreeGrid? _grid;
    private int _rowsToFall;

    public MatchThreeTile(int category, int value, bool locked = false)
    {
        Category = category;
        Value = value;
        IsLocked = locked;
        State = MatchThreeTileState.Create;
    }

    public int Category { get; }

    /// <summary>The tile's value; a wildcard in a chain takes the chain's value.</summary>
    public int Value { get; private set; }

    public int X { get; private set; }
    public int Y { get; private set; }

    public MatchThreeTileState State { get; set; }

    public bool IsLocked { get; private set; }

    /// <summary>Counts toward the level when it explodes or lands.</summary>
    public bool IsCollect { get; private set; }

    public bool IsConvert { get; private set; }

    public bool IsFallOffBottom { get; private set; }

    /// <summary>What the client sees: a locked rock is its value plus <see cref="MatchThreeTiles.LockDelta"/>.</summary>
    public int NetworkValue => IsLocked && Category == MatchThreeTiles.Selectable ? Value + MatchThreeTiles.LockDelta : Value;

    public void SetGrid(MatchThreeGrid grid) => _grid = grid;

    public void SetPosition(int x, int y) => (X, Y) = (x, y);

    public void SetChainValue(int value) => Value = value;

    public void Lock() => IsLocked = true;

    public void Unlock() => IsLocked = false;

    public void AddCollect() => IsCollect = true;

    public void AddConvert() => IsConvert = true;

    public void AddFallOffBottom() => IsFallOffBottom = true;

    public void AddFall()
    {
        _rowsToFall++;
        State = MatchThreeTileState.Fall;
    }

    public void Convert()
    {
        if (!IsConvert)
            return;

        IsConvert = false;
        _grid?.DoWildTile(this);
    }

    public void Update()
    {
        switch (State)
        {
            case MatchThreeTileState.Create:
                State = MatchThreeTileState.Idle;
                break;

            case MatchThreeTileState.Appear:
                State = MatchThreeTileState.IdlePending;
                break;

            case MatchThreeTileState.Disappear:
                State = MatchThreeTileState.Destroy;
                break;

            case MatchThreeTileState.Explode:
                State = MatchThreeTileState.Destroy;
                if (IsCollect)
                    _grid?.OnTileCollect(this);
                break;

            case MatchThreeTileState.Fall:
                Y += _rowsToFall;
                _rowsToFall = 0;
                State = MatchThreeTileState.IdlePending;

                if (IsCollect)
                {
                    IsCollect = false;
                    _grid?.OnTileCollect(this);
                }
                break;
        }
    }

    public override string ToString() => $"{NetworkValue}@{X},{Y}";
}

/// <summary>
/// A weighted pick (SOE's <c>Randomizer</c>). Server-side only: the client never rolls, it gets the results through
/// the tile and random-number queues.
/// </summary>
public sealed class MatchThreeRandomizer
{
    private readonly List<(int Element, double Chance, double Odds)> _entries = [];
    private readonly Random _random;

    public MatchThreeRandomizer(Random random)
    {
        _random = random;
    }

    public double TotalChance { get; private set; }

    public void AddElement(int element, double chance)
    {
        if (double.IsNaN(chance) || chance <= 0)
            return;

        TotalChance += chance;
        _entries.Add((element, chance, 0));
    }

    /// <summary>Loot drops: "one in <paramref name="odds"/>" style weights.</summary>
    public void AddOdds(int element, double odds, double multiplier)
    {
        var scaled = odds * multiplier;
        if (scaled <= 0)
            return;

        if (scaled > TotalChance)
            TotalChance = scaled;

        _entries.Add((element, 0, scaled));

        for (var i = 0; i < _entries.Count; i++)
            _entries[i] = _entries[i] with { Chance = TotalChance / _entries[i].Odds };
    }

    /// <summary>An element by weight, or -1 if nothing can be picked.</summary>
    public int GetElement()
    {
        if (TotalChance <= 0)
            return -1;

        var remaining = TotalChance;
        var roll = Math.Floor(_random.NextDouble() * TotalChance);

        foreach (var entry in _entries)
        {
            remaining -= entry.Chance;
            if (roll >= remaining)
                return entry.Element;
        }

        return -1;
    }

    public void ChangeElementChance(int element, double chance)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Element != element)
                continue;

            TotalChance += chance - _entries[i].Chance;
            _entries[i] = _entries[i] with { Chance = chance };
            break;
        }
    }
}
