using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

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
        "unban|unmute|resetrename <player>. Put | before the reason or text.";

    public string Description => "Moderation: ban, mute, kick or warn a player, see a player's staff history, unban, unmute, or let a player rename again now.";

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

        if (_zoneManager.TryGetPlayer(target.Name, out var targetPlayer))
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

        if (_zoneManager.TryGetPlayer(target.Name, out var targetPlayer))
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

        if (!_zoneManager.TryGetPlayer(target.Name, out var targetPlayer))
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

        if (!_zoneManager.TryGetPlayer(target.Name, out var targetPlayer))
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

        var total = dbContext.StaffActions.Count(action => action.TargetUserId == resolved.UserId);

        // Newest first by id: it grows with time, and SQLite can't sort a DateTimeOffset.
        var recent = dbContext.StaffActions
            .AsNoTracking()
            .Where(action => action.TargetUserId == resolved.UserId)
            .OrderByDescending(action => action.Id)
            .Take(HistoryLimit)
            .ToList();

        ChatHelper.SendSystemMessage(invoker, string.Join('\n', FormatHistory(targetName, total, recent)));
        return true;
    }

    /// <summary>
    /// The lines of <c>!mod history</c>: a heading, then one short line per action. Reports about the player go after
    /// these lines, once the report system exists.
    /// </summary>
    internal static List<string> FormatHistory(string targetName, int total, IReadOnlyList<DbStaffAction> recent)
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

        return lines;
    }

    /// <summary>Lifts the rename cooldown (<see cref="RenameCooldown"/>), e.g. after a typo. Works on yourself too.</summary>
    private bool ResetRename(Player invoker, string[] args)
    {
        if (args.Length < 1)
            return false;

        string targetName = string.Join(' ', args);

        using DatabaseContext dbContext = _dbContextFactory.CreateDbContext();

        if (!IsSelfTarget(invoker, targetName) && !TryResolveTarget(invoker, dbContext, targetName, false, out _))
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

        LogAction(invoker, "ResetRename", targetName, null);

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

        if (_zoneManager.TryGetPlayer(targetName, out var targetPlayer))
        {
            targetPlayer.MutedUntil = null;
        }

        LogAction(invoker, "Unmute", targetName, resolved);

        ChatHelper.SendSystemMessage(invoker, $"{targetName} has been unmuted.");
        return true;
    }
}
