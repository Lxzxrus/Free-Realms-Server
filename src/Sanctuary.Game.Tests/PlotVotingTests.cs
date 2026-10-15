using System;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class PlotVotingTests
{
    // Wednesday of the week that starts on Monday 12 October 2026.
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ThisMonday = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NextMonday = ThisMonday.AddDays(7);

    // The owner's account has two characters; so does the voter's.
    private const ulong OwnerUser = 1, OwnerCharacter = 10, OwnerAlt = 11;
    private const ulong VoterUser = 2, Voter = 20, VoterAlt = 21;
    private const ulong NewUser = 3, NewVoter = 30;
    private const ulong IdleUser = 4, IdleVoter = 40;

    private const ulong RankedPlot = 100;       // the owner's ranked plot, published
    private const ulong PersonalPlot = 101;     // the owner's alt's personal plot, published
    private const ulong VotersRankedPlot = 102; // the voter's ranked plot, published
    private const ulong UnpublishedPlot = 103;  // the idle account's ranked plot, not published

    private static readonly CompetitionOptions Options = new();

    private SqliteConnection _connection = null!;

    [TestInitialize]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var dbContext = CreateDbContext();
        dbContext.Database.Migrate();

        var longAgo = Now.AddDays(-60);

        dbContext.Users.AddRange(
            User(OwnerUser, longAgo, Character(OwnerCharacter, playTime: 600), Character(OwnerAlt)),
            User(VoterUser, longAgo, Character(Voter, playTime: 90), Character(VoterAlt, playTime: 30)),
            User(NewUser, Now.AddDays(-1), Character(NewVoter, playTime: 600)),
            User(IdleUser, longAgo, Character(IdleVoter, playTime: 30)));

        dbContext.Houses.AddRange(
            House(RankedPlot, OwnerCharacter, HousePlot.Ranked, isPublished: true),
            House(PersonalPlot, OwnerAlt, HousePlot.Personal, isPublished: true),
            House(VotersRankedPlot, Voter, HousePlot.Ranked, isPublished: true),
            House(UnpublishedPlot, IdleVoter, HousePlot.Ranked, isPublished: false));

        dbContext.SaveChanges();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection.Dispose();
    }

    private DatabaseContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder()
            .UseSqlite(_connection)
            .Options;

        return new SqliteDatabaseContext(options);
    }

    private static DbUser User(ulong id, DateTimeOffset created, params DbCharacter[] characters)
    {
        var user = new DbUser { Id = id, Username = $"user{id}", Password = string.Empty, Created = created };

        foreach (var character in characters)
            user.Characters.Add(character);

        return user;
    }

    private static DbCharacter Character(ulong id, int playTime = 0) => new()
    {
        Id = id,
        FirstName = $"Character{id}",
        Head = string.Empty,
        HeadId = 0,
        Hair = string.Empty,
        HairId = 0,
        SkinTone = string.Empty,
        SkinToneId = 0,
        PlayTime = playTime
    };

    private static DbHouse House(ulong id, ulong characterId, HousePlot plot, bool isPublished) => new()
    {
        Id = id,
        CharacterId = characterId,
        ZoneDefinitionId = (int)id,
        Plot = plot,
        IsPublished = isPublished
    };

    private PlotVoteOutcome Vote(ulong characterId, ulong houseId, int stars, DateTimeOffset? at = null, CompetitionOptions? options = null)
    {
        using var dbContext = CreateDbContext();
        return PlotVoting.Cast(dbContext, characterId, houseId, stars, at ?? Now, options ?? Options);
    }

    private DbPlotVote[] Votes()
    {
        using var dbContext = CreateDbContext();
        return dbContext.PlotVotes.AsNoTracking().ToArray();
    }

    private WeeklyStandings Standings(DateTimeOffset at)
    {
        using var dbContext = CreateDbContext();
        return PlotVoting.Standings(dbContext, Options.WeekContaining(at), Options.MinimumVotes);
    }

    /// <summary>The voter has 120 minutes: 90 and 30 on two characters.</summary>
    private static CompetitionOptions Exactly120Minutes => new() { MinPlaytimeMinutes = 120 };

    [TestMethod]
    public void OneVotePerAccountPerPlotPerWeek()
    {
        Assert.AreEqual(PlotVoteResult.Cast, Vote(Voter, RankedPlot, 4).Result);
        Assert.AreEqual(PlotVoteResult.Unchanged, Vote(Voter, RankedPlot, 4).Result);

        var vote = Votes().Single();
        Assert.AreEqual(RankedPlot, vote.HouseId);
        Assert.AreEqual(VoterUser, vote.UserId);
        Assert.AreEqual(OwnerUser, vote.OwnerUserId);
        Assert.AreEqual(Voter, vote.CharacterId);
        Assert.AreEqual(ThisMonday, vote.WeekStart);
        Assert.AreEqual(4, vote.Value);
    }

    [TestMethod]
    public void AnotherCharacterOnTheSameAccount_ChangesTheAccountsVote()
    {
        Vote(Voter, RankedPlot, 4);

        var outcome = Vote(VoterAlt, RankedPlot, 2);

        Assert.AreEqual(PlotVoteResult.Changed, outcome.Result);
        Assert.AreEqual(4, outcome.PreviousStars);

        var vote = Votes().Single();
        Assert.AreEqual(2, vote.Value);
        Assert.AreEqual(VoterAlt, vote.CharacterId, "The row records who changed it.");
    }

    [TestMethod]
    public void ChangingAVote_DuringTheWeek_ReplacesIt()
    {
        Vote(Voter, RankedPlot, 5, at: ThisMonday.AddHours(1));

        var outcome = Vote(Voter, RankedPlot, 1, at: Now);

        Assert.AreEqual(PlotVoteResult.Changed, outcome.Result);
        Assert.AreEqual(5, outcome.PreviousStars);

        var vote = Votes().Single();
        Assert.AreEqual(1, vote.Value);
        Assert.AreEqual(ThisMonday.AddHours(1), vote.Created);
        Assert.AreEqual(Now, vote.Updated);

        var standing = Standings(Now).For(RankedPlot);
        Assert.AreEqual(1, standing.Votes);
        Assert.AreEqual(1.0, standing.Average);
    }

    [TestMethod]
    public void WeekBoundary_ResetsVotes_AndKeepsTheOldWeeksAsHistory()
    {
        var lastSecond = NextMonday.AddSeconds(-1);

        Assert.AreEqual(PlotVoteResult.Cast, Vote(Voter, RankedPlot, 5, at: lastSecond).Result);
        Assert.AreEqual(PlotVoteResult.Cast, Vote(Voter, RankedPlot, 3, at: NextMonday).Result, "A new week: a new vote, not a change.");

        var votes = Votes().OrderBy(vote => vote.Updated).ToArray();
        Assert.HasCount(2, votes);
        Assert.AreEqual(ThisMonday, votes[0].WeekStart);
        Assert.AreEqual(5, votes[0].Value, "Last week's vote is kept as it was.");
        Assert.AreEqual(NextMonday, votes[1].WeekStart);

        Assert.AreEqual(5.0, Standings(lastSecond).For(RankedPlot).Average);
        Assert.AreEqual(3.0, Standings(NextMonday).For(RankedPlot).Average);
        Assert.AreEqual(1, Standings(NextMonday).For(RankedPlot).Votes);
        Assert.AreEqual(0, Standings(NextMonday.AddDays(7)).For(RankedPlot).Votes, "The week after starts empty.");
    }

    [TestMethod]
    public void WeekBoundary_FollowsTheTimeZoneSetting()
    {
        // In New York (UTC-4 in October) 01:00 UTC on Monday is still Sunday evening, last week.
        var newYork = new CompetitionOptions { TimeZone = "America/New_York", MinimumVotes = 10 };
        var mondayInUtc = ThisMonday.AddHours(1);

        Vote(Voter, RankedPlot, 5, at: mondayInUtc, options: newYork);

        Assert.AreEqual(new DateTimeOffset(2026, 10, 5, 4, 0, 0, TimeSpan.Zero), Votes().Single().WeekStart);
    }

    [TestMethod]
    public void NobodyVotesOnTheirOwnAccountsPlot()
    {
        Assert.AreEqual(PlotVoteResult.OwnAccount, Vote(OwnerCharacter, RankedPlot, 5).Result);
        Assert.AreEqual(PlotVoteResult.OwnAccount, Vote(OwnerAlt, RankedPlot, 5).Result, "Not from another character on the account either.");
        Assert.IsEmpty(Votes());
    }

    [TestMethod]
    public void Eligibility_NewAccountsCantVote()
    {
        var outcome = Vote(NewVoter, RankedPlot, 5);

        Assert.AreEqual(PlotVoteResult.AccountTooNew, outcome.Result);
        Assert.AreEqual(Now.AddDays(-1).AddDays(Options.MinAccountAgeDays), outcome.EligibleFrom);
        Assert.IsEmpty(Votes());

        Assert.AreEqual(PlotVoteResult.Cast, Vote(NewVoter, RankedPlot, 5, options: new CompetitionOptions { MinAccountAgeDays = 1 }).Result);
    }

    [TestMethod]
    public void Eligibility_PlaytimeIsTheWholeAccounts()
    {
        // 90 + 30 minutes across the voter's two characters.
        Assert.AreEqual(PlotVoteResult.Cast, Vote(Voter, RankedPlot, 5, options: Exactly120Minutes).Result);

        var outcome = Vote(IdleVoter, RankedPlot, 5, options: Exactly120Minutes);

        Assert.AreEqual(PlotVoteResult.NotEnoughPlaytime, outcome.Result);
        Assert.AreEqual(30, outcome.PlaytimeMinutes);
    }

    [TestMethod]
    public void Eligibility_CountsTheSessionInProgress()
    {
        Assert.AreEqual(PlotVoteResult.NotEnoughPlaytime, Vote(IdleVoter, RankedPlot, 5, options: Exactly120Minutes).Result);

        using (var dbContext = CreateDbContext())
        {
            // Logged in 90 minutes ago: 30 saved plus 90 now.
            var character = dbContext.Characters.Single(candidate => candidate.Id == IdleVoter);
            character.LastLogin = Now.AddMinutes(-90);
            dbContext.SaveChanges();
        }

        var outcome = Vote(IdleVoter, RankedPlot, 5, options: Exactly120Minutes);
        Assert.AreEqual(PlotVoteResult.Cast, outcome.Result);
        Assert.AreEqual(120, outcome.PlaytimeMinutes);
    }

    [TestMethod]
    public void OnlyPublishedRankedPlotsTakeVotes()
    {
        Assert.AreEqual(PlotVoteResult.NotEntered, Vote(Voter, PersonalPlot, 5).Result, "Published, but personal.");
        Assert.AreEqual(PlotVoteResult.NotEntered, Vote(Voter, UnpublishedPlot, 5).Result, "Ranked, but not published.");
        Assert.AreEqual(PlotVoteResult.NotFound, Vote(Voter, 999, 5).Result);
        Assert.IsEmpty(Votes());
    }

    [TestMethod]
    public void AVoteIsOneToFiveStars()
    {
        Assert.AreEqual(PlotVoteResult.InvalidStars, Vote(Voter, RankedPlot, 0).Result);
        Assert.AreEqual(PlotVoteResult.InvalidStars, Vote(Voter, RankedPlot, 6).Result);
        Assert.AreEqual(PlotVoteResult.Cast, Vote(Voter, RankedPlot, 1).Result);
        Assert.AreEqual(PlotVoteResult.Changed, Vote(Voter, RankedPlot, 5).Result);
    }

    [TestMethod]
    public void Standings_ScoreEveryEnteredPlot_WithTheWeightedFormula()
    {
        // Ranked plot: 5 from the voter. The voter's plot: 3 from the owner's account.
        Vote(Voter, RankedPlot, 5);
        Vote(OwnerAlt, VotersRankedPlot, 3);

        var standings = Standings(Now);

        // One vote each, so C = (5 + 3) / 2; score = (1 × R + 10 × 4) / 11.
        Assert.AreEqual(4.0, standings.OverallAverage, 1e-9);
        Assert.AreEqual(45.0 / 11, standings.For(RankedPlot).Score, 1e-9);
        Assert.AreEqual(43.0 / 11, standings.For(VotersRankedPlot).Score, 1e-9);

        Assert.IsFalse(standings.Plots.ContainsKey(PersonalPlot), "A personal plot isn't in the competition.");
        Assert.IsFalse(standings.Plots.ContainsKey(UnpublishedPlot), "Nor is an unpublished ranked plot.");
    }

    [TestMethod]
    public void Standings_LeaveOutVotesForAPlotThatLeftTheCompetition()
    {
        Vote(Voter, RankedPlot, 5);
        Vote(OwnerAlt, VotersRankedPlot, 3);

        using (var dbContext = CreateDbContext())
            HousePlots.MakePersonal(dbContext, RankedPlot, OwnerCharacter);

        var standings = Standings(Now);
        Assert.IsFalse(standings.Plots.ContainsKey(RankedPlot));
        Assert.AreEqual(3.0, standings.OverallAverage, 1e-9, "Only the plot still entered counts towards C.");
        Assert.HasCount(2, Votes(), "The vote itself is kept.");
    }

    /// <summary>
    /// An account can't run two builds in one week and keep whichever scored better: a plot's votes count from when it
    /// last became the ranked plot.
    /// </summary>
    [TestMethod]
    public void MovingTheRankedPlotAwayAndBack_DoesntBringItsOldVotesBack()
    {
        Vote(Voter, RankedPlot, 5, at: Now.AddHours(-2));

        using (var dbContext = CreateDbContext())
        {
            HousePlots.MakeRanked(dbContext, PersonalPlot, OwnerAlt, Now.AddHours(-1));
            HousePlots.MakeRanked(dbContext, RankedPlot, OwnerCharacter, Now);
        }

        Assert.AreEqual(PlotStanding.None, Standings(Now).For(RankedPlot), "The vote from before the move doesn't count.");

        var again = Vote(Voter, RankedPlot, 5, at: Now.AddMinutes(1));
        Assert.AreEqual(PlotVoteResult.Cast, again.Result, "Voting again is a new vote, even with the same stars.");
        Assert.AreEqual(0, again.PreviousStars);

        Assert.AreEqual(1, Standings(Now.AddMinutes(1)).For(RankedPlot).Votes);
        Assert.HasCount(1, Votes(), "Still one row for the account, plot and week.");
    }

    [TestMethod]
    public void ALockedPlotIsntInTheCompetition()
    {
        Vote(Voter, RankedPlot, 5);
        SetLocked(RankedPlot, true);

        Assert.AreEqual(PlotVoteResult.NotEntered, Vote(VoterAlt, RankedPlot, 2).Result);
        Assert.IsFalse(Standings(Now).Plots.ContainsKey(RankedPlot));

        SetLocked(RankedPlot, false);

        Assert.AreEqual(5.0, Standings(Now).For(RankedPlot).Average, "Unlocked, its votes count again.");
    }

    /// <summary>Past votes are kept for history and moderation, even when the owner deletes the character and house.</summary>
    [TestMethod]
    public void VotesOutliveTheHouse_WithTheOwnersAccount()
    {
        Vote(Voter, RankedPlot, 4);

        using (var dbContext = CreateDbContext())
        {
            dbContext.Characters.Remove(dbContext.Characters.Single(character => character.Id == OwnerCharacter));
            dbContext.SaveChanges();

            Assert.IsFalse(dbContext.Houses.Any(house => house.Id == RankedPlot), "The house went with its character.");
        }

        var vote = Votes().Single();
        Assert.AreEqual(RankedPlot, vote.HouseId);
        Assert.AreEqual(OwnerUser, vote.OwnerUserId);
        Assert.AreEqual(VoterUser, vote.UserId);
        Assert.IsFalse(Standings(Now).Plots.ContainsKey(RankedPlot));
    }

    private void SetLocked(ulong houseId, bool isLocked)
    {
        using var dbContext = CreateDbContext();
        dbContext.Houses.Single(house => house.Id == houseId).IsLocked = isLocked;
        dbContext.SaveChanges();
    }
}
