using System;
using System.Collections.Generic;

namespace Sanctuary.Database.Entities;

public sealed class DbUser
{
    public const int MaxReasonLength = 200;

    public ulong Id { get; set; }

    public required string Username { get; set; }
    public required string Password { get; set; }

    public string? Session { get; set; }
    public DateTimeOffset? SessionCreated { get; set; }

    public int MaxCharacters { get; set; }

    public bool IsMember { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsMod { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? MutedUntil { get; set; }

    /// <summary>Why the account was banned (<c>!mod ban</c>), up to <see cref="MaxReasonLength"/> characters. Cleared by <c>!mod unban</c>.</summary>
    public string? BanReason { get; set; }

    /// <summary>Why the account was muted (<c>!mod mute</c>), up to <see cref="MaxReasonLength"/> characters. Cleared by <c>!mod unmute</c>.</summary>
    public string? MuteReason { get; set; }

    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? LastLogin { get; set; }

    public ICollection<DbCharacter> Characters { get; set; } = [];
}