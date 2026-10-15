using System;
using System.IO;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class CompetitionWeekTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Wednesday = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A zone whose clocks change at midnight on a Monday, the week's start: at 00:00 on the second Monday of March they
    /// jump to 01:00, and at 01:00 on the first Monday of November they go back to 00:00. Standard time is UTC-3.
    /// </summary>
    private static readonly TimeZoneInfo MondayMidnightDst = TimeZoneInfo.CreateCustomTimeZone(
        "Test/MondayMidnightDst",
        TimeSpan.FromHours(-3),
        "Test",
        "Test standard",
        "Test daylight",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1),
                new DateTime(2099, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 2, DayOfWeek.Monday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 11, 1, DayOfWeek.Monday))
        ]);

    private static CompetitionWeek DefaultWeek(DateTimeOffset instant) => new CompetitionOptions().WeekContaining(instant);

    private static CompetitionWeek MondayMidnightDstWeek(DateTimeOffset instant)
        => CompetitionWeek.Containing(instant, MondayMidnightDst, DayOfWeek.Monday, TimeSpan.Zero);

    [TestMethod]
    public void DefaultWeek_RunsFromMondayMidnightUtc_ToTheNextMonday()
    {
        var week = DefaultWeek(Wednesday);

        Assert.AreEqual(Monday, week.Start);
        Assert.AreEqual(Monday.AddDays(7), week.End);
        Assert.AreEqual(TimeSpan.Zero, week.Start.Offset, "Votes are keyed by the start, so it is always stored in UTC.");
        Assert.IsTrue(week.Contains(Wednesday));
    }

    [TestMethod]
    public void WeekBoundary_SundayNightIsTheOldWeek_MondayMidnightTheNew()
    {
        var lastTick = DefaultWeek(Monday.AddTicks(-1));
        var sunday = DefaultWeek(Monday.AddMinutes(-1));
        var midnight = DefaultWeek(Monday);

        Assert.AreEqual(Monday.AddDays(-7), lastTick.Start);
        Assert.AreEqual(Monday, lastTick.End);
        Assert.AreEqual(lastTick, sunday);

        Assert.AreEqual(Monday, midnight.Start);
        Assert.IsFalse(lastTick.Contains(Monday), "The end is exclusive.");
        Assert.IsTrue(midnight.Contains(Monday));
    }

    [TestMethod]
    public void AnInstantWithAnOffset_IsPlacedByTheCompetitionClock()
    {
        // 20:00 on Sunday at UTC-5 is 01:00 on Monday in UTC.
        var sundayEvening = new DateTimeOffset(2026, 10, 11, 20, 0, 0, TimeSpan.FromHours(-5));

        Assert.AreEqual(Monday, DefaultWeek(sundayEvening).Start);
    }

    [TestMethod]
    public void StartDayAndTime_AreSettings()
    {
        var options = new CompetitionOptions { WeekStartDay = DayOfWeek.Friday, WeekStartTime = TimeSpan.FromHours(18) };
        var friday = new DateTimeOffset(2026, 10, 9, 18, 0, 0, TimeSpan.Zero);

        Assert.AreEqual(friday, options.WeekContaining(Wednesday).Start);
        Assert.AreEqual(friday.AddDays(7), options.WeekContaining(friday.AddDays(7).AddSeconds(-1)).End);
        Assert.AreEqual(friday, options.WeekContaining(friday).Start, "18:00 on the start day starts the week.");
        Assert.AreEqual(friday.AddDays(-7), options.WeekContaining(friday.AddSeconds(-1)).Start, "17:59:59 on the start day is still last week.");
    }

    [TestMethod]
    public void TimeZone_MovesTheBoundary()
    {
        // New York is on daylight time (UTC-4) in October, so its Monday midnight is 04:00 UTC.
        var options = new CompetitionOptions { TimeZone = "America/New_York" };
        var newYorkMonday = new DateTimeOffset(2026, 10, 12, 4, 0, 0, TimeSpan.Zero);

        Assert.IsNotNull(options.FindTimeZone(), "This machine has no time zone data (tzdata).");
        Assert.AreEqual(newYorkMonday, options.WeekContaining(Wednesday).Start);
        Assert.AreEqual(newYorkMonday.AddDays(-7), options.WeekContaining(newYorkMonday.AddSeconds(-1)).Start, "23:59:59 on Sunday in New York.");
        Assert.AreEqual(TimeSpan.Zero, options.WeekContaining(Wednesday).Start.Offset);
    }

    [TestMethod]
    public void ClocksJumpingForwardAtTheStart_StartTheWeekWhenTheyJump()
    {
        // Monday 9 March 2026, 00:00 at UTC-3 (03:00 UTC): the clocks jump straight to 01:00, now UTC-2.
        var jump = new DateTimeOffset(2026, 3, 9, 3, 0, 0, TimeSpan.Zero);
        Assert.IsTrue(MondayMidnightDst.IsInvalidTime(new DateTime(2026, 3, 9, 0, 30, 0)), "The test zone skips 00:00 to 01:00.");

        var before = MondayMidnightDstWeek(jump.AddTicks(-1));
        var week = MondayMidnightDstWeek(new DateTimeOffset(2026, 3, 12, 12, 0, 0, TimeSpan.Zero));

        Assert.AreEqual(jump, before.End);
        Assert.AreEqual(TimeSpan.FromHours(168), before.End - before.Start);
        Assert.AreEqual(jump, week.Start);
        Assert.AreEqual(new DateTimeOffset(2026, 3, 16, 2, 0, 0, TimeSpan.Zero), week.End, "Monday 16 March 00:00 at UTC-2.");
        Assert.AreEqual(TimeSpan.FromHours(167), week.End - week.Start, "The week the clocks go forward is an hour short.");
    }

    [TestMethod]
    public void ClocksGoingBackAtTheStart_StartTheWeekAtTheFirstMidnight()
    {
        // Monday 2 November 2026: the clocks reach 01:00 at UTC-2 and go back to 00:00, now UTC-3, so 00:00 to 01:00
        // happens twice. The week starts at the first 00:00, 02:00 UTC.
        var firstMidnight = new DateTimeOffset(2026, 11, 2, 2, 0, 0, TimeSpan.Zero);
        Assert.IsTrue(MondayMidnightDst.IsAmbiguousTime(new DateTime(2026, 11, 2, 0, 30, 0)), "The test zone repeats 00:00 to 01:00.");

        var week = MondayMidnightDstWeek(firstMidnight.AddMinutes(30));

        Assert.AreEqual(firstMidnight, week.Start);
        Assert.AreEqual(week, MondayMidnightDstWeek(firstMidnight.AddMinutes(90)), "00:30 the second time round is the same week.");
        Assert.AreEqual(firstMidnight, MondayMidnightDstWeek(firstMidnight.AddTicks(-1)).End);
        Assert.AreEqual(new DateTimeOffset(2026, 11, 9, 3, 0, 0, TimeSpan.Zero), week.End, "Monday 9 November 00:00 at UTC-3.");
        Assert.AreEqual(TimeSpan.FromHours(169), week.End - week.Start, "The week the clocks go back is an hour long.");
    }

    [TestMethod]
    public void Problems_RefuseAnUnknownTimeZoneAndBadNumbers()
    {
        Assert.AreEqual(0, new CompetitionOptions().Problems().Count);
        Assert.AreEqual(0, new CompetitionOptions { TimeZone = "utc" }.Problems().Count, "UTC in any case.");

        Assert.AreEqual(1, new CompetitionOptions { TimeZone = "Mars/Olympus_Mons" }.Problems().Count);
        Assert.AreEqual(1, new CompetitionOptions { TimeZone = "" }.Problems().Count);
        Assert.AreEqual(1, new CompetitionOptions { WeekStartTime = TimeSpan.FromHours(24) }.Problems().Count);
        Assert.AreEqual(1, new CompetitionOptions { WeekStartTime = TimeSpan.FromMinutes(-1) }.Problems().Count);
        Assert.AreEqual(1, new CompetitionOptions { WeekStartDay = (DayOfWeek)7 }.Problems().Count);
        Assert.AreEqual(3, new CompetitionOptions { MinAccountAgeDays = -1, MinPlaytimeMinutes = -1, MinimumVotes = -1 }.Problems().Count);

        Assert.ThrowsExactly<InvalidOperationException>(() => new CompetitionOptions { TimeZone = "Mars/Olympus_Mons" }.WeekContaining(Wednesday));
    }

    [TestMethod]
    public void Defaults_AreTheDesignsPlaceholders_AndTheTrackedConfigMatches()
    {
        var options = new CompetitionOptions();

        Assert.AreEqual("UTC", options.TimeZone);
        Assert.AreEqual(DayOfWeek.Monday, options.WeekStartDay);
        Assert.AreEqual(TimeSpan.Zero, options.WeekStartTime);
        Assert.AreEqual(3, options.MinAccountAgeDays);
        Assert.AreEqual(120, options.MinPlaytimeMinutes);
        Assert.AreEqual(10, options.MinimumVotes);

        using var document = JsonDocument.Parse(ReadSource("Sanctuary.Gateway/gateway.json"));
        var tracked = document.RootElement.GetProperty(CompetitionOptions.Section);

        Assert.AreEqual(options.TimeZone, tracked.GetProperty(nameof(CompetitionOptions.TimeZone)).GetString());
        Assert.AreEqual(options.WeekStartDay.ToString(), tracked.GetProperty(nameof(CompetitionOptions.WeekStartDay)).GetString());
        Assert.AreEqual(options.WeekStartTime, TimeSpan.Parse(tracked.GetProperty(nameof(CompetitionOptions.WeekStartTime)).GetString()!));
        Assert.AreEqual(options.MinAccountAgeDays, tracked.GetProperty(nameof(CompetitionOptions.MinAccountAgeDays)).GetInt32());
        Assert.AreEqual(options.MinPlaytimeMinutes, tracked.GetProperty(nameof(CompetitionOptions.MinPlaytimeMinutes)).GetInt32());
        Assert.AreEqual(options.MinimumVotes, tracked.GetProperty(nameof(CompetitionOptions.MinimumVotes)).GetInt32());
    }

    private static string ReadSource(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, path);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException($"{path} not found above the test output.");
    }
}
