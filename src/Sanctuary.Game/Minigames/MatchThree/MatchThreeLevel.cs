using System;
using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.Game.Minigames.MatchThree;

/// <summary>Where a match-three game's messages go: tab-separated, as the client's Flash reads them.</summary>
public interface IMatchThreeOutput
{
    void Send(string name, params object[] args);
}

/// <summary>One tile type in a level, with what's been made and collected of it.</summary>
public sealed class MatchThreeSpawn(MatchThreeSpawnDefinition definition, int value)
{
    public MatchThreeSpawnDefinition Definition { get; } = definition;

    /// <summary>The tile value: the kind's hundred plus the type.</summary>
    public int Value { get; } = value;

    /// <summary>The quota bin on the client's screen, if it has one.</summary>
    public int BinIndex { get; set; } = -1;

    public int Created { get; set; }
    public int Collected { get; set; }

    public bool WithinCreateLimit(int count) => Definition.MaxCreate < 1 || count < Definition.MaxCreate;

    public int LimitToCollectLimit(int count) => Definition.MaxCollect < 1 ? count : Math.Min(count, Definition.MaxCollect);
}

/// <summary>
/// One level of a match-three game (a port of SOE's <c>GameLevel</c> and <c>SoeLevel</c>, with its <c>Score</c> and
/// <c>Progression</c>): the clock, the quotas, the score and the board.
/// </summary>
public sealed class MatchThreeLevel
{
    public const int MinimumChainLength = 3;
    public const int ChainScore = 400;
    public const int ChainBonusTileScore = 200;
    public const int BonusScore = 2000;
    public const int PowerupScore = 250;
    public const double MinimumChainTimeBonus = 1;

    /// <summary>Every this many tiles collected the queue is rerolled ("next wave"), as in SOE's levels.</summary>
    public const int TilesPerWave = 100;

    private readonly IMatchThreeOutput _output;
    private readonly Func<long> _clock;
    private readonly Random _random;

    private long _startTime;
    private long _pausedAt;
    private double _durationMs;
    private int _tileCount;

    private int _multiplier = 1;
    private double _multiplierTimer;
    private double _multiplierDuration = 10_000;

    public MatchThreeLevel(int number, MatchThreeLevelDefinition definition, IMatchThreeOutput output, Func<long> clock, Random random)
    {
        Number = number;
        Definition = definition;
        _output = output;
        _clock = clock;
        _random = random;

        Selectables = definition.Selectables.ToDictionary(spawn => spawn.Type, spawn => new MatchThreeSpawn(spawn, MatchThreeTiles.Selectable + spawn.Type));
        Collectables = definition.Collectables.ToDictionary(spawn => spawn.Type, spawn => new MatchThreeSpawn(spawn, MatchThreeTiles.Collectable + spawn.Type));
        Bonuses = definition.Bonuses.ToDictionary(spawn => spawn.Type, spawn => new MatchThreeSpawn(spawn, MatchThreeTiles.Bonus + spawn.Type));
        Powerups = definition.Powerups.ToDictionary(spawn => MatchThreeTiles.Powerup + spawn.Type, spawn => new MatchThreeSpawn(spawn, MatchThreeTiles.Powerup + spawn.Type));
    }

    public int Number { get; }
    public MatchThreeLevelDefinition Definition { get; }

    public IReadOnlyDictionary<int, MatchThreeSpawn> Selectables { get; }
    public IReadOnlyDictionary<int, MatchThreeSpawn> Collectables { get; }
    public IReadOnlyDictionary<int, MatchThreeSpawn> Bonuses { get; }

    /// <summary>Keyed by tile value (900 and up).</summary>
    public IReadOnlyDictionary<int, MatchThreeSpawn> Powerups { get; }

    public MatchThreeSpawnChances SpawnChances => Definition.SpawnChances;

    public MatchThreeGrid? Grid { get; private set; }

    public bool IsStarted { get; private set; }
    public bool IsInProgress { get; private set; }
    public bool IsDone { get; private set; }
    public bool IsPaused => _pausedAt != 0;

    public long Score { get; private set; }

    public int Wave { get; private set; } = 1;

    /// <summary>Times the client's board was found out of step and rebuilt this level.</summary>
    public int Desyncs { get; set; }

    /// <summary>Milliseconds on the clock, including time won from chains and power-ups.</summary>
    public double DurationMs => _durationMs;

