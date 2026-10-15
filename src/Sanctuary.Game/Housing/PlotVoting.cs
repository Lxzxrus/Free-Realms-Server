using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Database.Entities;

namespace Sanctuary.Game.Housing;

public enum PlotVoteResult
{
    /// <summary>The account's first vote for this plot this week.</summary>
    Cast,

    /// <summary>The account had voted for this plot this week, with a different number of stars.</summary>
    Changed,

    /// <summary>The account had already given this plot the same stars this week.</summary>
    Unchanged,

    /// <summary>Not 1 to 5 stars.</summary>
    InvalidStars,

    /// <summary>
    /// The house isn't in the competition: it isn't its account's ranked plot, or it isn't published, or it's locked.
    /// </summary>
    NotEntered,

    /// <summary>The plot belongs to the voter's own account.</summary>
    OwnAccount,

    /// <summary>The voter's account is younger than <see cref="CompetitionOptions.MinAccountAgeDays"/>.</summary>
    AccountTooNew,

    /// <summary>The voter's account has played less than <see cref="CompetitionOptions.MinPlaytimeMinutes"/>.</summary>
    NotEnoughPlaytime,

    /// <summary>No such voter or house.</summary>
    NotFound
}

/// <param name="PreviousStars">The account's vote for the plot this week before this one, or 0 if it had none.</param>
/// <param name="EligibleFrom">When the voter's account is old enough to vote.</param>
/// <param name="PlaytimeMinutes">The voter's account's playtime, the current session included.</param>
public readonly record struct PlotVoteOutcome(
    PlotVoteResult Result,
    int PreviousStars = 0,
    DateTimeOffset EligibleFrom = default,
    long PlaytimeMinutes = 0);

/// <summary>
/// Voting in the weekly competition (<c>docs/design/evergrove-housing.md</c>, section 3): one vote per account per
/// ranked plot per week, which the account can change until the week ends. A plot's votes count from when it last
/// became ranked (<see cref="DbHouse.RankedSince"/>).
/// </summary>
public static class PlotVoting
{
    public const int MinStars = 1;
    public const int MaxStars = 5;

    /// <summary>
    /// Records <paramref name="stars"/> from the account of <paramref name="voterCharacterId"/> for the house, in the
    /// competition week that contains <paramref name="now"/>. Saves the change. Run it in a transaction: the primary key
    /// stops a second vote from the same account, and on MariaDB a race between two votes ends in a deadlock, which the
    /// execution strategy retries with a new transaction.
    /// </summary>
    public static PlotVoteOutcome Cast(
        DatabaseContext dbContext,
        ulong voterCharacterId,
        ulong houseId,
        int stars,
        DateTimeOffset now,
        CompetitionOptions options)
    {
        if (stars is < MinStars or > MaxStars)
            return new PlotVoteOutcome(PlotVoteResult.InvalidStars);

        var house = dbContext.Houses
            .AsNoTracking()
            .Where(candidate => candidate.Id == houseId)
            .Select(candidate => new
            {
                candidate.Plot,
                candidate.IsPublished,
                candidate.IsLocked,
                candidate.RankedSince,
                OwnerUserId = candidate.Character.UserId
            })
            .FirstOrDefault();

        var voter = dbContext.Characters
            .AsNoTracking()
            .Where(character => character.Id == voterCharacterId)
            .Select(character => new
            {
                character.UserId,
                character.LastLogin,
                AccountCreated = character.User.Created,
                SavedPlaytime = character.User.Characters.Sum(accountCharacter => accountCharacter.PlayTime)
            })
            .FirstOrDefault();

        if (house is null || voter is null)
            return new PlotVoteOutcome(PlotVoteResult.NotFound);

        if (!IsEntered(house.Plot, house.IsPublished, house.IsLocked))
            return new PlotVoteOutcome(PlotVoteResult.NotEntered);

        if (voter.UserId == house.OwnerUserId)
            return new PlotVoteOutcome(PlotVoteResult.OwnAccount);

        // The voter is online, so their last login is when the session in progress started.
        var playtime = VoteEligibility.Playtime(voter.SavedPlaytime, voter.LastLogin, now);
        var eligibleFrom = VoteEligibility.EligibleFrom(voter.AccountCreated, options);

        var problem = VoteEligibility.Check(voter.AccountCreated, playtime, now, options) switch
        {
            VoteEligibilityProblem.AccountTooNew => PlotVoteResult.AccountTooNew,
            VoteEligibilityProblem.NotEnoughPlaytime => PlotVoteResult.NotEnoughPlaytime,
            _ => (PlotVoteResult?)null
        };

        if (problem is PlotVoteResult refused)
            return new PlotVoteOutcome(refused, EligibleFrom: eligibleFrom, PlaytimeMinutes: playtime);

        var weekStart = options.WeekContaining(now).Start;
        var vote = dbContext.PlotVotes.FirstOrDefault(candidate =>
            candidate.HouseId == houseId &&
            candidate.UserId == voter.UserId &&
            candidate.WeekStart == weekStart);

        // A vote from before the house last became the ranked plot doesn't count, so this one is a new vote.
        var counted = vote is not null && IsCounted(vote.Updated, house.RankedSince);
        var previousStars = counted ? vote!.Value : 0;

        if (counted && vote!.Value == stars)
            return new PlotVoteOutcome(PlotVoteResult.Unchanged, previousStars, eligibleFrom, playtime);

        if (vote is null)
        {
            vote = new DbPlotVote
            {
                HouseId = houseId,
                UserId = voter.UserId,
                WeekStart = weekStart,
                Created = now
            };
            dbContext.PlotVotes.Add(vote);
        }

        vote.OwnerUserId = house.OwnerUserId;
        vote.CharacterId = voterCharacterId;
        vote.Value = stars;
        vote.Updated = now;
        dbContext.SaveChanges();

        return new PlotVoteOutcome(
            counted ? PlotVoteResult.Changed : PlotVoteResult.Cast,
            previousStars,
            eligibleFrom,
            playtime);
    }

