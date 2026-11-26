using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.ChatCommands;

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

    /// <summary>The last reports, newest first.</summary>
    public static List<DbReport> List(DatabaseContext dbContext, bool openOnly)
    {
        var query = dbContext.Reports.AsNoTracking();

        if (openOnly)
            query = query.Where(report => report.Status == ReportStatus.Open);

        // By id: it grows with time, and SQLite can't sort a DateTimeOffset or a ulong (hence the cast).
        return query.OrderByDescending(report => (long)report.Id).Take(ListLimit).ToList();
    }

    public static DbReport? Get(DatabaseContext dbContext, ulong id) =>
        dbContext.Reports.AsNoTracking().FirstOrDefault(report => report.Id == id);

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

    /// <summary>How many reports an account has had made about it, and the last <see cref="HistoryLimit"/>, newest first.</summary>
    public static (int Total, List<DbReport> Recent) LoadAbout(DatabaseContext dbContext, ulong userId)
    {
        var total = dbContext.Reports.Count(report => report.TargetUserId == userId);

        var recent = dbContext.Reports
            .AsNoTracking()
            .Where(report => report.TargetUserId == userId)
            .OrderByDescending(report => (long)report.Id)
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

            lines.Add(Cap($"Closed {report.ClosedAt?.UtcDateTime:yyyy-MM-dd HH:mm} by user {report.ClosedByUserId}" + (note is null ? "." : $": {note}")));
        }

        lines.Add(report.Kind == ReportKind.Bug ? "The report:" : "What the reporter's client says:");
        AddEvidence(lines, report.ClientText);

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
