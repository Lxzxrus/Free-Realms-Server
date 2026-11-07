using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.Core.Helpers;

/// <summary>
/// Runs work on the thread pool, one item at a time in the order it was queued, and holds at most <see cref="Limit"/>
/// items, the one running included. Without a limit, a caller that queues work faster than it runs would grow the queue,
/// and the memory it holds, without end.
/// </summary>
public sealed class SerialWorkQueue
{
    private readonly object _lock = new();
    private Task _tail = Task.CompletedTask;
    private int _pending;

    public SerialWorkQueue(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        Limit = limit;
    }

    public int Limit { get; }

    /// <summary>Items queued and not finished yet, the one running included.</summary>
    public int Pending => Volatile.Read(ref _pending);

    /// <summary>Whether <see cref="TryQueue"/> would refuse an item now.</summary>
    public bool IsFull => Pending >= Limit;

    /// <summary>
    /// Queues <paramref name="work"/> to run after everything queued before it. An exception from it goes to
    /// <paramref name="onError"/>, and the items after it still run. Refused (false) while <see cref="Limit"/> items wait.
    /// </summary>
    public bool TryQueue(Action work, Action<Exception> onError)
    {
        lock (_lock)
        {
            if (IsFull)
                return false;

            Interlocked.Increment(ref _pending);

            _tail = _tail.ContinueWith(_ =>
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    onError(ex);
                }
                finally
                {
                    Interlocked.Decrement(ref _pending);
                }
            }, TaskScheduler.Default);

            return true;
        }
    }

    /// <summary>Completes when everything queued so far has run.</summary>
    public Task WhenIdle()
    {
        lock (_lock)
        {
            return _tail;
        }
    }
}
