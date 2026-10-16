using System;
using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.Game.Minigames.MatchThree;

/// <summary>
/// The server's copy of a match-three board (a port of SOE's <c>ServerGrid</c>, from the server code inside the games'
/// own Flash files). The client and the server play the board in lockstep: both draw new tiles from the same tile
/// queue and both take the same random numbers, which only the server makes, so each can replay the other's moves.
/// The client sends each chain and power-up it plays; the server checks it against its own board before it counts.
/// </summary>
/// <remarks>
/// The fall rules keep two quirks of the client's own code: column 0 and row 0 are never looked at, so level grids
/// leave them empty. Positions in messages are x, y with y growing downwards.
/// </remarks>
public sealed class MatchThreeGrid
{
    public const int TileQueueBatchSize = 20;
    public const int TileQueueMinLength = 50;
    public const int RandomQueueBatchSize = 20;
    public const int RandomQueueMinLength = 50;
    public const int RandomMaxValue = 999;

    /// <summary>A board that settles without a move is rebuilt; this stops a level that can never have one.</summary>
    private const int MaxReshufflesPerSettle = 20;

    /// <summary>A client that keeps rejecting the tile queue isn't sent it again after this many tries.</summary>
    public const int MaxTileQueueRejects = 10;

    private readonly MatchThreeLevel _level;
    private readonly MatchThreeTileGenerator _generator;
    private readonly IMatchThreeOutput _output;
    private readonly Random _random;
    private readonly int[][] _shape;

    private readonly List<MatchThreeTile> _tiles = [];
    private readonly List<MatchThreeTile> _chain = [];

    private Queue<MatchThreeTile> _tileQueue = new();
    private List<MatchThreeTile> _tilePendingQueue = [];
    private List<int> _tileUnsentValues = [];
    private int _tileQueueLength;

    /// <summary>A queue reset was sent and the client hasn't accepted it yet.</summary>
    private bool _resetOutstanding;

    private readonly Queue<int> _randomNumberQueue = new();
    private List<int> _unsentRandomNumbers = [];

    private MatchThreeTile?[,] _current;
    private List<MatchThreeTile> _randomTiles = [];

    private bool _hasExploded;
    private int _reshufflesThisSettle;
    private int _settleDepth;

    public MatchThreeGrid(MatchThreeLevel level, MatchThreeTileGenerator generator, int[][] shape, IMatchThreeOutput output, Random random)
    {
        _level = level;
        _generator = generator;
        _shape = shape;
        _output = output;
        _random = random;

        Height = shape.Length;
        Width = shape[0].Length;
        _current = new MatchThreeTile?[Height + 1, Width];

        BuildMap();

        GenerateTilesForQueue(TileQueueMinLength + TileQueueBatchSize, MatchThreeTiles.Any, MatchThreeTiles.Any);
        SendTileQueue(reset: false);

        GenerateRandomNumbersForQueue(RandomQueueMinLength + RandomQueueBatchSize);
        SendRandomNumberQueue();
    }

    public int Width { get; }
    public int Height { get; }

    public int TileQueueRejects { get; private set; }

    /// <summary>Times the board was rebuilt because no move was left.</summary>
    public int Reshuffles { get; private set; }

    public MatchThreeTile? At(int x, int y) =>
        x >= 0 && y >= 0 && y <= Height && x < Width ? _current[y, x] : null;

    private void Place(int x, int y, MatchThreeTile? tile)
    {
        if (x >= 0 && y >= 0 && y <= Height && x < Width)
            _current[y, x] = tile;
    }

    /// <summary>The tiles still in play, for tests and logs.</summary>
    public IEnumerable<MatchThreeTile> Tiles => _tiles.Where(tile => tile.State != MatchThreeTileState.Destroy);

    // ---- Messages from the client ----

