using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Reports;

namespace Sanctuary.Game.ChatCommands;

public class ModChatCommand : IChatCommand
{
    /// <summary>How many staff actions <c>!mod history</c> lists.</summary>
    public const int HistoryLimit = 10;

    /// <summary>The longest a history line gets, so the whole reply stays small however much staff wrote.</summary>
    private const int HistoryDetailsLength = 70;

    private readonly IChatCommandManager _chatCommandManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;
    private readonly IZoneManager _zoneManager;

    public string KeyWord => "mod";

    // "|" starts the reason: a player's name can have a space, so a space can't. Names are told apart from minutes by
    // the number at the end.
    public string Usage =>
        "ban|mute <player> [minutes] [| reason], kick <player> [| reason], warn <player> | <text>, history <player>, " +
        "unban|unmute|resetrename <player>, reports [open|all] [bug|player|house] [page], report <id>, close <id> [| note], " +
        "close all bug|player|house [| note]. Put | before the reason, text or note.";

    public string Description => "Moderation: ban, mute, kick or warn a player, read and close player reports, see a player's staff and report history, unban, unmute, or let a player rename again now.";

    public ChatCommandRole RequiredRole => ChatCommandRole.Mod;

    /// <summary>The player a subcommand acts on, found by character name.</summary>
    private readonly record struct ResolvedTarget(ulong UserId, ulong CharacterId);

    public ModChatCommand(IChatCommandManager chatCommandManager, IDbContextFactory<DatabaseContext> dbContextFactory, IZoneManager zoneManager)
    {
        _chatCommandManager = chatCommandManager;
        _dbContextFactory = dbContextFactory;
        _zoneManager = zoneManager;
    }

    private void LogAction(Player invoker, string action, string targetName, ResolvedTarget? target, string? detail = null)
    {
        _chatCommandManager.LogAction(this, invoker, action, targetName, detail, target?.UserId, target?.CharacterId);
    }

