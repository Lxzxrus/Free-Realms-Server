using System;
using System.Collections.Generic;

namespace Sanctuary.Game.Housing;

/// <summary>
/// The "Competition" section of the Gateway's config: the weekly ranked-plot competition's week, who may vote and
/// how the week's score is weighted. Read once at startup, so a change needs a restart. The defaults are the design's
/// placeholders; see <c>docs/design/evergrove-housing.md</c>, section 3.
/// </summary>
public sealed class CompetitionOptions
{
    public const string Section = "Competition";

    /// <summary>
    /// The time zone the competition week follows: an IANA id such as <c>Europe/London</c>, or <c>UTC</c>. The Gateway
    /// refuses to start if it doesn't know the id. Changing it moves the current week's start, so votes already cast
    /// this week stop counting if the new week starts later than the old one.
    /// </summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>The day a competition week starts, in <see cref="TimeZone"/>.</summary>
    public DayOfWeek WeekStartDay { get; set; } = DayOfWeek.Monday;

    /// <summary>The time of day a competition week starts, in <see cref="TimeZone"/>, from 00:00:00 to 23:59:59.</summary>
    public TimeSpan WeekStartTime { get; set; } = TimeSpan.Zero;

    /// <summary>How many days old an account must be before it may vote. The main defence against alt accounts.</summary>
    public int MinAccountAgeDays { get; set; } = 3;

    /// <summary>How many minutes an account's characters must have played, together, before it may vote.</summary>
    public int MinPlaytimeMinutes { get; set; } = 120;

    /// <summary>
    /// <c>m</c> in the week's score, <c>(v·R + m·C) / (v + m)</c>: how many votes' worth of the average of all ranked
    /// plots each plot starts with. 0 makes the score a plain average of the plot's own votes.
    /// </summary>
    public int MinimumVotes { get; set; } = 10;

    /// <summary>
    /// The time zone named by <see cref="TimeZone"/>, or <c>null</c> if this machine doesn't know it.
    /// </summary>
    public TimeZoneInfo? FindTimeZone()
    {
        if (string.IsNullOrWhiteSpace(TimeZone))
            return null;

        if (string.Equals(TimeZone.Trim(), "UTC", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZone.Trim());
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>The competition week that contains <paramref name="instant"/>.</summary>
    public CompetitionWeek WeekContaining(DateTimeOffset instant)
    {
        var zone = FindTimeZone()
            ?? throw new InvalidOperationException($"Competition:TimeZone '{TimeZone}' is not a time zone this machine knows.");

        return CompetitionWeek.Containing(instant, zone, WeekStartDay, WeekStartTime);
    }

    /// <summary>What's wrong with these settings, if anything. The Gateway refuses to start until it's fixed.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();

        if (FindTimeZone() is null)
            problems.Add($"Competition:TimeZone '{TimeZone}' is not a time zone this machine knows. Use an IANA id such as Europe/London, or UTC.");

        if (!Enum.IsDefined(WeekStartDay))
            problems.Add($"Competition:WeekStartDay '{WeekStartDay}' is not a day of the week.");

        if (WeekStartTime < TimeSpan.Zero || WeekStartTime >= TimeSpan.FromDays(1))
            problems.Add($"Competition:WeekStartTime '{WeekStartTime}' must be a time of day, from 00:00:00 to 23:59:59.");

        if (MinAccountAgeDays < 0)
            problems.Add("Competition:MinAccountAgeDays can't be negative.");

        if (MinPlaytimeMinutes < 0)
            problems.Add("Competition:MinPlaytimeMinutes can't be negative.");

        if (MinimumVotes < 0)
            problems.Add("Competition:MinimumVotes can't be negative.");

        return problems;
    }
}