    /// <summary>The client's animations finished for these tiles (x, y pairs): they can be used again.</summary>
    public void NowIdle(IReadOnlyList<int> positions)
    {
        for (var i = 0; i + 1 < positions.Count; i += 2)
        {
            var tile = At(positions[i], positions[i + 1]);
            if (tile?.State is MatchThreeTileState.IdlePending or MatchThreeTileState.Appear)
                tile.State = MatchThreeTileState.Idle;
        }
    }

    /// <summary>A chain the player drew (x, y pairs). False if it isn't one on this board.</summary>
    public bool ValidateAndRemoveChain(IReadOnlyList<int> positions)
    {
        _chain.Clear();

        var length = positions.Count / 2;
        if (length < MatchThreeLevel.MinimumChainLength)
            return false;

        if (!CheckCurrentTile(positions[0], positions[1]))
            return false;

        for (var i = 1; i < length; i++)
        {
            if (!CheckNextTile(positions[i * 2], positions[i * 2 + 1]))
            {
                _chain.Clear();
                UpdateCurrentGridArray();
                return false;
            }
        }

        DoMatch3();
        return true;
    }

    /// <summary>An ore piece the client saw fall out through the drop zone.</summary>
    public bool FallOffBottom(int x, int y)
    {
        var tile = At(x, y);
        if (tile is null || !tile.IsFallOffBottom)
            return false;

        tile.AddCollect();
        tile.State = MatchThreeTileState.Explode;
        _hasExploded = true;
        UpdateCurrentGridArray();
        return true;
    }

    public bool Convert(int x, int y)
    {
        var tile = At(x, y);
        if (tile is null || !tile.IsConvert)
            return false;

        tile.Convert();
        return true;
    }

    /// <summary>The client's whole board (x, y, value triples). False if a tile both sides have differs.</summary>
    public bool CheckGrid(IReadOnlyList<int> triples)
    {
        for (var i = 0; i + 2 < triples.Count; i += 3)
        {
            var tile = At(triples[i], triples[i + 1]);
            if (tile is null)
                continue;

            var mine = tile.NetworkValue;
            var theirs = triples[i + 2];

            if (mine != 0 && theirs != 0 && mine != theirs)
                return false;
        }

        return true;
    }

    public void TileQueueAccept()
    {
        if (!_resetOutstanding)
            return;

        _resetOutstanding = false;
        _tileQueue.Clear();
        _tileQueueLength = 0;
        TileQueueAddPending();
    }

    public void TileQueueReject()
    {
        if (!_resetOutstanding || ++TileQueueRejects > MaxTileQueueRejects)
            return;

        SendTileQueue(reset: true);
    }

    // ---- Power-ups ----

    /// <summary>Dynamite: the clicked tile, then neighbours of what's gone, picked with the shared random numbers.</summary>
    public bool StartShatter(IReadOnlyList<int> positions, int power)
    {
        var remaining = power;
        var count = positions.Count / 2;

        if (count > power || count == 0)
            return false;

        if (count < power)
            remaining = count;

        var next = new List<MatchThreeTile>();
        var later = new List<MatchThreeTile>();
        var index = 0;
        var tile = At(positions[0], positions[1]);

        while (remaining > 0 && tile is not null)
        {
            if (tile.State != MatchThreeTileState.Idle)
                return false;

            var (x, y) = (tile.X, tile.Y);
            tile.State = MatchThreeTileState.Explode;
            _hasExploded = true;
            _tiles.Remove(tile);
            Place(x, y, null);

            foreach (var neighbour in new[] { At(x, y + 1), At(x, y - 1), At(x + 1, y), At(x - 1, y) })
            {
                if (neighbour is null || neighbour.State != MatchThreeTileState.Idle)
                    continue;

                if (neighbour.Category is MatchThreeTiles.Selectable or MatchThreeTiles.Wildcard or MatchThreeTiles.Blocker && !later.Contains(neighbour))
                    later.Add(neighbour);
            }

            remaining--;
            if (remaining <= 0)
                break;

            if (next.Count == 0)
            {
                next = [.. later];
                later = [];
            }

            index++;
            tile = At(positions[index * 2], positions[index * 2 + 1]);

            // Both sides pick the next tile with the same random number; the client's pick must be the server's.
            var pick = GetRandomNumberFromQueue(0, next.Count - 1);
            if (next.Count == 0 || tile != next[pick])
                return false;

            next.RemoveAt(pick);
        }

        UpdateCurrentGridArray();
        return true;
    }