    public void Start()
    {
        foreach (var spawn in Selectables.Values.Concat(Collectables.Values).Concat(Bonuses.Values))
            (spawn.Created, spawn.Collected) = (0, 0);

        var generator = new MatchThreeTileGenerator(this, _random, _output);
        Grid = new MatchThreeGrid(this, generator, [.. Definition.Grid], _output, _random);

        _output.Send("OnLevelInitMsg", Number, Definition.DurationSeconds, (int)Definition.TypeFlags);
        QuotasInit();

        foreach (var spawn in Powerups.Values)
            _output.Send("OnPowerupInitMsg", spawn.Value, spawn.Definition.Duration, spawn.Definition.Power);

        IsStarted = true;
        IsInProgress = true;
        IsDone = false;
        Score = 0;
        _pausedAt = 0;
        _startTime = Now();
        _durationMs = Definition.DurationSeconds * 1000.0;

        // SOE's framework reports the level score as it starts: level score, total score.
        _output.Send("OnScoreMsg", 0, 0);
    }

    // The clock never reads 0, which stands for "not paused".
    private long Now() => Math.Max(1, _clock());

    public bool Pause()
    {
        if (!IsInProgress || IsPaused)
            return false;

        _pausedAt = Now();
        return true;
    }

    public bool Resume()
    {
        if (!IsPaused)
            return false;

        _startTime += Now() - _pausedAt;
        _pausedAt = 0;
        return true;
    }

    public bool Stop()
    {
        if (!IsInProgress)
            return false;

        Resume();
        IsDone = true;
        IsInProgress = false;
        return true;
    }

    public double ElapsedMs => !IsStarted ? 0 : (IsPaused ? _pausedAt : Now()) - _startTime;

    /// <summary>Time is up, or (on a stop-at-quota level) every quota is met.</summary>
    public bool CheckDone()
    {
        if (!IsInProgress || IsPaused)
            return false;

        if (ElapsedMs >= _durationMs)
            return true;

        if (Definition.StopAtQuota && (QuotasMet(Collectables.Values, successOnly: false) || QuotasMet(Selectables.Values, successOnly: false)))
            return true;

        return false;
    }

    /// <summary>Every quota met (a level without quotas always succeeds).</summary>
    public bool Succeeded => QuotasMet(Collectables.Values, successOnly: true) && QuotasMet(Selectables.Values, successOnly: true);

    private static bool QuotasMet(IEnumerable<MatchThreeSpawn> spawns, bool successOnly)
    {
        var withQuota = spawns.Where(spawn => spawn.Definition.Quota > 0).ToList();
        return withQuota.All(spawn => spawn.Collected >= spawn.Definition.Quota) && (withQuota.Count > 0 || successOnly);
    }

    public void Tick(double deltaMs)
    {
        if (_multiplier == 1)
            return;

        _multiplierTimer += deltaMs;
        if (_multiplierTimer > _multiplierDuration)
            ResetMultiplier();
    }

    /// <summary>Counts a new tile; false once a type reaches its create limit, so the generator stops rolling it.</summary>
    public bool CreatedTile(int category, int value)
    {
        var spawn = category switch
        {
            MatchThreeTiles.Collectable => Collectables.GetValueOrDefault(value - MatchThreeTiles.Collectable),
            MatchThreeTiles.Selectable => Selectables.GetValueOrDefault(value - MatchThreeTiles.Selectable),
            MatchThreeTiles.Bonus => Bonuses.GetValueOrDefault(value - MatchThreeTiles.Bonus),
            _ => null
        };

        if (spawn is null)
            return true;

        spawn.Created++;
        return spawn.WithinCreateLimit(spawn.Created);
    }

    public void CalculateChainTimeBonus(int chainLength)
    {
        var bonus = Definition.ChainSecondsPerTile * chainLength;
        if (bonus <= 0)
            return;

        bonus = Math.Max(bonus, MinimumChainTimeBonus);
        _output.Send("OnLevelEventMsg", MatchThreeEvents.TimerIncrement, bonus, MatchThreeEvents.TimerChain);
        ChangeTimerDuration(bonus);
    }

    public void CalculateChainScore(int chainLength)
    {
        var points = ChainScore + Math.Max(0, chainLength - MinimumChainLength) * ChainBonusTileScore;
        UpdateScore(points, MatchThreeEvents.ScoreChain);
    }

    private void ChangeTimerDuration(double seconds)
    {
        if (!IsStarted || IsDone)
            return;

        _durationMs += seconds * 1000;
    }

