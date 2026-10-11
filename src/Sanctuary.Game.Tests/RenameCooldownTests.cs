using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class RenameCooldownTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void NeverRenamed_MayRename()
    {
        Assert.AreEqual(TimeSpan.Zero, RenameCooldown.Remaining(null, Now));
    }

    [TestMethod]
    public void RenamedJustNow_WaitsThreeDays()
    {
        Assert.AreEqual(TimeSpan.FromDays(3), RenameCooldown.Remaining(Now, Now));
    }

    [TestMethod]
    public void RenamedOneDayAgo_WaitsTwoMoreDays()
    {
        Assert.AreEqual(TimeSpan.FromDays(2), RenameCooldown.Remaining(Now.AddDays(-1), Now));
    }

    [TestMethod]
    public void CooldownOver_MayRename()
    {
        Assert.AreEqual(TimeSpan.Zero, RenameCooldown.Remaining(Now.AddDays(-3), Now));
        Assert.AreEqual(TimeSpan.Zero, RenameCooldown.Remaining(Now.AddDays(-30), Now));
    }

    [TestMethod]
    [DataRow(3 * 24 * 60.0, "3 days")]
    [DataRow(2 * 24 * 60 + 5 * 60.0, "2 days and 5 hours")]
    [DataRow(24 * 60 + 60.0, "1 day and 1 hour")]
    [DataRow(3 * 60 + 1.0, "3 hours and 1 minute")]
    [DataRow(60.0, "1 hour")]
    [DataRow(12.0, "12 minutes")]
    [DataRow(0.2, "1 minute")]
    public void Describe_IsPlainAndRoundsUp(double minutes, string expected)
    {
        Assert.AreEqual(expected, RenameCooldown.Describe(TimeSpan.FromMinutes(minutes)));
    }

    [TestMethod]
    public void Describe_RoundsUpPartialMinutes()
    {
        // 2 days, 4 hours and 59.5 minutes is shown as 2 days and 5 hours, never earlier than it really is.
        Assert.AreEqual("2 days and 5 hours", RenameCooldown.Describe(TimeSpan.FromMinutes(2 * 24 * 60 + 4 * 60 + 59.5)));
    }

    [TestMethod]
    public void Message_SaysTheRuleAndTheWait()
    {
        Assert.AreEqual(
            "Characters can be renamed once every 3 days. You can rename again in 2 days.",
            RenameCooldown.Message(TimeSpan.FromDays(2)));
    }
}