    /// <summary>
    /// Whether a house is entered in the competition: its account's ranked plot, published and not locked. A locked
    /// house is out of the directory and only the owner's friends may come in, so it can't take votes from everyone.
    /// </summary>
    public static bool IsEntered(HousePlot plot, bool isPublished, bool isLocked)
    {
        return plot == HousePlot.Ranked && isPublished && !isLocked;
    }

    /// <summary>
    /// The week's standings: every entered plot (see <see cref="IsEntered"/>), with its votes this week and its score.
    /// </summary>
    public static WeeklyStandings Standings(DatabaseContext dbContext, CompetitionWeek week, int minimumVotes)
    {
        var entered = dbContext.Houses
            .AsNoTracking()
            .Where(house => house.Plot == HousePlot.Ranked && house.IsPublished && !house.IsLocked)
            .Select(house => new { house.Id, house.RankedSince })
            .ToDictionary(house => house.Id, house => house.RankedSince);

        if (entered.Count == 0)
            return WeeklyStandings.Empty;

        // SQLite can't compare DateTimeOffset in SQL, so the votes since each plot became ranked are picked out after
        // loading. A week's votes are few.
        var enteredIds = entered.Keys.ToList();
        var weekStart = week.Start;
        var votes = dbContext.PlotVotes
            .AsNoTracking()
            .Where(vote => vote.WeekStart == weekStart && enteredIds.Contains(vote.HouseId))
            .Select(vote => new { vote.HouseId, vote.Value, vote.Updated })
            .ToList()
            .Where(vote => IsCounted(vote.Updated, entered[vote.HouseId]))
            .ToLookup(vote => vote.HouseId, vote => vote.Value);

        var tallies = entered.Keys.ToDictionary(
            houseId => houseId,
            houseId => votes[houseId] is var values && values.Any()
                ? new PlotTally(values.Count(), values.Average())
                : new PlotTally(0, 0));

        return WeeklyStandings.Compute(tallies, minimumVotes);
    }

    /// <summary>Whether a vote last cast or changed at <paramref name="updated"/> counts for a plot ranked since <paramref name="rankedSince"/>.</summary>
    private static bool IsCounted(DateTimeOffset updated, DateTimeOffset? rankedSince)
    {
        return rankedSince is not DateTimeOffset since || updated >= since;
    }
}
