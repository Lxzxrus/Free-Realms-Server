using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.ChatCommands;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Reports;

/// <summary>What <c>!mod reports</c>, <c>!mod report</c>, <c>!mod close</c> and <c>!mod history</c> read and write, and how they show it.</summary>
public static class ReportQueries
{
    /// <summary>How many reports <c>!mod reports</c> lists.</summary>
    public const int ListLimit = 10;

    /// <summary>How many reports about a player <c>!mod history</c> lists.</summary>
    public const int HistoryLimit = 5;

    /// <summary>The longest a line of <c>!mod report</c>'s answer is, so one report cannot flood a staff member's chat.</summary>
    public const int MaxLineLength = 140;

    /// <summary>The most lines of chat evidence <c>!mod report</c> shows from each side.</summary>
    public const int MaxEvidenceLines = 10;

    /// <summary>
    /// The most lines of a bug report's own text <c>!mod report</c> shows. Its description is one long line (up to
    /// <see cref="ReportLimits.MaxBugDescriptionLength"/> characters), so it is wrapped over lines of
    /// <see cref="MaxLineLength"/> instead of cut at one.
    /// </summary>
    public const int MaxBugTextLines = 16;

    /// <summary>Who is reading the reports, so that reports they may not read are left out.</summary>
    public readonly record struct Viewer(ulong UserId, ChatCommandRole Role);

    /// <summary>
    /// The reports a staff member may read: the same rule as for closing one (<see cref="ModChatCommand.MayCloseReportAbout"/>).
    /// Reports about the staff member themselves, and about staff they do not outrank, are left out, so nobody can read
    /// what was said about them (or learn who said it) and then act on it. Reports about no known account are open to all.
    /// </summary>
    internal static IQueryable<DbReport> VisibleTo(DatabaseContext dbContext, IQueryable<DbReport> query, Viewer viewer)
    {
        var userId = viewer.UserId;

        if (viewer.Role >= ChatCommandRole.Admin)
            return query.Where(report => report.TargetUserId == null || report.TargetUserId != userId);

        return query.Where(report => report.TargetUserId == null
            || (report.TargetUserId != userId && !dbContext.Users.Any(user => user.Id == report.TargetUserId && (user.IsAdmin || user.IsMod))));
    }

    private static IQueryable<DbReport> Filter(DatabaseContext dbContext, bool openOnly, ReportKind? kind, Viewer? viewer)
    {
        var query = dbContext.Reports.AsNoTracking();

        if (openOnly)
            query = query.Where(report => report.Status == ReportStatus.Open);

        if (kind is { } only)
            query = query.Where(report => report.Kind == only);

        if (viewer is { } who)
            query = VisibleTo(dbContext, query, who);

        return query;
    }

    /// <summary>One page of reports, newest first. Pages count from 1 and are <see cref="ListLimit"/> reports long.</summary>
    public static List<DbReport> List(DatabaseContext dbContext, bool openOnly, ReportKind? kind = null, int page = 1, Viewer? viewer = null)
    {
        var skip = (Math.Max(page, 1) - 1) * ListLimit;

        return NewestFirst(dbContext, Filter(dbContext, openOnly, kind, viewer)).Skip(skip).Take(ListLimit).ToList();
    }

    /// <summary>How many reports <see cref="List"/> pages over.</summary>
    public static int Count(DatabaseContext dbContext, bool openOnly, ReportKind? kind = null, Viewer? viewer = null) =>
        Filter(dbContext, openOnly, kind, viewer).Count();

    /// <summary>
    /// Newest first, by id: it grows with time. SQLite can't sort a ulong (nor a DateTimeOffset), so there it sorts by a
    /// cast to long. MySQL sorts the plain column, so the primary key serves the ORDER BY: a cast there would be an
    /// expression no index can satisfy, and every listing would sort the whole table.
    /// </summary>
    internal static IQueryable<DbReport> NewestFirst(DatabaseContext dbContext, IQueryable<DbReport> query) =>
        IsSqlite(dbContext) ? query.OrderByDescending(report => (long)report.Id) : query.OrderByDescending(report => report.Id);

    private static bool IsSqlite(DatabaseContext dbContext) =>
        dbContext.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    public static DbReport? Get(DatabaseContext dbContext, ulong id) =>
        dbContext.Reports.AsNoTracking().FirstOrDefault(report => report.Id == id);

    /// <summary>
    /// The role of an account from its stored flags (the same in Debug and Release), or null for no account (none given,
    /// or it has been deleted).
    /// </summary>
    public static ChatCommandRole? GetUserRole(DatabaseContext dbContext, ulong? userId)
    {
        if (userId is not { } id)
            return null;

        var flags = dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == id)
            .Select(user => new { user.IsAdmin, user.IsMod })
            .FirstOrDefault();