    public void HandleCollected(int category, int value)
    {
        switch (category)
        {
            case MatchThreeTiles.Collectable:
                Collect(Collectables.GetValueOrDefault(value - MatchThreeTiles.Collectable), sendQuota: true, score: 0);
                break;

            case MatchThreeTiles.Bonus:
                Collect(Bonuses.GetValueOrDefault(value - MatchThreeTiles.Bonus), sendQuota: false, score: BonusScore);
                break;

            case MatchThreeTiles.Powerup:
                HandlePowerup(value);
                break;

            // A wildcard counts as the rock it stood in for.
            case MatchThreeTiles.Selectable:
            case MatchThreeTiles.Wildcard when value >= MatchThreeTiles.Selectable:
                if (Selectables.GetValueOrDefault(value - MatchThreeTiles.Selectable) is { } rock)
                {
                    rock.Collected++;
                    SendQuotaUpdate(rock);
                }
                break;
        }

        if (++_tileCount >= TilesPerWave)
        {
            _tileCount = 0;
            Wave++;
            Grid?.NextWave();
            _output.Send("OnLevelEventMsg", MatchThreeEvents.LevelNextWave, Wave);
        }
    }

    private void Collect(MatchThreeSpawn? spawn, bool sendQuota, int score)
    {
        if (spawn is null || spawn.LimitToCollectLimit(spawn.Collected + 1) <= spawn.Collected)
            return;

        spawn.Collected++;

        if (sendQuota)
            SendQuotaUpdate(spawn);

        if (score > 0)
            UpdateScore(score, MatchThreeEvents.ScoreBonus);
    }

    private void SendQuotaUpdate(MatchThreeSpawn spawn)
    {
        if (spawn.Definition.Quota > 0)
            _output.Send("OnQuotaUpdateMsg", spawn.BinIndex, spawn.Collected, spawn.Definition.Quota, RequiredPerReward(spawn));
    }

    private void HandlePowerup(int value)
    {
        if (!Powerups.TryGetValue(value, out var spawn))
            return;

        switch (value)
        {
            case MatchThreeTiles.PowerupScore2X:
                UpdateMultiplier(2, spawn.Definition.Duration);
                UpdateScore(PowerupScore, MatchThreeEvents.ScorePowerup);
                break;

            case MatchThreeTiles.PowerupScore5X:
                UpdateMultiplier(5, spawn.Definition.Duration);
                UpdateScore(PowerupScore, MatchThreeEvents.ScorePowerup);
                break;

            case MatchThreeTiles.PowerupFreeze:
                ChangeTimerDuration(spawn.Definition.Duration);
                _output.Send("OnLevelEventMsg", MatchThreeEvents.PowerupFreeze, spawn.Definition.Duration);
                UpdateScore(PowerupScore, MatchThreeEvents.ScorePowerup);
                break;
        }
    }

    private void UpdateScore(int points, int reason)
    {
        var scored = points * _multiplier;
        Score += scored;
        _output.Send("OnLevelEventMsg", MatchThreeEvents.ScoreIncrement, scored, reason);
    }

    private void UpdateMultiplier(int multiplier, int durationSeconds)
    {
        if (multiplier > _multiplier)
        {
            _multiplier = multiplier;
            _multiplierDuration = durationSeconds * 1000.0;
        }

        _multiplierTimer = 0;
        _output.Send("OnLevelEventMsg", MatchThreeEvents.ScoreMultiplier, _multiplier, _multiplierDuration);
    }

    private void ResetMultiplier()
    {
        _multiplier = 1;
        _multiplierTimer = 0;
        _multiplierDuration = 0;
        _output.Send("OnLevelEventMsg", MatchThreeEvents.ScoreMultiplierExpired);
    }

    /// <summary>A bin for each rock and ore with a quota (or a reward count), and for each gem, then the meter.</summary>
    private void QuotasInit()
    {
        var bin = 0;
        var total = 0;

        foreach (var spawn in Selectables.Values.Concat(Collectables.Values).OrderBy(spawn => spawn.Value))
        {
            if (spawn.Definition.Quota == 0 && spawn.Definition.RequiredPerReward <= 0)
                continue;

            total += spawn.Definition.Quota;
            spawn.BinIndex = bin++;
            _output.Send("OnQuotaInitMsg", spawn.BinIndex, spawn.Value, spawn.Definition.Quota, RequiredPerReward(spawn), spawn.Definition.IconId);
        }

        foreach (var spawn in Bonuses.Values.OrderBy(spawn => spawn.Value))
        {
            total += spawn.Definition.Quota;
            spawn.BinIndex = bin++;
            _output.Send("OnQuotaInitMsg", spawn.BinIndex, spawn.Value, spawn.Definition.Quota, RequiredPerReward(spawn), spawn.Definition.IconId);
        }

        _output.Send("OnLevelEventMsg", MatchThreeEvents.MeterInit, Definition.MoldId, total);
    }

    private static int RequiredPerReward(MatchThreeSpawn spawn) =>
        spawn.Definition.Quota > 0 ? Math.Max(1, spawn.Definition.RequiredPerReward) : spawn.Definition.RequiredPerReward;
}
