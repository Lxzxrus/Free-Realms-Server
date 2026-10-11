using System;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// A character can be renamed once every <see cref="Length"/>, so a rename can't be used to slip away from a report
/// or to pass as someone else for a while. Mods and admins can lift it (<c>/mod resetrename</c>), for example after a
/// typo, and so can tools/ops/reset-rename-cooldown.sh on the server.
/// </summary>
public static class RenameCooldown
{
    public static readonly TimeSpan Length = TimeSpan.FromDays(3);

    /// <summary>How long until the character may be renamed again; zero if it may now.</summary>
    public static TimeSpan Remaining(DateTimeOffset? lastRenamed, DateTimeOffset now)
    {
        if (lastRenamed is not DateTimeOffset last)
            return TimeSpan.Zero;

        var remaining = last + Length - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public static string Message(TimeSpan remaining)
    {
        return $"Characters can be renamed once every {(int)Length.TotalDays} days. You can rename again in {Describe(remaining)}.";
    }

    /// <summary>"2 days and 5 hours", "3 hours and 1 minute", "12 minutes". Rounds up, so it never says too early.</summary>
    public static string Describe(TimeSpan remaining)
    {
        var minutes = (long)Math.Ceiling(remaining.TotalMinutes);
        if (minutes < 1)
            minutes = 1;

        var days = minutes / (24 * 60);
        var hours = minutes / 60 % 24;
        minutes %= 60;

        if (days > 0)
            return hours > 0 ? $"{Count(days, "day")} and {Count(hours, "hour")}" : Count(days, "day");

        if (hours > 0)
            return minutes > 0 ? $"{Count(hours, "hour")} and {Count(minutes, "minute")}" : Count(hours, "hour");

        return Count(minutes, "minute");
    }

    private static string Count(long value, string unit) => value == 1 ? $"1 {unit}" : $"{value} {unit}s";
}
