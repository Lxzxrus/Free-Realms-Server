using System;

namespace Sanctuary.Game.Housing;

/// <summary>Why an account may not vote yet, if it may not.</summary>
public enum VoteEligibilityProblem
{
    None,
    AccountTooNew,
    NotEnoughPlaytime
}

/// <summary>
/// Who may vote in the weekly competition (design section 3, Voting): an account at least
/// <see cref="CompetitionOptions.MinAccountAgeDays"/> old whose characters have played at least
/// <see cref="CompetitionOptions.MinPlaytimeMinutes"/> between them.
/// </summary>
public static class VoteEligibility
{
    /// <summary>
    /// Checks an account. <paramref name="playtimeMinutes"/> is all its characters' play, the session in progress
    /// included.
    /// </summary>
    public static VoteEligibilityProblem Check(
        DateTimeOffset accountCreated,
        long playtimeMinutes,
        DateTimeOffset now,
        CompetitionOptions options)
    {
        if (now < EligibleFrom(accountCreated, options))
            return VoteEligibilityProblem.AccountTooNew;

        if (playtimeMinutes < options.MinPlaytimeMinutes)
            return VoteEligibilityProblem.NotEnoughPlaytime;

        return VoteEligibilityProblem.None;
    }

    /// <summary>When an account created at <paramref name="accountCreated"/> is old enough to vote.</summary>
    public static DateTimeOffset EligibleFrom(DateTimeOffset accountCreated, CompetitionOptions options)
    {
        return accountCreated.AddDays(options.MinAccountAgeDays);
    }

    /// <summary>
    /// An account's playtime in whole minutes: what its characters saved at their last logout, plus the session in
    /// progress for a character that is online now (since <paramref name="sessionStart"/>, its last login).
    /// </summary>
    public static long Playtime(long savedMinutes, DateTimeOffset? sessionStart, DateTimeOffset now)
    {
        if (sessionStart is not DateTimeOffset start || start > now)
            return savedMinutes;

        return savedMinutes + (long)(now - start).TotalMinutes;
    }
}
