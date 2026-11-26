using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.ChatCommands;

namespace Sanctuary.Game.Reports;

/// <summary>Who is reporting, and where they are. Built from the player by the packet handler.</summary>
public sealed record ReportContext(ulong UserId, ulong CharacterId, string Name, string Zone, string Position);

/// <summary>The reported player when they are online: who they are, and the chat lines the server accepted from them.</summary>
public sealed record OnlineReportTarget(ulong UserId, ulong CharacterId, string Name, IReadOnlyList<ChatEvidenceLine> Chat);

public enum ReportOutcome
{
    /// <summary>Saved.</summary>
    Accepted,

    /// <summary>Nothing to report: a self-report, a name or house the server does not know, or empty input.</summary>
    Ignored,

    /// <summary>The account has reported too much lately.</summary>
    Limited,

    /// <summary>The account already has an open report about this.</summary>
    Duplicate,

    /// <summary>The database failed; it was logged.</summary>
    Failed
}

/// <summary>The result of a report. <see cref="Summary"/> is the short line staff see for a bug (its category).</summary>
public readonly record struct ReportResult(ReportOutcome Outcome, DbReport? Report = null, string Summary = "");

/// <summary>
/// Takes in-game reports: applies the limits, drops self-reports and repeats, writes the row and passes it on to the
/// notifier. Nothing the client sent is believed: names and ids are checked against the database, and the chat lines
/// are kept as the client's words next to the server's own record. The database is used the way the packet handlers use
/// it everywhere else (a short synchronous call); a failure is logged and the report dropped, never thrown.
/// </summary>
public sealed class ReportService
{
    private const int MaxServerContextForChat = 3900;

    private readonly IDbContextFactory<DatabaseContext>? _dbContextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly ILogger _auditLogger;
    private readonly IReportNotifier _notifier;

    // When each kind of limit warning was last logged, and how many were held back since. The keys are a few fixed strings.
    private readonly Dictionary<string, (DateTimeOffset At, int Suppressed)> _warnings = [];

    public ReportService(IDbContextFactory<DatabaseContext>? dbContextFactory, TimeProvider timeProvider, ILoggerFactory loggerFactory,
        IReportNotifier notifier, ReportRateLimiter? limiter = null)
    {
        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;
        _logger = loggerFactory.CreateLogger<ReportService>();
        _auditLogger = loggerFactory.CreateLogger("Audit");
        _notifier = notifier;
        Limiter = limiter ?? new ReportRateLimiter(timeProvider);
    }

    public ReportRateLimiter Limiter { get; }

    /// <summary>A report on a player, from the chat log's "Report" or /report.</summary>
    public ReportResult SubmitPlayerReport(ReportContext reporter, string? reportedName, IReadOnlyList<(int Channel, string Message, ulong Time)> lines,
        OnlineReportTarget? online)
    {
        var name = ModArguments.CleanText(reportedName, ReportLimits.MaxTargetNameLength);

        if (name is null)
            return Dropped(ReportOutcome.Ignored, reporter, "no name");

        // Their own characters are not reported, by the name they typed or by the account behind the one they found.
        if (IsSameName(name, reporter.Name) || online?.UserId == reporter.UserId)
            return Dropped(ReportOutcome.Ignored, reporter, "self-report");

        if (Gate(reporter, ReportBudget.Report) is { } refused)
            return refused;

        try
        {
            var now = _timeProvider.GetUtcNow();

            if (_dbContextFactory is null)
                return NoDatabase();

            using var dbContext = _dbContextFactory.CreateDbContext();

            ulong? targetUserId = online?.UserId;
            ulong? targetCharacterId = online?.CharacterId;
            string targetName = online?.Name ?? name;

            if (online is null)
            {
                var found = dbContext.Characters
                    .AsNoTracking()
                    .Where(character => character.FullName == name)
                    .Select(character => new { character.Id, character.UserId, character.FullName })
                    .FirstOrDefault();

                if (found is not null)
                {
                    targetUserId = found.UserId;
                    targetCharacterId = found.Id;
                    targetName = found.FullName ?? name;
                }
            }

            if (targetUserId == reporter.UserId)
                return Dropped(ReportOutcome.Ignored, reporter, "self-report");

            if (HasOpenReport(dbContext, reporter.UserId, ReportKind.Player, targetUserId, targetName, null))
                return Dropped(ReportOutcome.Duplicate, reporter, "already open");

            if (Screen(dbContext, reporter, now, ReportKind.Player, targetUserId, targetName, out var established) is { } screened)
                return screened;

            var report = NewReport(reporter, ReportKind.Player, now);

            report.TargetName = Cap(targetName, DbReport.MaxTargetNameLength);
            report.TargetUserId = targetUserId;
            report.TargetCharacterId = targetCharacterId;
            report.ClientText = FormatClientLines(lines);
            report.ServerContext = online is not null
                ? Cap(ChatEvidence.Format(online.Chat, now, MaxServerContextForChat), DbReport.MaxServerContextLength)
                : targetUserId is null ? "target offline; no character has that name" : "target offline";

            return Save(dbContext, report, ReportBudget.Report, string.Empty, established);
        }
        catch (Exception exception)
        {
            return Failed(exception, reporter, ReportKind.Player);
        }
    }