    /// <summary>Wild tiles: the power-up and random rocks become wildcards.</summary>
    public bool StartWildTiles(IReadOnlyList<int> positions, int power)
    {
        if (positions.Count / 2 > power || positions.Count < 2)
            return false;

        var tile = At(positions[0], positions[1]);
        InitRandomTiles(MatchThreeTiles.Selectable, MatchThreeTileState.Idle);

        for (var i = 0; i < power; i++)
        {
            if (i != 0 || tile is null)
                tile = GetRandomTile();

            if (tile is null)
                break;

            if (ClientTile(positions, i) != tile)
                return false;

            DoWildTile(tile);
        }

        UpdateCurrentGridArray();
        return true;
    }

    /// <summary>Destruct: the power-up and random rocks and blockers break, without counting.</summary>
    public bool StartDestruction(IReadOnlyList<int> positions, int power)
    {
        if (positions.Count / 2 > power || positions.Count < 2)
            return false;

        var tile = At(positions[0], positions[1]);
        InitRandomTiles(MatchThreeTiles.Selectable, MatchThreeTileState.Idle);
        AddRandomTiles(MatchThreeTiles.Blocker, MatchThreeTileState.Idle);

        for (var i = 0; i < power; i++)
        {
            if (i != 0 || tile is null)
                tile = GetRandomTile();

            if (tile is null)
                break;

            if (ClientTile(positions, i) != tile)
                return false;

            _tiles.Remove(tile);
            tile.State = MatchThreeTileState.Explode;
            _hasExploded = true;
        }

        UpdateCurrentGridArray();
        return true;
    }

    /// <summary>Shuffle: the power-up becomes a wildcard and random tiles swap places in a ring.</summary>
    public bool StartShuffle(IReadOnlyList<int> positions, int power)
    {
        if (positions.Count < 2)
            return false;

        var count = power;
        var list = positions.Skip(2).ToList();

        var powerupTile = At(positions[0], positions[1]);
        if (powerupTile is null)
            return false;

        DoWildTile(powerupTile);
        count--;

        InitRandomTiles(null, MatchThreeTileState.Idle);
        if (_randomTiles.Count < count)
            count = _randomTiles.Count;

        if (list.Count / 2 > count)
            return false;

        count = Math.Max(count, 0);
        var places = new (int X, int Y)?[count];
        var moved = new MatchThreeTile[count];

        for (var i = 0; i < count; i++)
        {
            var tile = GetRandomTile();
            if (tile is null || ClientTile(list, i) != tile)
                return false;

            places[i] = (tile.X, tile.Y);
            moved[(i + 1) % count] = tile;
            tile.State = MatchThreeTileState.Disappear;
        }

        for (var i = 0; i < count; i++)
        {
            var tile = moved[i];
            var place = places[i];
            var occupant = place is { } p ? At(p.X, p.Y) : null;
            var skip = 0;

            // A tile doesn't move onto one of its own kind if another place in the ring is free of it.
            while (occupant is not null && occupant.Value == tile.Value && i + skip < count)
            {
                skip++;
                place = i + skip < count ? places[i + skip] : null;
                occupant = place is { } q ? At(q.X, q.Y) : null;
            }

            if (i + skip >= count)
                place = places[i];
            else if (skip != 0)
                places[i + skip] = places[i];

            if (place is { } target)
                tile.SetPosition(target.X, target.Y);

            tile.State = MatchThreeTileState.Appear;
        }

        _hasExploded = true;
        UpdateCurrentGridArray();
        return true;
    }

