using System;

namespace Sanctuary.Game.Reports;

/// <summary>
/// Decides which of many log lines about the same thing are written, for lines a client can make as fast as it likes: the
/// first <c>firstFree</c> are allowed, then one per <c>interval</c>, each told how many were held back since the last one.
/// It keeps no text, so a flood costs a counter, not memory or disk.
/// </summary>
public sealed class LogRateLimiter
{
    private readonly int _firstFree;
    private readonly TimeSpan _interval;
    private readonly Func<long> _nowMilliseconds;
    private readonly object _lock = new();

    private int _allowed;
    private int _suppressed;
    private long _lastAt;

    public LogRateLimiter(int firstFree, TimeSpan interval, Func<long>? nowMilliseconds = null)
    {
        _firstFree = firstFree;
        _interval = interval;
        _nowMilliseconds = nowMilliseconds ?? (() => Environment.TickCount64);
    }

    /// <summary>
    /// Whether this line is written. If so, <paramref name="suppressed"/> is how many were held back since the last one
    /// written. Safe to call from several connections at once.
    /// </summary>
    public bool TryAllow(out int suppressed)
    {
        lock (_lock)
        {
            var now = _nowMilliseconds();

            if (_allowed < _firstFree || now - _lastAt >= _interval.TotalMilliseconds)
            {
                _allowed++;
                _lastAt = now;
                suppressed = _suppressed;
                _suppressed = 0;
                return true;
            }

            _suppressed++;
            suppressed = 0;
            return false;
        }
    }
}
