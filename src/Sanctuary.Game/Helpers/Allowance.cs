using System;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// How often a player may do something a client can repeat at will (a token bucket): up to <c>burst</c> times at once,
/// then once more every <c>refillMs</c>. A player's packets are handled one at a time, so it isn't locked.
/// </summary>
public sealed class Allowance
{
    private readonly int _burst;
    private readonly long _refillMs;
    private int _left;
    private long _refilledAt;

    /// <param name="now">The time it starts full, in <see cref="Environment.TickCount64"/> milliseconds.</param>
    public Allowance(int burst, int refillMs, long now)
    {
        _burst = burst;
        _refillMs = refillMs;
        _left = burst;
        _refilledAt = now;
    }

    /// <summary>Uses one up if any is left at <paramref name="now"/> (<see cref="Environment.TickCount64"/>).</summary>
    public bool TryUse(long now)
    {
        var refills = (now - _refilledAt) / _refillMs;

        if (refills > 0)
        {
            _left = (int)Math.Min(_burst, _left + refills);
            _refilledAt += refills * _refillMs;
        }

        if (_left == 0)
            return false;

        _left--;
        return true;
    }
}