    /// <summary>
    /// A report on a house, from the side panel. The guids are the client's echo of what the server sent; the house is
    /// found by its id and the owner is the server's, so what the client says about names changes nothing.
    /// </summary>
    public ReportResult SubmitHouseReport(ReportContext reporter, ulong houseGuid, ulong ownerGuid, string? houseName, string? ownerName)
    {
        if (!GuidHelper.IsHouseGuid(houseGuid))
            return Dropped(ReportOutcome.Ignored, reporter, "not a house guid");

        var houseId = GuidHelper.GetHouseId(houseGuid);

        if (Gate(reporter, ReportBudget.Report) is { } refused)
            return refused;

        try
        {
            var now = _timeProvider.GetUtcNow();

            if (_dbContextFactory is null)
                return NoDatabase();

            using var dbContext = _dbContextFactory.CreateDbContext();

            var house = dbContext.Houses
                .AsNoTracking()
                .Where(h => h.Id == houseId)
                .Select(h => new { h.Id, h.Name, h.CharacterId, h.Character.UserId, h.Character.FullName })
                .FirstOrDefault();

            if (house is null)
                return Dropped(ReportOutcome.Ignored, reporter, "unknown house");

            if (house.UserId == reporter.UserId)
                return Dropped(ReportOutcome.Ignored, reporter, "self-report");

            if (HasOpenReport(dbContext, reporter.UserId, ReportKind.House, null, null, houseId))
                return Dropped(ReportOutcome.Duplicate, reporter, "already open");

            if (Screen(dbContext, reporter, now, ReportKind.House, house.UserId, null, out var established) is { } screened)
                return screened;

            var owner = house.FullName ?? "?";
            var houseLabel = ModArguments.CleanText(house.Name, 64) ?? "(unnamed)";
            var ownerMatches = ownerGuid == GuidHelper.GetPlayerGuid(house.CharacterId);

            var report = NewReport(reporter, ReportKind.House, now);

            report.HouseId = houseId;
            report.TargetName = Cap(owner, DbReport.MaxTargetNameLength);
            report.TargetUserId = house.UserId;
            report.TargetCharacterId = house.CharacterId;
            report.ClientText = Cap(
                $"The client names house \"{ModArguments.CleanText(houseName, 64)}\" of \"{ModArguments.CleanText(ownerName, 64)}\" (house guid {houseGuid}, owner guid {ownerGuid}).",
                DbReport.MaxClientTextLength);
            report.ServerContext = Cap(
                $"House {houseId} \"{houseLabel}\" belongs to {owner} (character {house.CharacterId})." +
                (ownerMatches ? string.Empty : " The client's owner guid does not match the server's."),
                DbReport.MaxServerContextLength);

            return Save(dbContext, report, ReportBudget.Report, string.Empty, established);
        }
        catch (Exception exception)
        {
            return Failed(exception, reporter, ReportKind.House);
        }
    }

