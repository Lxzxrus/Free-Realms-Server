using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;

namespace Sanctuary.Game;

/// <summary>What staff did, as <see cref="StaffActionLog"/> writes it. Text longer than its column is cut to fit.</summary>
public sealed record StaffActionEntry(
    ulong StaffUserId,
    string StaffName,
    string Command,
    string Action,
    ulong? TargetUserId = null,
    ulong? TargetCharacterId = null,
    string? TargetName = null,
    string? Details = null);

/// <summary>
/// Writes <see cref="DbStaffAction"/> rows. A staff command has already done its work by the time it is logged, so a
/// failed write is logged to the file log and swallowed: it must never break the command or take the zone down.
/// </summary>
public sealed class StaffActionLog
{
    private readonly IDbContextFactory<DatabaseContext>? _dbContextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public StaffActionLog(IDbContextFactory<DatabaseContext>? dbContextFactory, TimeProvider timeProvider, ILogger logger)
    {
        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Writes the row. Returns false, after logging why, if it could not.</summary>
    public bool TryWrite(StaffActionEntry entry)
    {
        if (_dbContextFactory is null)
        {
            _logger.LogWarning("No database to write the staff action log to. ( {Command}|{Action} )", entry.Command, entry.Action);
            return false;
        }

        try
        {
            using var dbContext = _dbContextFactory.CreateDbContext();

            dbContext.StaffActions.Add(ToRow(entry, _timeProvider.GetUtcNow()));
            dbContext.SaveChanges();

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to write the staff action log. ( {Command}|{Action}, staff user {StaffUserId} )",
                entry.Command, entry.Action, entry.StaffUserId);

            return false;
        }
    }

    internal static DbStaffAction ToRow(StaffActionEntry entry, DateTimeOffset now) => new()
    {
        Created = now,
        StaffUserId = entry.StaffUserId,
        StaffName = Truncate(entry.StaffName, DbStaffAction.MaxStaffNameLength),
        Command = Truncate(entry.Command, DbStaffAction.MaxCommandLength),
        Action = Truncate(entry.Action, DbStaffAction.MaxActionLength),
        TargetUserId = entry.TargetUserId,
        TargetCharacterId = entry.TargetCharacterId,
        TargetName = entry.TargetName is null ? null : Truncate(entry.TargetName, DbStaffAction.MaxTargetNameLength),
        Details = entry.Details is null ? null : Truncate(entry.Details, DbStaffAction.MaxDetailsLength)
    };

    /// <summary>
    /// Text that a player chose (a name they typed), made safe to put between quotes in an audit file line. A double quote
    /// or backslash could close the quotes and forge fields of the line, so each becomes an apostrophe or slash; a line
    /// break could start what looks like a separate entry, and a right-to-left override could reorder the text beside it,
    /// so control and invisible formatting characters are dropped and whitespace becomes single spaces
    /// (<see cref="ChatCommands.ModArguments.CleanText"/>).
    /// </summary>
    internal static string ForAuditLine(string? text) =>
        (ChatCommands.ModArguments.CleanText(text, int.MaxValue) ?? string.Empty).Replace('"', '\'').Replace('\\', '/');

    internal static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        if (text.Length <= maxLength)
            return text;

        // Do not leave half of a surrogate pair at the end.
        if (char.IsHighSurrogate(text[maxLength - 1]))
            maxLength--;

        return text[..maxLength];
    }
}
