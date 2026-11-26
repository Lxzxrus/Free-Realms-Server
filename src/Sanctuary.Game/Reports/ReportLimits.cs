using System;

namespace Sanctuary.Game.Reports;

/// <summary>The limits on in-game reports. Everything in a report comes from a client that may not be the real one.</summary>
public static class ReportLimits
{
    /// <summary>Player and house reports one account may make an hour, and a day.</summary>
    public const int ReportsPerHour = 5;
    public const int ReportsPerDay = 20;

    /// <summary>Bug reports one account may make an hour, and a day.</summary>
    public const int BugsPerHour = 5;
    public const int BugsPerDay = 20;

    /// <summary>
    /// The same limits across every account together. The per-account limits stop one account; these stop many cheap
    /// ones: reports and bug reports each have their own so that a flood of one cannot block the other.
    /// </summary>
    public const int GlobalReportsPerHour = 30;
    public const int GlobalReportsPerDay = 200;
    public const int GlobalBugsPerHour = 30;
    public const int GlobalBugsPerDay = 200;

    /// <summary>
    /// Accounts younger than <see cref="EstablishedAccountAge"/> may use only part of the global allowance above, so a
    /// crowd of cheap new accounts cannot use all of it and shut out the players who have been here a while. The global
    /// counts are charged only for reports that were kept, never for attempts: an attempt costs the attacker nothing.
    /// </summary>
    public const int GlobalNewAccountReportsPerHour = 20;
    public const int GlobalNewAccountReportsPerDay = 130;
    public const int GlobalNewAccountBugsPerHour = 20;
    public const int GlobalNewAccountBugsPerDay = 130;

    public static readonly TimeSpan EstablishedAccountAge = TimeSpan.FromDays(7);

    /// <summary>
    /// The most open reports about one player (or house owner) at a time. Staff already have that many to read; more would
    /// only be many accounts piling onto one victim.
    /// </summary>
    public const int MaxOpenReportsPerTarget = 10;

    /// <summary>
    /// The most open player and house reports at once. Closing some makes room. Bug reports have their own ceiling
    /// (<see cref="MaxOpenBugReports"/>) so that a flood of one kind cannot block the other.
    /// </summary>
    public const int MaxOpenReports = 1000;

    /// <summary>The most open bug reports at once.</summary>
    public const int MaxOpenBugReports = 500;

    /// <summary>Staff are warned in the log when the open reports of a kind reach this share of their ceiling.</summary>
    public const int OpenWarningPercent = 80;

    /// <summary>An open report nobody has dealt with after this long is closed automatically, so the ceiling cannot be held up for ever.</summary>
    public static readonly TimeSpan OpenRetention = TimeSpan.FromDays(60);

    /// <summary>The longest between two warnings in the log about the same limit being reached.</summary>
    public static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(10);

    /// <summary>An account has to be at least this old to report, so a throwaway account cannot report at once.</summary>
    public static readonly TimeSpan MinAccountAge = TimeSpan.FromMinutes(10);

    /// <summary>Closed reports are deleted after this long.</summary>
    public static readonly TimeSpan ClosedRetention = TimeSpan.FromDays(30);

    /// <summary>
    /// Reports of any kind one account may even try in <see cref="AttemptWindow"/>, dropped ones included. Without it a
    /// client repeating a duplicate report would make a database query for each.
    /// </summary>
    public const int AttemptsPerWindow = 30;

    public static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(10);

    /// <summary>The longest a name the client typed may be, after cleaning.</summary>
    public const int MaxTargetNameLength = 64;

    /// <summary>The longest one chat line, or one of the bug report's short fields, in a stored report.</summary>
    public const int MaxLineLength = 256;

    public const int MaxBugCategoryLength = 64;
    public const int MaxBugSeverityLength = 32;
    public const int MaxBugDescriptionLength = 1500;

    /// <summary>The most characters of a bug report's XML read; a longer document is kept only as a cut copy.</summary>
    public const int MaxBugXmlLength = 65536;

    /// <summary>The longest cut copy of an XML that did not parse.</summary>
    public const int MaxRawBugLength = 2000;
}