    /// <summary>A report from the bug report window. <paramref name="bugXml"/> is the client's document.</summary>
    public ReportResult SubmitBugReport(ReportContext reporter, string? bugXml)
    {
        if (Gate(reporter, ReportBudget.Bug) is { } refused)
            return refused;

        try
        {
            var now = _timeProvider.GetUtcNow();

            if (_dbContextFactory is null)
                return NoDatabase();

            var summary = string.Empty;
            string clientText;

            if (BugReportXml.TryParse(bugXml, out var fields))
            {
                summary = fields.Category;
                clientText = $"Category: {fields.Category}\nSubCategory: {fields.SubCategory}\nSeverity: {fields.Severity}\nDescription: {fields.Description}";
            }
            else
            {
                clientText = "Unreadable bug XML, a cut copy:\n" + BugReportXml.RawCopy(bugXml);
            }

            using var dbContext = _dbContextFactory.CreateDbContext();

            if (Screen(dbContext, reporter, now, ReportKind.Bug, null, null, out var established) is { } screened)
                return screened;

            var report = NewReport(reporter, ReportKind.Bug, now);

            report.ClientText = Cap(clientText, DbReport.MaxClientTextLength);

            return Save(dbContext, report, ReportBudget.Bug, summary, established);
        }
        catch (Exception exception)
        {
            return Failed(exception, reporter, ReportKind.Bug);
        }
    }

    /// <summary>
    /// The cheap checks, before any database work: this account's own attempts and allowance. Nothing shared between
    /// accounts is charged here: that happens only for a report that is kept (see <see cref="Save"/>).
    /// </summary>
    private ReportResult? Gate(ReportContext reporter, ReportBudget budget)
    {
        if (!Limiter.TryBeginAttempt(reporter.UserId))
            return Dropped(ReportOutcome.Limited, reporter, "too many attempts");

        if (Limiter.CheckAccount(reporter.UserId, budget) != ReportRefusal.None)
            return Dropped(ReportOutcome.Limited, reporter, "limit reached");

        return null;
    }

    /// <summary>
    /// The limits that need the database, checked before anything is written: the account must be old enough, the
    /// allowance of all accounts together (a smaller share for accounts that are not yet established) and the ceiling on
    /// open reports of this kind must have room, and one player (or house owner) cannot have more than a few open reports
    /// about them. A target is given as an account or, when there is none, by the name the client typed. A limit shared
    /// between accounts is logged as a warning, once in a while, because it also stops real players' reports.
    /// </summary>
    private ReportResult? Screen(DatabaseContext dbContext, ReportContext reporter, DateTimeOffset now, ReportKind kind, ulong? targetUserId, string? targetName,
        out bool established)
    {
        established = false;

        var created = dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == reporter.UserId)
            .Select(user => (DateTimeOffset?)user.Created)
            .FirstOrDefault();

        if (created is null || now - created.Value < ReportLimits.MinAccountAge)
            return Dropped(ReportOutcome.Limited, reporter, "account too new");

        established = now - created.Value >= ReportLimits.EstablishedAccountAge;

        var isBug = kind == ReportKind.Bug;
        var budget = isBug ? ReportBudget.Bug : ReportBudget.Report;
        var what = isBug ? "bug reports" : "player and house reports";
        var refusal = Limiter.CheckGlobal(budget, established);

        if (refusal != ReportRefusal.None)
        {
            return Refused(reporter, refusal == ReportRefusal.Global
                ? $"the limit on {what} from all accounts together is used up"
                : $"the share of the limit on {what} that accounts younger than {(int)ReportLimits.EstablishedAccountAge.TotalDays} days may use is used up");
        }

        var open = dbContext.Reports.Where(report => report.Status == ReportStatus.Open && (report.Kind == ReportKind.Bug) == isBug);
        var ceiling = isBug ? ReportLimits.MaxOpenBugReports : ReportLimits.MaxOpenReports;
        var openCount = open.Count();

        if (openCount >= ceiling)
            return Refused(reporter, $"there are {openCount} open {what}, the most there can be. Close some with !mod close");

        if (openCount * 100 >= ceiling * ReportLimits.OpenWarningPercent)
            Warn("open-nearly-full-" + (isBug ? "bug" : "report"), $"{openCount} of {ceiling} open {what} are in use. Close some with !mod close (!mod close all {(isBug ? "bug" : "player")})");

        if (targetUserId is { } userId)
        {
            if (open.Count(report => report.TargetUserId == userId) >= ReportLimits.MaxOpenReportsPerTarget)
                return Refused(reporter, "one player already has the most open reports about them there can be (a pile-on?)");
        }
        else if (targetName is not null && open.Count(report => report.TargetUserId == null && report.TargetName == targetName) >= ReportLimits.MaxOpenReportsPerTarget)
        {
            return Refused(reporter, "one name already has the most open reports about it there can be (a pile-on?)");
        }

