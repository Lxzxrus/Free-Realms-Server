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

    /// <summary>Attempts, dropped ones included, from all accounts together in <see cref="AttemptWindow"/>.</summary>
    public const int GlobalAttemptsPerWindow = 300;

    /// <summary>
    /// The most open reports about one player (or house owner) at a time. Staff already have that many to read; more would
    /// only be many accounts piling onto one victim.
    /// </summary>
    public const int MaxOpenReportsPerTarget = 10;

    /// <summary>The most open reports at once, of any kind. Closing some makes room.</summary>
    public const int MaxOpenReports = 1000;

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