    /// <summary>Loot drops reshuffle the board.</summary>
    public bool StartLootDrop()
    {
        LevelReshuffle();
        return true;
    }

    // ---- Level changes ----

    public void NextWave()
    {
        TileQueueRejects = 0;
        _generator.SetBaseRandomizers();
        GenerateTilesForQueue(TileQueueMinLength + TileQueueBatchSize, MatchThreeTiles.Any, MatchThreeTiles.Any);
        SendTileQueue(reset: true);
    }

    /// <summary>A new board: when no move is left, and to bring a client that fell out of step back in line.</summary>
    public void LevelReshuffle()
    {
        Reshuffles++;

        foreach (var tile in _tiles)
            tile.State = MatchThreeTileState.Destroy;

        _tiles.Clear();
        _current = new MatchThreeTile?[Height + 1, Width];

        _output.Send("OnLevelEventMsg", MatchThreeEvents.LevelReshuffle, MatchThreeEvents.ReshuffleNoMoves);

        BuildMap();
        GenerateTilesForQueue(TileQueueMinLength + TileQueueBatchSize, MatchThreeTiles.Any, MatchThreeTiles.Any);
        SendTileQueue(reset: false);
    }

    // ---- Board building and settling ----

    private void BuildMap()
    {
        _tileQueue = new Queue<MatchThreeTile>();
        _resetOutstanding = false;
        _tilePendingQueue = [];
        _tileQueueLength = 0;
        _tileUnsentValues = [];

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var code = _shape[y][x];

                var (category, value) = code switch
                {
                    MatchThreeTiles.ShapeEmpty => (MatchThreeTiles.Empty, -1),
                    MatchThreeTiles.ShapeRegular => (MatchThreeTiles.BuildRegular, -1),
                    MatchThreeTiles.ShapeAny => (MatchThreeTiles.BuildAny, -1),
                    MatchThreeTiles.ShapeBlocker => (MatchThreeTiles.Blocker, -1),
                    MatchThreeTiles.ShapeDropZone => (MatchThreeTiles.DropZone, -1),
                    < 0 => (MatchThreeTiles.CategoryOf(-code), -code),
                    _ => (MatchThreeTiles.CategoryOf(code), -1)
                };

                var tile = _generator.Generate(category, value);
                tile.SetPosition(x, y);
                tile.SetGrid(this);
                _tileUnsentValues.Add(tile.NetworkValue);
                _tiles.Add(tile);
            }
        }

        SendTileQueue(reset: false);
        _output.Send("OnBuildMapMsg", Width, Height, string.Join(',', _shape.SelectMany(row => row)));
        UpdateCurrentGridArray();
    }

    /// <summary>Applies every tile's pending change, refills holes and rebuilds the board if no move is left.</summary>
    private void UpdateCurrentGridArray()
    {
        _settleDepth++;
        try
        {
            Settle();
        }
        finally
        {
            if (--_settleDepth == 0)
                _reshufflesThisSettle = 0;
        }
    }

    private void Settle()
    {
        _current = new MatchThreeTile?[Height + 1, Width];

        // Copy: a collected tile can start a new wave, which only queues tiles, but keep the loop independent of it.
        foreach (var tile in _tiles.ToArray())
        {
            tile.Update();
            if (tile.State != MatchThreeTileState.Destroy)
                Place(tile.X, tile.Y, tile);
        }

        // Spent tiles are never looked at again; the newest tiles stay last, which the refill relies on.
        _tiles.RemoveAll(tile => tile.State == MatchThreeTileState.Destroy);

        if (_hasExploded)
        {
            _hasExploded = false;
            if (CheckFalls())
            {
                Settle();
                return;
            }
        }

        if (!HasMove() && _reshufflesThisSettle++ < MaxReshufflesPerSettle)
            LevelReshuffle();

        if (CheckFalls())
            Settle();
    }

    private bool CheckFalls()
    {
        var hasFall = false;

        for (var x = Width - 1; x > 0; x--)
        {
            // The row new tiles appear in: just above the column's first non-empty cell.
            var firstNotEmpty = 0;
            var columnEmpty = true;
            while (firstNotEmpty < Height && columnEmpty)
            {
                if (At(x, firstNotEmpty) is not { Category: MatchThreeTiles.Empty })
                    columnEmpty = false;

                firstNotEmpty++;
            }

            var spawnRow = firstNotEmpty - 2;
            var spawned = 0;

            for (var y = Height - 1; y > 0; y--)
            {
                var cell = At(x, y);

                if (cell?.Category == MatchThreeTiles.DropZone)
                {
                    // The first thing above the drop zone: ore falls out, a transforming tile converts.
                    for (var above = y - 1; above >= 0; above--)
                    {
                        var tile = At(x, above);
                        if (tile is null)
                            continue;

                        if (IsFallType(tile.Value))
                            tile.AddFallOffBottom();
                        else if (IsTransformType(tile.Value))
                            tile.AddConvert();

                        break;
                    }
                }
                else if (cell is null)
                {
                    var tile = RegenTile(x, spawnRow);

                    // The client's own rule: new ore that will land right on the drop zone falls straight out.
                    if (IsFallType(tile.Value) && At(x, y + 1)?.Category == MatchThreeTiles.DropZone)
                    {
                        for (var row = y - 1; row >= spawnRow; row--)
                        {
                            if (row == spawnRow)
                                tile.AddFallOffBottom();

                            if (At(x, row) is not null)
                                break;
                        }
                    }

                    spawned++;
                    spawnRow--;
                    hasFall = true;
                    AddColumnFall(y, x);
                }
            }

            for (var row = firstNotEmpty - 2; row > spawnRow; row--)
            {
                for (var i = 1; i <= spawned; i++)
                {
                    _tiles[^i].AddFall();
                    hasFall = true;
                }
            }
        }

        return hasFall;
    }

    private void AddColumnFall(int fromY, int x)
    {
        for (var y = fromY - 1; y > 0; y--)
        {
            var tile = At(x, y);
            if (tile is null || tile.Category is MatchThreeTiles.Empty or MatchThreeTiles.DropZone)
                continue;

            tile.AddFall();
        }
    }

    private static bool IsFallType(int value) => value is >= MatchThreeTiles.FallTypeStart and < MatchThreeTiles.FallTypeEnd;

    private static bool IsTransformType(int value) => value is >= MatchThreeTiles.TransformTypeStart and < MatchThreeTiles.TransformTypeEnd;

    /// <summary>Whether any chain or clickable power-up is left on the board.</summary>
    public bool HasMove()
    {
        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                var tile = At(x, y);
                if (tile is null)
                    continue;

                switch (tile.Category)
                {
                    case MatchThreeTiles.Powerup when tile.Value >= MatchThreeTiles.PowerupPressMe:
                        return true;

                    case MatchThreeTiles.Wildcard:
                    case MatchThreeTiles.Selectable:
                        if (MatchingNeighbourCount(x, y, tile.Value) > 1)
                            return true;
                        break;
                }
            }
        }

        return false;
    }

    private int MatchingNeighbourCount(int x, int y, int value)
    {
        var count = 0;
        int up = MatchThreeTiles.Empty, down = MatchThreeTiles.Empty, right = MatchThreeTiles.Empty;
        int upPairs = 0, downPairs = 0, rightPairs = 0, leftPairs = 0;

        if (At(x, y - 1) is { } upTile)
        {
            up = upTile.Value;
            count += Matches(value, up);
            if (upTile.Category == MatchThreeTiles.Selectable)
                upPairs += 1;
        }

        if (At(x, y + 1) is { } downTile)
        {
            down = downTile.Value;
            count += Matches(value, down);
            if (downTile.Category == MatchThreeTiles.Selectable)
            {
                upPairs += Matches(up, down);
                downPairs += 1;
            }
        }

        if (At(x + 1, y) is { } rightTile)
        {
            right = rightTile.Value;
            count += Matches(value, right);
            if (rightTile.Category == MatchThreeTiles.Selectable)
            {
                upPairs += Matches(up, right);
                downPairs += Matches(down, right);
                rightPairs += 1;
            }
        }

        if (At(x - 1, y) is { } leftTile)
        {
            var left = leftTile.Value;
            count += Matches(value, left);
            if (leftTile.Category == MatchThreeTiles.Selectable)
            {
                upPairs += Matches(up, left);
                downPairs += Matches(down, left);
                rightPairs += Matches(right, left);
                leftPairs += 1;
            }
        }

        // A wildcard links two neighbours that match each other.
        if (value == MatchThreeTiles.Wildcard)
            count += Math.Max(Math.Max(upPairs, downPairs), Math.Max(rightPairs, leftPairs));

        return count;
    }

    private static int Matches(int value, int neighbour) =>
        neighbour == MatchThreeTiles.Wildcard || value == neighbour ? 1 : 0;

    // ---- Chains ----

    private bool CheckNextTile(int x, int y)
    {
        var last = _chain[^1];
        var adjacent = (Math.Abs(x - last.X) == 1 && y == last.Y) || (Math.Abs(y - last.Y) == 1 && x == last.X);

        return adjacent && CheckCurrentTile(x, y);
    }

    private bool CheckCurrentTile(int x, int y)
    {
        var tile = At(x, y);
        if (tile is null || _chain.Contains(tile))
            return false;

        var chainValue = _chain.LastOrDefault(link => link.Category == MatchThreeTiles.Selectable)?.Value ?? 0;

        if (tile.Value == MatchThreeTiles.Wildcard
            || (chainValue != 0 ? tile.Value == chainValue : tile.Category == MatchThreeTiles.Selectable))
        {
            _chain.Add(tile);
            return true;
        }

        return false;
    }

    private void DoMatch3()
    {
        var length = _chain.Count;
        int? chainValue = null;
        var hasWildcard = false;

        foreach (var tile in _chain)
        {
            if (tile.Category != MatchThreeTiles.Wildcard)
                chainValue ??= tile.Value;
            else
                hasWildcard = true;

            // A locked rock only loses its lock.
            if (tile.IsLocked)
                tile.Unlock();
            else
            {
                tile.AddCollect();
                tile.State = MatchThreeTileState.Explode;
                _hasExploded = true;
            }
        }

        if (hasWildcard && chainValue is { } value)
            foreach (var tile in _chain.Where(tile => tile.Category == MatchThreeTiles.Wildcard))
                tile.SetChainValue(value);

        _level.CalculateChainTimeBonus(length);
        _level.CalculateChainScore(length);

        _chain.Clear();
        UpdateCurrentGridArray();
    }

    internal void OnTileCollect(MatchThreeTile tile) => _level.HandleCollected(tile.Category, tile.Value);

    // ---- Tiles made on the server and shared ----

    public void DoWildTile(MatchThreeTile tile)
    {
        var (x, y) = (tile.X, tile.Y);
        _tiles.Remove(tile);
        tile.State = MatchThreeTileState.Destroy;

        var wildcard = MatchThreeTileGenerator.Specific(MatchThreeTiles.Wildcard, MatchThreeTiles.Wildcard);
        Place(x, y, wildcard);
        wildcard.State = MatchThreeTileState.Appear;
        wildcard.SetPosition(x, y);
        wildcard.SetGrid(this);
        _tiles.Add(wildcard);
    }

    private MatchThreeTile? ClientTile(IReadOnlyList<int> positions, int index) =>
        index * 2 + 1 < positions.Count ? At(positions[index * 2], positions[index * 2 + 1]) : null;

    private void InitRandomTiles(int? category, MatchThreeTileState state)
    {
        _randomTiles = [];
        AddRandomTiles(category, state);
    }

    /// <summary>In the order tiles were made, which the client's list follows too.</summary>
    private void AddRandomTiles(int? category, MatchThreeTileState state)
    {
        foreach (var tile in _tiles)
        {
            if (tile.Category >= MatchThreeTiles.Visible && (category is null || tile.Category == category) && tile.State == state)
                _randomTiles.Add(tile);
        }
    }

    private MatchThreeTile? GetRandomTile()
    {
        if (_randomTiles.Count == 0)
            return null;

        var index = GetRandomNumberFromQueue(0, _randomTiles.Count - 1);
        var tile = _randomTiles[index];
        _randomTiles.RemoveAt(index);
        return tile;
    }

    /// <summary>The next shared random number, in <paramref name="start"/>..<paramref name="end"/>.</summary>
    private int GetRandomNumberFromQueue(int start, int end)
    {
        var number = _randomNumberQueue.Count > 0 ? _randomNumberQueue.Dequeue() : 0;
        var span = end - start + 1;

        while (_randomNumberQueue.Count < RandomQueueMinLength)
        {
            GenerateRandomNumbersForQueue(RandomQueueBatchSize);
            SendRandomNumberQueue();
        }

        return span > 0 ? start + number % span : start;
    }

    private void GenerateRandomNumbersForQueue(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var number = _random.Next(RandomMaxValue);
            _randomNumberQueue.Enqueue(number);
            _unsentRandomNumbers.Add(number);
        }
    }

    private void SendRandomNumberQueue()
    {
        var text = string.Join(',', _unsentRandomNumbers);
        _unsentRandomNumbers = [];
        _output.Send("OnRandomQueueMsg", text);
    }

    private MatchThreeTile RegenTile(int x, int y)
    {
        // Never empty in play (the queue is topped up below 50); a fresh tile keeps a misbehaving level going.
        var tile = _tileQueue.Count > 0 ? _tileQueue.Dequeue() : _generator.Generate(MatchThreeTiles.Any, MatchThreeTiles.Any);
        tile.SetPosition(x, y);
        tile.SetGrid(this);
        _tiles.Add(tile);

        _tileQueueLength--;

        // While a reset waits for the client's answer its tiles are already queued to replace these; topping up now
        // would send them a second time (a race in SOE's server).
        if (_tileQueueLength < TileQueueMinLength && !_resetOutstanding)
        {
            GenerateTilesForQueue(TileQueueBatchSize, MatchThreeTiles.Any, MatchThreeTiles.Any);
            SendTileQueue(reset: false);
        }

        return tile;
    }

    private void GenerateTilesForQueue(int count, int category, int value)
    {
        if (_tilePendingQueue.Count > 0)
            return;

        for (var i = 0; i < count; i++)
        {
            var tile = _generator.Generate(category, value);
            _tilePendingQueue.Add(tile);
            _tileUnsentValues.Add(tile.NetworkValue);
        }
    }

    /// <summary>
    /// Sends the queued tiles. A reset replaces the client's queue, but only if it still holds as many tiles as the
    /// server thinks; the client answers accept or reject, and a reject sends it again with the new count.
    /// </summary>
    private void SendTileQueue(bool reset)
    {
        if (_tileUnsentValues.Count == 0)
            return;

        var text = string.Join(',', _tileUnsentValues);
        var resetCount = -1;

        if (reset)
        {
            resetCount = _tileQueueLength;
            _resetOutstanding = true;
        }
        else
            TileQueueAddPending();

        _output.Send("OnTileQueueMsg", resetCount, text);
    }

    private void TileQueueAddPending()
    {
        foreach (var tile in _tilePendingQueue)
            _tileQueue.Enqueue(tile);

        _tileQueueLength += _tilePendingQueue.Count;
        _tilePendingQueue = [];
        _tileUnsentValues = [];
    }
}
