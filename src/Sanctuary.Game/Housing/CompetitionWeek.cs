using System;
using System.Linq;

namespace Sanctuary.Game.Housing;

/// <summary>
/// One week of the ranked-plot competition, from <see cref="Start"/> (inclusive) to <see cref="End"/> (exclusive),
/// both in UTC. A week starts on a set day and time in the competition's time zone (design section 3: Monday 00:00), so
/// across a daylight-saving change it is an hour shorter or longer than seven days.
/// </summary>
public readonly record struct CompetitionWeek(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>Whether <paramref name="instant"/> falls in this week.</summary>
    public bool Contains(DateTimeOffset instant) => instant >= Start && instant < End;

    /// <summary>
    /// The week that contains <paramref name="instant"/>, for weeks that start every <paramref name="startDay"/> at
    /// <paramref name="startTime"/> on the clocks of <paramref name="zone"/>.
    /// </summary>
    public static CompetitionWeek Containing(
        DateTimeOffset instant,
        TimeZoneInfo zone,
        DayOfWeek startDay,
        TimeSpan startTime)
    {
        // The wall-clock time in the zone, with no offset attached (DateTimeKind.Unspecified).
        var local = TimeZoneInfo.ConvertTime(instant, zone).DateTime;
        var daysSinceStartDay = ((int)local.DayOfWeek - (int)startDay + 7) % 7;
        var startLocal = local.Date.AddDays(-daysSinceStartDay) + startTime;

        // On the start day itself, before the start time, the week began seven days ago.
        if (ToInstant(startLocal, zone) > instant)
            startLocal = startLocal.AddDays(-7);

        return new CompetitionWeek(ToInstant(startLocal, zone), ToInstant(startLocal.AddDays(7), zone));
    }

    /// <summary>
    /// The UTC instant a zone's clocks show <paramref name="local"/>. A time the clocks skip when daylight saving starts
    /// is read with the offset from before the change, so the week starts the moment the clocks jump. A time the clocks
    /// show twice when it ends is its first occurrence.
    /// </summary>
    private static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        TimeSpan offset;

        if (zone.IsInvalidTime(local))
            offset = zone.GetUtcOffset(local.AddDays(-1));
        else if (zone.IsAmbiguousTime(local))
            offset = zone.GetAmbiguousTimeOffsets(local).Max();
        else
            offset = zone.GetUtcOffset(local);

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