        return flags is null ? null : ChatHelper.GetStoredRole(flags.IsAdmin, flags.IsMod);
    }

    /// <summary>Closes an open report. False if there is none with that id, or it is already closed.</summary>
    public static bool TryClose(DatabaseContext dbContext, ulong id, ulong staffUserId, string? note, DateTimeOffset now)
    {
        var closedNote = ModArguments.CleanText(note, DbReport.MaxCloseNoteLength);

        return dbContext.Reports
            .Where(report => report.Id == id && report.Status == ReportStatus.Open)
            .ExecuteUpdate(report => report
                .SetProperty(r => r.Status, ReportStatus.Closed)
                .SetProperty(r => r.ClosedByUserId, (ulong?)staffUserId)
                .SetProperty(r => r.ClosedAt, (DateTimeOffset?)now)
                .SetProperty(r => r.CloseNote, closedNote)) > 0;
    }

    /// <summary>
    /// Closes every open report of a kind whose target is not staff, and returns how many. Reports about staff are left
    /// for <see cref="TryClose"/>, which the rank rule governs one at a time. Reports about no account (a bug, or a name
    /// nobody has) are closed.
    /// </summary>
    public static int CloseAll(DatabaseContext dbContext, ReportKind kind, ulong staffUserId, string? note, DateTimeOffset now)
    {
        var closedNote = ModArguments.CleanText(note, DbReport.MaxCloseNoteLength);

        return dbContext.Reports
            .Where(report => report.Status == ReportStatus.Open && report.Kind == kind
                && (report.TargetUserId == null || !dbContext.Users.Any(user => user.Id == report.TargetUserId && (user.IsAdmin || user.IsMod))))
            .ExecuteUpdate(report => report
                .SetProperty(r => r.Status, ReportStatus.Closed)
                .SetProperty(r => r.ClosedByUserId, (ulong?)staffUserId)
                .SetProperty(r => r.ClosedAt, (DateTimeOffset?)now)
                .SetProperty(r => r.CloseNote, closedNote));
    }

    /// <summary>The report kind a word names (<c>bug</c>, <c>player</c>, <c>house</c>), or null.</summary>
    public static ReportKind? ParseKind(string? word) => word?.ToLowerInvariant() switch
    {
        "bug" or "bugs" => ReportKind.Bug,
        "player" or "players" => ReportKind.Player,
        "house" or "houses" => ReportKind.House,
        _ => null
    };

    /// <summary>How many reports an account has had made about it, and the last <see cref="HistoryLimit"/>, newest first.</summary>
    public static (int Total, List<DbReport> Recent) LoadAbout(DatabaseContext dbContext, ulong userId)
    {
        var total = dbContext.Reports.Count(report => report.TargetUserId == userId);

        var recent = NewestFirst(dbContext, dbContext.Reports.AsNoTracking().Where(report => report.TargetUserId == userId))
            .Take(HistoryLimit)
            .ToList();

        return (total, recent);
    }

    public static string KindName(ReportKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>One short line: <c>#12 2026-11-25 14:03 player open: Bob Smith reported Griefer</c>.</summary>
    public static string FormatListLine(DbReport report)
    {
        var status = report.Status == ReportStatus.Open ? "open" : "closed";
        var subject = report.Kind == ReportKind.Bug
            ? $"bug from {Show(report.ReporterName)}"
            : $"{Show(report.ReporterName)} reported {Show(report.TargetName)}";

        return Cap($"#{report.Id} {report.Created.UtcDateTime:yyyy-MM-dd HH:mm} {KindName(report.Kind)} {status}: {subject}");
    }

    /// <summary>The lines <c>!mod history</c> adds: how many reports are about the player, then the last few.</summary>
    public static List<string> FormatAboutLines(int total, IReadOnlyList<DbReport> recent)
    {
        var lines = new List<string>(recent.Count + 1)
        {
            total == 0
                ? "No reports about them."
                : total <= recent.Count ? $"{total} report(s) about them:" : $"{total} reports about them, the last {recent.Count}:"
        };

        foreach (var report in recent)
        {
            var status = report.Status == ReportStatus.Open ? "open" : "closed";

            lines.Add(Cap($"#{report.Id} {report.Created.UtcDateTime:yyyy-MM-dd} {KindName(report.Kind)} {status}, by {Show(report.ReporterName)}"));
        }

        return lines;
    }

    /// <summary>
    /// The lines of <c>!mod report &lt;id&gt;</c>: a bounded number, each capped. The client's words and the server's own
    /// record are kept apart so staff can compare them.
    /// </summary>
    public static List<string> FormatDetails(DbReport report)
    {
        var lines = new List<string>
        {
            Cap($"Report #{report.Id}, {KindName(report.Kind)}, {(report.Status == ReportStatus.Open ? "open" : "closed")}, {report.Created.UtcDateTime:yyyy-MM-dd HH:mm} UTC."),
            Cap($"From {Show(report.ReporterName)} (user {report.ReporterUserId}) in {Show(report.Zone)} at {Show(report.Position)}.")
        };

        if (report.Kind == ReportKind.House)
            lines.Add(Cap($"About house {report.HouseId}, owned by {Show(report.TargetName)} (user {report.TargetUserId})."));
        else if (report.Kind == ReportKind.Player)
            lines.Add(Cap($"About {Show(report.TargetName)}" + (report.TargetUserId is { } user ? $" (user {user})." : " (no such player known).")));

        if (report.Status == ReportStatus.Closed)
        {
            var note = ModArguments.CleanText(report.CloseNote, DbReport.MaxCloseNoteLength);

            // A report the server closed itself (CloseStale) has no staff member.
            var closer = report.ClosedByUserId is { } closedBy ? $"by user {closedBy}" : "automatically";

            lines.Add(Cap($"Closed {report.ClosedAt?.UtcDateTime:yyyy-MM-dd HH:mm} {closer}" + (note is null ? "." : $": {note}")));
        }

        if (report.Kind == ReportKind.Bug)
        {
            lines.Add("The report:");
            AddWrapped(lines, report.ClientText, MaxBugTextLines);
        }
        else
        {
            lines.Add("What the reporter's client says:");
            AddEvidence(lines, report.ClientText);
        }

        if (report.ServerContext.Length > 0)
        {
            lines.Add("What the server saw:");
            AddEvidence(lines, report.ServerContext);
        }

        return lines;
    }

    private static void AddEvidence(List<string> lines, string text)
    {
        var parts = text.Split('\n');

        foreach (var part in parts.Take(MaxEvidenceLines))
            lines.Add(Cap("  " + (ModArguments.CleanText(part, MaxLineLength) ?? string.Empty)));

        if (parts.Length > MaxEvidenceLines)
            lines.Add($"  ... {parts.Length - MaxEvidenceLines} more line(s) not shown.");
    }

    /// <summary>
    /// A bug report's text: each of its lines is wrapped over as many lines of at most <see cref="MaxLineLength"/>
    /// characters as it needs, up to <paramref name="maxLines"/> in all. What does not fit is not dropped silently: a last line
    /// says how many characters are left in the Reports table.
    /// </summary>
    internal static void AddWrapped(List<string> lines, string text, int maxLines)
    {
        const string Indent = "  ";
        var width = MaxLineLength - Indent.Length;
        var shown = 0;
        var cut = false;
        var wrapped = new List<string>();

        foreach (var part in text.Split('\n'))
        {
            var clean = ModArguments.CleanText(part, DbReport.MaxClientTextLength) ?? string.Empty;

            do
            {
                var length = clean.Length;

                if (length > width)
                {
                    // Break at the last space that fits, if there is one past the first quarter of the line.
                    length = clean.LastIndexOf(' ', width) is var space && space > width / 4 ? space : width;

                    // Do not leave half of a surrogate pair at the end of the line.
                    if (char.IsHighSurrogate(clean[length - 1]))
                        length--;
                }

                if (wrapped.Count < maxLines)
                {
                    wrapped.Add(Indent + clean[..length]);
                    shown += length;
                }
                else
                {
                    cut = true;
                }

                clean = clean[length..].TrimStart();
            } while (clean.Length > 0);
        }

        lines.AddRange(wrapped);

        if (cut)
            lines.Add($"{Indent}... cut here: about {Math.Max(0, text.Length - shown)} more character(s) are stored in the Reports table.");
    }

    /// <summary>The one-line system message online staff get for a new report.</summary>
    public static string FormatStaffNotice(DbReport report, string summary)
    {
        if (report.Kind == ReportKind.Bug)
        {
            var category = ModArguments.CleanText(summary, 40) ?? "no category";

            return $"Bug report #{report.Id} from {Show(report.ReporterName)}: {category}";
        }

        return $"Report #{report.Id}: {Show(report.ReporterName)} reported {Show(report.TargetName)} ({KindName(report.Kind)}). !mod report {report.Id}";
    }

    private static string Show(string? text) => ModArguments.CleanText(text, 64) ?? "?";

    private static string Cap(string text) => text.Length <= MaxLineLength ? text : StaffActionLog.Truncate(text, MaxLineLength - 3) + "...";
}
