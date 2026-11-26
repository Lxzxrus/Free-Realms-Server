using System;
using System.Collections.Generic;

namespace Sanctuary.Game.Reports;

/// <summary>Which allowance a report counts against.</summary>
public enum ReportBudget
{
    /// <summary>Player and house reports share one.</summary>
    Report,

    /// <summary>Bug reports.</summary>
    Bug
}

/// <summary>
/// How many reports an account may make, kept in memory and counted per account, not per character or connection, so
/// switching characters or reconnecting does not reset it. A restart does reset it. Time comes from a
/// <see cref="TimeProvider"/> so a test can drive it.
/// </summary>
public sealed class ReportRateLimiter
{
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(10);

    private sealed class Account
    {
        public readonly Queue<DateTimeOffset> Attempts = new();
        public readonly Queue<DateTimeOffset> Reports = new();
        public readonly Queue<DateTimeOffset> Bugs = new();
    }

    private readonly object _lock = new();
    private readonly Dictionary<ulong, Account> _accounts = [];

    // The same counts across every account. Each is bounded by its limit, so none of them grows with the number of accounts.
    private readonly Account _global = new();
    private readonly TimeProvider _timeProvider;
    private DateTimeOffset _lastSweep;

    public ReportRateLimiter(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastSweep = _timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Counts an attempt, accepted or not. False once the account has tried too often lately: nothing else should be
    /// done for it then, not even a database query.
    /// </summary>
    public bool TryBeginAttempt(ulong userId)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            Sweep(now);

            var account = GetAccount(userId);

            Prune(account.Attempts, now, ReportLimits.AttemptWindow);
            Prune(_global.Attempts, now, ReportLimits.AttemptWindow);

            if (account.Attempts.Count >= ReportLimits.AttemptsPerWindow || _global.Attempts.Count >= ReportLimits.GlobalAttemptsPerWindow)
                return false;

            account.Attempts.Enqueue(now);
            _global.Attempts.Enqueue(now);
            return true;
        }
    }

    /// <summary>Whether the account may make another report of this budget now. Counts nothing.</summary>
    public bool CanAccept(ulong userId, ReportBudget budget)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            var global = QueueFor(_global, budget);

            Prune(global, now, Day);

            var (globalPerHour, globalPerDay) = GlobalLimitsFor(budget);

            if (global.Count >= globalPerDay || CountWithin(global, now, Hour) >= globalPerHour)
                return false;

            if (!_accounts.TryGetValue(userId, out var account))
                return true;

            var queue = QueueFor(account, budget);

            Prune(queue, now, Day);

            var (perHour, perDay) = LimitsFor(budget);

            return queue.Count < perDay && CountWithin(queue, now, Hour) < perHour;
        }
    }

    /// <summary>Counts a report that was kept.</summary>
    public void RecordAccepted(ulong userId, ReportBudget budget)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            QueueFor(GetAccount(userId), budget).Enqueue(now);
            QueueFor(_global, budget).Enqueue(now);
        }
    }

    /// <summary>How many accounts are remembered (for tests: it must not grow without bound).</summary>
    internal int AccountCount
    {
        get
        {
            lock (_lock)
                return _accounts.Count;
        }
    }

    private static (int PerHour, int PerDay) LimitsFor(ReportBudget budget) => budget == ReportBudget.Bug
        ? (ReportLimits.BugsPerHour, ReportLimits.BugsPerDay)
        : (ReportLimits.ReportsPerHour, ReportLimits.ReportsPerDay);

    private static (int PerHour, int PerDay) GlobalLimitsFor(ReportBudget budget) => budget == ReportBudget.Bug
        ? (ReportLimits.GlobalBugsPerHour, ReportLimits.GlobalBugsPerDay)
        : (ReportLimits.GlobalReportsPerHour, ReportLimits.GlobalReportsPerDay);

    private static Queue<DateTimeOffset> QueueFor(Account account, ReportBudget budget) =>
        budget == ReportBudget.Bug ? account.Bugs : account.Reports;

    private Account GetAccount(ulong userId)
    {
        if (!_accounts.TryGetValue(userId, out var account))
            _accounts[userId] = account = new Account();

        return account;
    }

    private static void Prune(Queue<DateTimeOffset> queue, DateTimeOffset now, TimeSpan window)
    {
        while (queue.Count > 0 && now - queue.Peek() >= window)
            queue.Dequeue();
    }

    private static int CountWithin(Queue<DateTimeOffset> queue, DateTimeOffset now, TimeSpan window)
    {
        var count = 0;

        foreach (var time in queue)
        {
            if (now - time < window)
                count++;
        }

        return count;
    }

    /// <summary>Forgets accounts with nothing left in any window, now and then, so memory follows who reported lately.</summary>
    private void Sweep(DateTimeOffset now)
    {
        if (now - _lastSweep < SweepEvery)
            return;

        _lastSweep = now;

        List<ulong>? idle = null;

        foreach (var (userId, account) in _accounts)
        {
            Prune(account.Attempts, now, ReportLimits.AttemptWindow);
            Prune(account.Reports, now, Day);
            Prune(account.Bugs, now, Day);

            if (account.Attempts.Count == 0 && account.Reports.Count == 0 && account.Bugs.Count == 0)
                (idle ??= []).Add(userId);
        }

        if (idle is null)
            return;

        foreach (var userId in idle)
            _accounts.Remove(userId);
    }
}