    /// <summary>
    /// The player online as this character, if any. Found by the character's guid, never by the name that was typed: the
    /// database matches names without regard to case (on MariaDB), and the list of online players does not, so a name
    /// typed in another case finds the character but not the player.
    /// </summary>
    private bool TryGetOnline(ResolvedTarget resolved, [MaybeNullWhen(false)] out Player player) =>
        _zoneManager.TryGetPlayer(GuidHelper.GetPlayerGuid(resolved.CharacterId), out player);

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length < 1)
            return false;

        var subCommand = args[0].ToLower();

        args = args[1..];

        switch (subCommand)
        {
            case "ban":
                return Ban(invoker, args);
            case "mute":
                return Mute(invoker, args);
            case "kick":
                return Kick(invoker, args);
            case "warn":
                return Warn(invoker, args);
            case "history":
                return History(invoker, args);
            case "reports":
                return ListReports(invoker, args);
            case "report":
                return ShowReport(invoker, args);
            case "close":
                return CloseReport(invoker, args);
            case "unban":
                return Unban(invoker, args);
            case "unmute":
                return Unmute(invoker, args);
            case "resetrename":
                return ResetRename(invoker, args);
            default:
                return false;
        }
    }

    private static bool IsSelfTarget(Player invoker, string targetName)
    {
        return string.Equals(invoker.Name.FullName, targetName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The rank rule: staff act only on players ranked below them.</summary>
    internal static bool IsAuthorizedAgainstTarget(ChatCommandRole playerRole, ChatCommandRole targetRole)
    {
        return playerRole > targetRole;
    }

    /// <summary>
    /// Whether the invoker may close, or read, a report about <paramref name="targetUserId"/>. The rank rule applies as it
    /// does to every other action on a player: a moderator can't close reports about moderators or admins, or about themselves,
    /// or a report about staff would vanish from the open list that the others read. An admin has nobody above them, so
    /// an admin may close reports about other staff, but never about themselves. A report about no known account, or
    /// one that has been deleted (<paramref name="targetRole"/> null), is open to any staff member.
    /// </summary>
    internal static bool MayCloseReportAbout(ulong invokerUserId, ChatCommandRole invokerRole, ulong? targetUserId, ChatCommandRole? targetRole)
    {
        if (targetUserId is null)
            return true;

        if (targetUserId == invokerUserId)
            return false;

        if (targetRole is null || invokerRole == ChatCommandRole.Admin)
            return true;

        return IsAuthorizedAgainstTarget(invokerRole, targetRole.Value);
    }

    /// <summary>
    /// Finds the character, and refuses unless the invoker outranks its account. With <paramref name="allowSelf"/> the
    /// invoker's own account is let through, for commands that only read.
    /// </summary>
    private static bool TryResolveTarget(Player invoker, DatabaseContext dbContext, string targetName, bool allowSelf, out ResolvedTarget resolved)
    {
        resolved = default;

        var target = dbContext.Characters
            .Where(character => character.FullName == targetName)
            .Select(character => new { character.Id, character.UserId, character.User.IsAdmin, character.User.IsMod })
            .SingleOrDefault();

        if (target is null)
        {
            ChatHelper.SendSystemMessage(invoker, $"No player named \"{targetName}\" was found.");
            return false;
        }

        var isSelf = target.UserId == invoker.UserId;

        if (!(allowSelf && isSelf))
        {
            ChatCommandRole targetRole = ChatHelper.GetRoleFromFlags(target.IsAdmin, target.IsMod);

            if (!IsAuthorizedAgainstTarget(invoker.ChatCommandRole, targetRole))
            {
                ChatHelper.SendSystemMessage(invoker, "You don't have permission to target this player.");
                return false;
            }
        }

        resolved = new ResolvedTarget(target.UserId, target.Id);
        return true;
    }

    /// <summary>
    /// Whether the text names a player whose name has the separator in it, so it could be the reason of a shorter
    /// name. Names are not limited to letters yet, and acting on the wrong player is worse than a refusal.
    /// </summary>
    private static bool IsAmbiguousWithSeparator(Player invoker, DatabaseContext dbContext, ModTarget target)
    {
        if (!target.HasSeparator)
            return false;

        var separatorNames = dbContext.Characters
            .Where(character => character.FullName!.Contains(ModArguments.ReasonSeparator.ToString()))
            .Select(character => character.FullName!)
            .Take(100)
            .ToList();

        if (!separatorNames.Any(name => target.Text.StartsWith(name, StringComparison.OrdinalIgnoreCase)))
            return false;

        ChatHelper.SendSystemMessage(invoker, $"That could be a player whose name has a \"{ModArguments.ReasonSeparator}\" in it. Use the command without a reason.");
        return true;
    }

    /// <summary>Reads the arguments, tells the invoker what is wrong with them, and says whether to go on.</summary>
    private static bool TryReadTarget(Player invoker, string[] args, bool allowMinutes, int maxTextLength, out ModTarget target, out bool handled)
    {
        handled = false;

        if (ModArguments.TryParse(args, allowMinutes, maxTextLength, out target, out var error))
            return true;

        if (error is not null)
        {
            ChatHelper.SendSystemMessage(invoker, error);
            handled = true;
        }

        return false;
    }

    private static string Describe(string what, string? reason)
    {
        return reason is null ? what : $"{what}; Reason: {reason}";
    }

    private bool Ban(Player invoker, string[] args)
    {
        if (!TryReadTarget(invoker, args, true, ModArguments.MaxReasonLength, out var target, out var handled))
            return handled;

        if (IsSelfTarget(invoker, target.Name))
        {
            ChatHelper.SendSystemMessage(invoker, "You cannot ban yourself.");
            return true;
        }

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        if (IsAmbiguousWithSeparator(invoker, dbContext, target))
            return true;

        if (!TryResolveTarget(invoker, dbContext, target.Name, false, out var resolved))
            return true;

        if (resolved.UserId == invoker.UserId)
        {
            ChatHelper.SendSystemMessage(invoker, "You cannot ban yourself.");
            return true;
        }

        DateTimeOffset? banUntilTime = target.Minutes is { } minutes ? DateTimeOffset.UtcNow.AddMinutes(minutes) : null;
        DateTimeOffset lockedUntil = banUntilTime ?? DateTimeOffset.MaxValue;
        string? reason = target.Reason;

        dbContext.Users
            .Where(user => user.Id == resolved.UserId)
            .ExecuteUpdate(user => user
                .SetProperty(u => u.LockedUntil, lockedUntil)
                .SetProperty(u => u.BanReason, reason));

        if (TryGetOnline(resolved, out var targetPlayer))
            targetPlayer.Disconnect();

        LogAction(invoker, "Ban", target.Name, resolved, Describe(banUntilTime is null ? "Permanent" : $"Until: {banUntilTime:u}", reason));

        var message = banUntilTime is null
            ? $"{target.Name} has been banned permanently."
            : $"{target.Name} has been banned until {banUntilTime:u}.";

        ChatHelper.SendSystemMessage(invoker, reason is null ? message : $"{message} Reason: {reason}");
        return true;
    }

    private bool Unban(Player invoker, string[] args)
    {
        if (args.Length < 1)
            return false;

        string targetName = string.Join(' ', args);

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        if (!TryResolveTarget(invoker, dbContext, targetName, false, out var resolved))
            return true;

        dbContext.Users
            .Where(user => user.Id == resolved.UserId)
            .ExecuteUpdate(user => user
                .SetProperty(u => u.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(u => u.BanReason, (string?)null));

        LogAction(invoker, "Unban", targetName, resolved);

        ChatHelper.SendSystemMessage(invoker, $"{targetName} has been unbanned.");
        return true;
    }

    private bool Mute(Player invoker, string[] args)
    {
        if (!TryReadTarget(invoker, args, true, ModArguments.MaxReasonLength, out var target, out var handled))
            return handled;

        if (target.Minutes is not { } minutes)
        {
            ChatHelper.SendSystemMessage(invoker, "Please specify a duration in minutes for mute.");
            return true;
        }

        if (IsSelfTarget(invoker, target.Name))
        {
            ChatHelper.SendSystemMessage(invoker, "You cannot mute yourself.");
            return true;
        }

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        if (IsAmbiguousWithSeparator(invoker, dbContext, target))
            return true;

        if (!TryResolveTarget(invoker, dbContext, target.Name, false, out var resolved))
            return true;

        if (resolved.UserId == invoker.UserId)
        {
            ChatHelper.SendSystemMessage(invoker, "You cannot mute yourself.");
            return true;
        }

        DateTimeOffset muteUntilTime = DateTimeOffset.UtcNow.AddMinutes(minutes);
        string? reason = target.Reason;

        dbContext.Users
            .Where(user => user.Id == resolved.UserId)
            .ExecuteUpdate(user => user
                .SetProperty(u => u.MutedUntil, (DateTimeOffset?)muteUntilTime)
                .SetProperty(u => u.MuteReason, reason));

        if (TryGetOnline(resolved, out var targetPlayer))
        {
            targetPlayer.MutedUntil = muteUntilTime;
        }

        LogAction(invoker, "Mute", target.Name, resolved, Describe($"Until: {muteUntilTime:u}", reason));

        var message = $"{target.Name} has been muted until {muteUntilTime:u}.";

        ChatHelper.SendSystemMessage(invoker, reason is null ? message : $"{message} Reason: {reason}");
        return true;
    }

    /// <summary>Disconnects a player who is online, without banning them.</summary>
    private bool Kick(Player invoker, string[] args)
    {
        if (!TryReadTarget(invoker, args, false, ModArguments.MaxReasonLength, out var target, out var handled))
            return handled;

        if (IsSelfTarget(invoker, target.Name))
        {
            ChatHelper.SendSystemMessage(invoker, "You cannot kick yourself.");
            return true;
        }

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        if (IsAmbiguousWithSeparator(invoker, dbContext, target))
            return true;

        if (!TryResolveTarget(invoker, dbContext, target.Name, false, out var resolved))
            return true;

        if (resolved.UserId == invoker.UserId)
        {
            ChatHelper.SendSystemMessage(invoker, "You cannot kick yourself.");
            return true;
        }

        if (!TryGetOnline(resolved, out var targetPlayer))
        {
            ChatHelper.SendSystemMessage(invoker, $"{target.Name} is not online.");
            return true;
        }

        targetPlayer.Disconnect();

        LogAction(invoker, "Kick", target.Name, resolved, target.Reason is null ? null : $"Reason: {target.Reason}");

        ChatHelper.SendSystemMessage(invoker, target.Reason is null
            ? $"{target.Name} has been kicked."
            : $"{target.Name} has been kicked. Reason: {target.Reason}");
        return true;
    }

    /// <summary>Sends a player who is online a message from staff, and logs it.</summary>
    private bool Warn(Player invoker, string[] args)
    {
        if (!TryReadTarget(invoker, args, false, ModArguments.MaxWarnLength, out var target, out var handled))
            return handled;

        if (target.Reason is not { } text)
        {
            ChatHelper.SendSystemMessage(invoker, "Please write the warning after a | .");
            return true;
        }

        if (IsSelfTarget(invoker, target.Name))
        {
            ChatHelper.SendSystemMessage(invoker, "You cannot warn yourself.");
            return true;
        }

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        if (IsAmbiguousWithSeparator(invoker, dbContext, target))
            return true;

        if (!TryResolveTarget(invoker, dbContext, target.Name, false, out var resolved))
            return true;

        if (resolved.UserId == invoker.UserId)
        {
            ChatHelper.SendSystemMessage(invoker, "You cannot warn yourself.");
            return true;
        }

        if (!TryGetOnline(resolved, out var targetPlayer))
        {
            ChatHelper.SendSystemMessage(invoker, $"{target.Name} is not online.");
            return true;
        }

        ChatHelper.SendSystemMessage(targetPlayer, $"Warning from staff: {text}");

        LogAction(invoker, "Warn", target.Name, resolved, $"Text: {text}");

        ChatHelper.SendSystemMessage(invoker, $"{target.Name} has been warned.");
        return true;
    }

    /// <summary>Lists the last staff actions on a player's account, newest first.</summary>
    private bool History(Player invoker, string[] args)
    {
        if (args.Length < 1)
            return false;

        string targetName = string.Join(' ', args);

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        if (!TryResolveTarget(invoker, dbContext, targetName, true, out var resolved))
            return true;

        var (total, recent) = LoadHistory(dbContext, resolved.UserId);

        // The reports about a staff member's own account are not shown to them (see ReportQueries.VisibleTo). Anyone else
        // here already outranks the player, or the target would not have been resolved.
        if (resolved.UserId == invoker.UserId)
        {
            ChatHelper.SendSystemMessage(invoker, string.Join('\n', FormatHistory(targetName, total, recent)));
            return true;
        }

        var (reportTotal, recentReports) = ReportQueries.LoadAbout(dbContext, resolved.UserId);

        ChatHelper.SendSystemMessage(invoker, string.Join('\n', FormatHistory(targetName, total, recent, reportTotal, recentReports)));
        return true;
    }

    /// <summary>
    /// The words of <c>!mod reports</c>: <c>open</c> (the default) or <c>all</c>, a kind (<c>bug</c>, <c>player</c>,
    /// <c>house</c>) and a page number, in any order, each at most once.
    /// </summary>
    internal static bool TryParseListArguments(string[] args, out bool openOnly, out ReportKind? kind, out int page)
    {
        openOnly = true;
        kind = null;
        page = 1;

        bool sawStatus = false, sawPage = false;

        foreach (var word in args)
        {
            if (word.Equals("all", StringComparison.OrdinalIgnoreCase) || word.Equals("open", StringComparison.OrdinalIgnoreCase))
            {
                if (sawStatus)
                    return false;

                sawStatus = true;
                openOnly = word.Equals("open", StringComparison.OrdinalIgnoreCase);
            }
            else if (ReportQueries.ParseKind(word) is { } parsedKind)
            {
                if (kind is not null)
                    return false;

                kind = parsedKind;
            }
            else if (int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 100000)
            {
                if (sawPage)
                    return false;

                sawPage = true;
                page = number;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The words of <c>!mod close</c>: a report id or <c>all</c> and a kind, then an optional note after a | . The text is
    /// split at the first | wherever it is (<c>close 12|spam</c> works), as the other subcommands do, and without a | the
    /// words after the id are the note (<c>close 12 spam</c>).
    /// </summary>
    internal static bool TryParseClose(string[] args, out ulong? id, out ReportKind? allOfKind, out string? note)
    {
        id = null;
        allOfKind = null;
        note = null;

        var text = string.Join(' ', args);
        var bar = text.IndexOf(ModArguments.ReasonSeparator);
        var head = bar < 0 ? text : text[..bar];
        var words = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
            return false;

        var used = 1;

        if (words[0].Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (words.Length < 2 || ReportQueries.ParseKind(words[1]) is not { } kind)
                return false;

            allOfKind = kind;
            used = 2;
        }
        else if (ulong.TryParse(words[0], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            id = parsed;
        }
        else
        {
            return false;
        }

        if (bar >= 0)
        {
            // Nothing may stand between the id and the | but the | itself.
            if (words.Length > used)
                return false;

            note = text[(bar + 1)..].Trim();
        }
        else if (words.Length > used)
        {
            note = string.Join(' ', words[used..]);
        }

        if (note is { Length: 0 })
            note = null;

        return true;
    }

    private static ReportQueries.Viewer ViewerOf(Player invoker) => new(invoker.UserId, invoker.ChatCommandRole);

    /// <summary>
    /// Lists reports, newest first, a page at a time: <c>reports</c> or <c>reports open</c> for the open ones,
    /// <c>reports all</c>, and a kind and page number after them. Reports about the invoker, or about staff they do not
    /// outrank, are left out, and the reading is written to the staff log.
    /// </summary>
    private bool ListReports(Player invoker, string[] args)
    {
        if (!TryParseListArguments(args, out var openOnly, out var kind, out var page))
            return false;

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        var viewer = ViewerOf(invoker);
        var total = ReportQueries.Count(dbContext, openOnly, kind, viewer);
        var pages = Math.Max(1, (total + ReportQueries.ListLimit - 1) / ReportQueries.ListLimit);
        var reports = ReportQueries.List(dbContext, openOnly, kind, page, viewer);

        var what = $"{(openOnly ? "open " : string.Empty)}{(kind is { } k ? ReportQueries.KindName(k) + " " : string.Empty)}reports";

        var lines = new List<string>(reports.Count + 1);

        if (total == 0)
            lines.Add($"No {what}.");
        else if (reports.Count == 0)
            lines.Add($"No {what} on page {page}: there {(pages == 1 ? "is 1 page" : $"are {pages} pages")}.");
        else
            lines.Add($"{char.ToUpperInvariant(what[0])}{what[1..]}, newest first, page {page} of {pages} ({total} in all). !mod report <id> shows one{(page < pages ? ", add " + (page + 1) + " for the next page" : string.Empty)}:");

        lines.AddRange(reports.Select(ReportQueries.FormatListLine));

        LogAction(invoker, "ListReports", "reports", null, $"{what}, page {page}");

        ChatHelper.SendSystemMessage(invoker, string.Join('\n', lines));
        return true;
    }

    /// <summary>
    /// Shows one report, a few lines at a time so no single chat message is large. A report about the invoker, or about
    /// staff they do not outrank, is not shown, and every report shown is written to the staff log under its target.
    /// </summary>
    private bool ShowReport(Player invoker, string[] args)
    {
        if (args.Length != 1 || !ulong.TryParse(args[0], out var id))
            return false;

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        var report = ReportQueries.Get(dbContext, id);

        // The same answer for a report that does not exist and one that may not be read, so ids can't be probed.
        if (report is null || !MayCloseReportAbout(invoker.UserId, invoker.ChatCommandRole, report.TargetUserId, ReportQueries.GetUserRole(dbContext, report.TargetUserId)))
        {
            ChatHelper.SendSystemMessage(invoker, $"No report #{id} that you may read.");
            return true;
        }

        LogAction(invoker, "ReadReport", report.TargetName ?? report.ReporterName,
            report.TargetUserId is { } userId ? new ResolvedTarget(userId, report.TargetCharacterId ?? 0) : null,
            $"Report #{id}");

        foreach (var chunk in ReportQueries.FormatDetails(report).Chunk(6))
            ChatHelper.SendSystemMessage(invoker, string.Join('\n', chunk));

        return true;
    }

    /// <summary>
    /// Closes an open report, with an optional note after a | ; or, with <c>all</c> and a kind, every open report of that
    /// kind except those about staff, which are closed one at a time (the rank rule applies to each).
    /// </summary>
    private bool CloseReport(Player invoker, string[] args)
    {
        if (!TryParseClose(args, out var parsedId, out var allOfKind, out var rawNote))
            return false;

        var note = ModArguments.CleanText(rawNote, DbReport.MaxCloseNoteLength);

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        if (allOfKind is { } kind)
        {
            var closedCount = ReportQueries.CloseAll(dbContext, kind, invoker.UserId, note, DateTimeOffset.UtcNow);

            LogAction(invoker, "CloseReports", $"all {ReportQueries.KindName(kind)}", null,
                note is null ? $"{closedCount} report(s)" : $"{closedCount} report(s): {note}");

            ChatHelper.SendSystemMessage(invoker, closedCount == 0
                ? $"There are no open {ReportQueries.KindName(kind)} reports to close (those about staff are closed one at a time)."
                : $"Closed {closedCount} open {ReportQueries.KindName(kind)} report(s). Those about staff, if any, are closed one at a time.");
            return true;
        }

        var id = parsedId!.Value;
        var report = ReportQueries.Get(dbContext, id);

        if (report is null)
        {
            ChatHelper.SendSystemMessage(invoker, $"No report #{id}.");
            return true;
        }

        if (!MayCloseReportAbout(invoker.UserId, invoker.ChatCommandRole, report.TargetUserId, ReportQueries.GetUserRole(dbContext, report.TargetUserId)))
        {
            ChatHelper.SendSystemMessage(invoker, "You don't have permission to close a report about this player. Ask someone who outranks them.");
            return true;
        }

        if (!ReportQueries.TryClose(dbContext, id, invoker.UserId, note, DateTimeOffset.UtcNow))
        {
            ChatHelper.SendSystemMessage(invoker, $"Report #{id} is already closed.");
            return true;
        }

        LogAction(invoker, "CloseReport", report.TargetName ?? report.ReporterName,
            report.TargetUserId is { } userId ? new ResolvedTarget(userId, report.TargetCharacterId ?? 0) : null,
            note is null ? $"Report #{id}" : $"Report #{id}: {note}");

        ChatHelper.SendSystemMessage(invoker, $"Report #{id} is closed.");
        return true;
    }

    /// <summary>How many staff actions an account has, and the last <see cref="HistoryLimit"/> of them, newest first.</summary>
    internal static (int Total, List<DbStaffAction> Recent) LoadHistory(DatabaseContext dbContext, ulong userId)
    {
        var total = dbContext.StaffActions.Count(action => action.TargetUserId == userId);

        // Newest first by id: it grows with time, and SQLite can't sort a DateTimeOffset or a ulong (hence the cast).
        var recent = dbContext.StaffActions
            .AsNoTracking()
            .Where(action => action.TargetUserId == userId)
            .OrderByDescending(action => (long)action.Id)
            .Take(HistoryLimit)
            .ToList();

        return (total, recent);
    }

    /// <summary>
    /// The lines of <c>!mod history</c>: a heading, then one short line per action, then how many reports are about the
    /// player and the last few.
    /// </summary>
    internal static List<string> FormatHistory(string targetName, int total, IReadOnlyList<DbStaffAction> recent,
        int reportTotal = 0, IReadOnlyList<DbReport>? recentReports = null)
    {
        var lines = new List<string>(recent.Count + 1)
        {
            total <= recent.Count
                ? $"{targetName}: {total} staff action(s)."
                : $"{targetName}: {total} staff actions, the last {recent.Count}:"
        };

        foreach (var action in recent)
        {
            var staff = ModArguments.CleanText(action.StaffName, DbStaffAction.MaxStaffNameLength) ?? "?";
            var details = ModArguments.CleanText(action.Details, HistoryDetailsLength);
            var line = $"{action.Created.UtcDateTime:yyyy-MM-dd HH:mm} {action.Action} by {staff}";

            lines.Add(details is null ? line : $"{line}: {details}");
        }

        // With no list given the caller has no report data to show (the staff actions alone).
        if (recentReports is not null)
            lines.AddRange(ReportQueries.FormatAboutLines(reportTotal, recentReports));

        return lines;
    }

    /// <summary>Lifts the rename cooldown (<see cref="RenameCooldown"/>), e.g. after a typo. Works on yourself too.</summary>
    private bool ResetRename(Player invoker, string[] args)
    {
        if (args.Length < 1)
            return false;

        string targetName = string.Join(' ', args);

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        // The resolved target goes in the staff log, or "!mod history" would never list this action. For the invoker's own
        // character there is nothing to resolve: the account is theirs.
        ResolvedTarget resolved;

        if (IsSelfTarget(invoker, targetName))
            resolved = new ResolvedTarget(invoker.UserId, invoker.CharacterId);
        else if (!TryResolveTarget(invoker, dbContext, targetName, false, out resolved))
            return true;

        var updated = dbContext.Characters
            .Where(character => character.FullName == targetName)
            .ExecuteUpdate(character => character
                .SetProperty(c => c.LastRenamed, (DateTimeOffset?)null));

        if (updated == 0)
        {
            ChatHelper.SendSystemMessage(invoker, $"No player named \"{targetName}\" was found.");
            return true;
        }

        LogAction(invoker, "ResetRename", targetName, resolved);

        ChatHelper.SendSystemMessage(invoker, $"{targetName} can rename now.");
        return true;
    }

    private bool Unmute(Player invoker, string[] args)
    {
        if (args.Length < 1)
            return false;

        string targetName = string.Join(' ', args);

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        if (!TryResolveTarget(invoker, dbContext, targetName, false, out var resolved))
            return true;

        dbContext.Users
            .Where(user => user.Id == resolved.UserId)
            .ExecuteUpdate(user => user
                .SetProperty(u => u.MutedUntil, (DateTimeOffset?)null)
                .SetProperty(u => u.MuteReason, (string?)null));

        if (TryGetOnline(resolved, out var targetPlayer))
        {
            targetPlayer.MutedUntil = null;
        }

        LogAction(invoker, "Unmute", targetName, resolved);

        ChatHelper.SendSystemMessage(invoker, $"{targetName} has been unmuted.");
        return true;
    }
}
