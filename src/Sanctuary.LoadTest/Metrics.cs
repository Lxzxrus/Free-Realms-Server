using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Sanctuary.LoadTest;

/// <summary>
/// Latency in 0.1 ms buckets up to 10 s; anything slower lands in the last bucket.
/// </summary>
public sealed class LatencyHistogram
{
    private const int BucketsPerMs = 10;
    private readonly long[] _buckets = new long[10_000 * BucketsPerMs + 1];
    private long _count;
    private long _maxTicks;

    public long Count => Interlocked.Read(ref _count);

    public void Record(long elapsedTicks)
    {
        var bucket = (int)Math.Min(elapsedTicks * BucketsPerMs * 1000 / Stopwatch.Frequency, _buckets.Length - 1);

        Interlocked.Increment(ref _buckets[bucket]);
        Interlocked.Increment(ref _count);

        long max;
        while (elapsedTicks > (max = Interlocked.Read(ref _maxTicks)))
        {
            if (Interlocked.CompareExchange(ref _maxTicks, elapsedTicks, max) == max)
                break;
        }
    }

    public void Reset()
    {
        Array.Clear(_buckets);
        Interlocked.Exchange(ref _count, 0);
        Interlocked.Exchange(ref _maxTicks, 0);
    }

    public double PercentileMs(double percentile)
    {
        var count = Count;

        if (count == 0)
            return double.NaN;

        var target = (long)Math.Ceiling(count * percentile / 100.0);
        long seen = 0;

        for (var i = 0; i < _buckets.Length; i++)
        {
            seen += Interlocked.Read(ref _buckets[i]);

            if (seen >= target)
                return Math.Min((i + 1) / (double)BucketsPerMs, MaxMs); // upper edge of the bucket
        }

        return MaxMs;
    }

    public double MaxMs => Count == 0 ? double.NaN : Interlocked.Read(ref _maxTicks) * 1000.0 / Stopwatch.Frequency;
}

/// <summary>
/// Matches a move one bot sends to the copies other bots receive. The server forwards the position bytes
/// unchanged (it only overwrites the guid), so sender and position identify a move without adding anything to
/// the packet that a watching game client would see.
/// </summary>
public sealed class MoveTracker
{
    private readonly ConcurrentDictionary<(ulong Guid, int X, int Z), long> _sent = new();

    public LatencyHistogram Latency { get; } = new();

    public void OnSent(ulong guid, float x, float z, long timestamp)
    {
        _sent[(guid, BitConverter.SingleToInt32Bits(x), BitConverter.SingleToInt32Bits(z))] = timestamp;
    }

    /// <returns>True if the move was one a bot sent during this measurement.</returns>
    public bool OnSeen(ulong guid, float x, float z, long timestamp)
    {
        if (!_sent.TryGetValue((guid, BitConverter.SingleToInt32Bits(x), BitConverter.SingleToInt32Bits(z)), out var sentAt))
            return false;

        Latency.Record(timestamp - sentAt);

        return true;
    }

    public void Reset()
    {
        _sent.Clear();
        Latency.Reset();
    }
}

/// <summary>
/// What every bot adds to. Errors are counted, not thrown, so one bad bot doesn't end a run.
/// </summary>
public sealed class RunCounters
{
    public long MovesSent;
    public long MovesSeen;
    public long JumpsSent;
    public long ChatsSent;
    public long ChatsSeen;

    public long LoginFailures;
    public long Disconnects;
    public long CorruptPackets;
    public long BotExceptions;

    public long Errors => Interlocked.Read(ref LoginFailures) + Interlocked.Read(ref Disconnects)
        + Interlocked.Read(ref CorruptPackets) + Interlocked.Read(ref BotExceptions);

    public RunCounters Snapshot()
    {
        return new RunCounters
        {
            MovesSent = Interlocked.Read(ref MovesSent),
            MovesSeen = Interlocked.Read(ref MovesSeen),
            JumpsSent = Interlocked.Read(ref JumpsSent),
            ChatsSent = Interlocked.Read(ref ChatsSent),
            ChatsSeen = Interlocked.Read(ref ChatsSeen),
            LoginFailures = Interlocked.Read(ref LoginFailures),
            Disconnects = Interlocked.Read(ref Disconnects),
            CorruptPackets = Interlocked.Read(ref CorruptPackets),
            BotExceptions = Interlocked.Read(ref BotExceptions),
        };
    }
}
