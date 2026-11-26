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