        return null;
    }

    /// <summary>
    /// Deletes closed reports older than <see cref="ReportLimits.ClosedRetention"/>, so the table does not grow without
    /// end. Open reports are never deleted: staff close them, or <see cref="CloseStale"/> does. Returns how many were deleted; a database failure is logged
    /// and counts as none.
    /// </summary>
    public int PruneClosed()
    {
        if (_dbContextFactory is null)
            return 0;

        try
        {
            var cutoff = _timeProvider.GetUtcNow() - ReportLimits.ClosedRetention;

            using var dbContext = _dbContextFactory.CreateDbContext();

            // The dates are compared here, not in SQL: SQLite can't compare a DateTimeOffset. Only ids and dates are read,
            // and only of closed reports, which this very method keeps to a month's worth.
            var old = dbContext.Reports
                .AsNoTracking()
                .Where(report => report.Status == ReportStatus.Closed)
                .Select(report => new { report.Id, report.ClosedAt })
                .AsEnumerable()
                .Where(report => report.ClosedAt is { } closedAt && closedAt < cutoff)
                .Select(report => report.Id)
                .ToList();

            var deleted = 0;

            foreach (var batch in old.Chunk(200))
            {
                deleted += dbContext.Reports
                    .Where(report => report.Status == ReportStatus.Closed && batch.Contains(report.Id))
                    .ExecuteDelete();
            }

            if (deleted > 0)
                _logger.LogInformation("Deleted {Count} closed report(s) older than {Days} days.", deleted, (int)ReportLimits.ClosedRetention.TotalDays);

            return deleted;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to delete old closed reports.");
            return 0;
        }
    }

    /// <summary>The note on a report closed by <see cref="CloseStale"/>.</summary>
    public const string StaleCloseNote = "Closed automatically: nobody dealt with it in time.";

    /// <summary>
    /// Closes open reports older than <see cref="ReportLimits.OpenRetention"/>, with a note and no staff member, so
    /// reports nobody reads cannot hold up the ceiling on open reports for ever. (They are deleted later like any closed
    /// report.) Returns how many were closed; a database failure is logged and counts as none.
    /// </summary>
    public int CloseStale()
    {
        if (_dbContextFactory is null)
            return 0;

        try
        {
            var now = _timeProvider.GetUtcNow();
            var cutoff = now - ReportLimits.OpenRetention;

            using var dbContext = _dbContextFactory.CreateDbContext();

            // The dates are compared here, not in SQL: SQLite can't compare a DateTimeOffset. The open reports are bounded by
            // their ceilings, and only ids and dates are read.
            var stale = dbContext.Reports
                .AsNoTracking()
                .Where(report => report.Status == ReportStatus.Open)
                .Select(report => new { report.Id, report.Created })
                .AsEnumerable()
                .Where(report => report.Created < cutoff)
                .Select(report => report.Id)
                .ToList();

            var closed = 0;

            foreach (var batch in stale.Chunk(200))
            {
                closed += dbContext.Reports
                    .Where(report => report.Status == ReportStatus.Open && batch.Contains(report.Id))
                    .ExecuteUpdate(report => report
                        .SetProperty(r => r.Status, ReportStatus.Closed)
                        .SetProperty(r => r.ClosedAt, (DateTimeOffset?)now)
                        .SetProperty(r => r.CloseNote, StaleCloseNote));
            }

            if (closed > 0)
                _logger.LogInformation("Closed {Count} open report(s) older than {Days} days.", closed, (int)ReportLimits.OpenRetention.TotalDays);

            return closed;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to close stale open reports.");
            return 0;
        }
    }

    private ReportResult Save(DatabaseContext dbContext, DbReport report, ReportBudget budget, string summary, bool established)
    {
        dbContext.Reports.Add(report);
        dbContext.SaveChanges();

        Limiter.RecordAccepted(report.ReporterUserId, budget, established);

        _auditLogger.LogInformation("report|{Kind}|#{Id}, Actor: \"{ActorName}\" (user {UserId}), Target: \"{Target}\", In: {Zone} {Position}",
            report.Kind, report.Id, report.ReporterName, report.ReporterUserId, report.TargetName, report.Zone, report.Position);

        try
        {
            _notifier.Notify(ReportNotification.From(report, summary));
        }
        catch (Exception exception)
        {
            // The report is saved; a notifier that throws must not undo that.
            _logger.LogError(exception, "The report notifier failed for report {Id}.", report.Id);
        }

        return new ReportResult(ReportOutcome.Accepted, report, summary);
    }

    private static DbReport NewReport(ReportContext reporter, ReportKind kind, DateTimeOffset now) => new()
    {
        Kind = kind,
        Created = now,
        ReporterUserId = reporter.UserId,
        ReporterCharacterId = reporter.CharacterId,
        ReporterName = Cap(reporter.Name, DbReport.MaxReporterNameLength),
        Zone = Cap(reporter.Zone, DbReport.MaxZoneLength),
        Position = Cap(reporter.Position, DbReport.MaxPositionLength),
        Status = ReportStatus.Open
    };

    /// <summary>Whether the reporter already has an open report of this kind about this player or house.</summary>
    private static bool HasOpenReport(DatabaseContext dbContext, ulong reporterUserId, ReportKind kind, ulong? targetUserId, string? targetName, ulong? houseId)
    {
        var open = dbContext.Reports.Where(report => report.ReporterUserId == reporterUserId && report.Kind == kind && report.Status == ReportStatus.Open);

        if (houseId is { } id)
            return open.Any(report => report.HouseId == id);

        return open.Any(report => (targetUserId != null && report.TargetUserId == targetUserId) || report.TargetName == targetName);
    }

    /// <summary>The client's chat lines, one a line, each cleaned and cut. They are the reporter's word, not the server's.</summary>
    internal static string FormatClientLines(IReadOnlyList<(int Channel, string Message, ulong Time)> lines)
    {
        if (lines.Count == 0)
            return "(the client sent no chat lines)";

        var builder = new StringBuilder();

        foreach (var (channel, message, time) in lines.Take(ChatEvidence.MaxLines))
        {
            var text = ModArguments.CleanText(message, ReportLimits.MaxLineLength) ?? string.Empty;

            if (builder.Length > 0)
                builder.Append('\n');

            builder.Append(CultureInfo.InvariantCulture, $"[channel {channel}, time {time}] {text}");
        }

        return Cap(builder.ToString(), DbReport.MaxClientTextLength);
    }

    private static bool IsSameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Cap(string? text, int max) => StaffActionLog.Truncate(text, max);

    /// <summary>
    /// A report refused by a limit that is not about this account alone. It is logged as a warning, at most once per
    /// <see cref="ReportLimits.WarningInterval"/> for each reason, because it stops real players' reports too and staff
    /// should be able to see it in the log.
    /// </summary>
    private ReportResult Refused(ReportContext reporter, string why)
    {
        Warn(why, "Reports are being refused: " + why + ".");
        return Dropped(ReportOutcome.Limited, reporter, why);
    }

    /// <summary>Logs a warning, but for one key no more than once per <see cref="ReportLimits.WarningInterval"/>; the others are counted and reported with the next.</summary>
    private void Warn(string key, string message)
    {
        int suppressed;

        lock (_warnings)
        {
            var now = _timeProvider.GetUtcNow();

            if (_warnings.TryGetValue(key, out var last) && now - last.At < ReportLimits.WarningInterval)
            {
                _warnings[key] = (last.At, last.Suppressed + 1);
                return;
            }

            suppressed = _warnings.TryGetValue(key, out last) ? last.Suppressed : 0;
            _warnings[key] = (now, 0);
        }

        _logger.LogWarning("{Message} ({Suppressed} more since the last warning about this.)", message, suppressed);
    }

    private ReportResult Dropped(ReportOutcome outcome, ReportContext reporter, string why)
    {
        _logger.LogDebug("Dropped a report from user {UserId}: {Why}.", reporter.UserId, why);
        return new ReportResult(outcome);
    }

    private ReportResult NoDatabase()
    {
        _logger.LogWarning("No database to keep reports in; a report was dropped.");
        return new ReportResult(ReportOutcome.Failed);
    }

    private ReportResult Failed(Exception exception, ReportContext reporter, ReportKind kind)
    {
        _logger.LogError(exception, "Failed to save a {Kind} report from user {UserId}.", kind, reporter.UserId);
        return new ReportResult(ReportOutcome.Failed);
    }
}
