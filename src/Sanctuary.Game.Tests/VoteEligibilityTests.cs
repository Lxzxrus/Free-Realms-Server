using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class VoteEligibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly CompetitionOptions Options = new() { MinAccountAgeDays = 3, MinPlaytimeMinutes = 120 };

    [TestMethod]
    public void AccountAge_MustBeAtLeastTheSetting()
    {
        Assert.AreEqual(VoteEligibilityProblem.None, VoteEligibility.Check(Now.AddDays(-3), 120, Now, Options), "Exactly 3 days.");
        Assert.AreEqual(VoteEligibilityProblem.AccountTooNew, VoteEligibility.Check(Now.AddDays(-3).AddSeconds(1), 120, Now, Options));
        Assert.AreEqual(VoteEligibilityProblem.AccountTooNew, VoteEligibility.Check(Now, 10_000, Now, Options), "Playtime doesn't make up for age.");
        Assert.AreEqual(Now.AddDays(3), VoteEligibility.EligibleFrom(Now, Options));
    }

    [TestMethod]
    public void Playtime_MustBeAtLeastTheSetting()
    {
        var old = Now.AddDays(-30);

        Assert.AreEqual(VoteEligibilityProblem.None, VoteEligibility.Check(old, 120, Now, Options));
        Assert.AreEqual(VoteEligibilityProblem.NotEnoughPlaytime, VoteEligibility.Check(old, 119, Now, Options));
        Assert.AreEqual(VoteEligibilityProblem.NotEnoughPlaytime, VoteEligibility.Check(old, 0, Now, Options));
    }

    [TestMethod]
    public void SettingsAtZero_LetEveryAccountVote()
    {
        var open = new CompetitionOptions { MinAccountAgeDays = 0, MinPlaytimeMinutes = 0 };

        Assert.AreEqual(VoteEligibilityProblem.None, VoteEligibility.Check(Now, 0, Now, open));
    }

    [TestMethod]
    public void Playtime_CountsTheSessionInProgress()
    {
        Assert.AreEqual(100, VoteEligibility.Playtime(100, null, Now), "Offline, or never logged in.");
        Assert.AreEqual(145, VoteEligibility.Playtime(100, Now.AddMinutes(-45.9), Now), "Whole minutes, like the logout save.");
        Assert.AreEqual(100, VoteEligibility.Playtime(100, Now.AddMinutes(5), Now), "A login in the future counts nothing.");
    }
}
