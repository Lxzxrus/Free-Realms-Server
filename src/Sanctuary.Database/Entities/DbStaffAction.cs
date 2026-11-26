using System;

namespace Sanctuary.Database.Entities;

/// <summary>
/// One thing staff did with a chat command: a ban, a mute, a kick, a warning, a promotion and so on. Rows are history,
/// so none of the ids is a foreign key: the row stays when an account or character is deleted. The shape follows
/// Sulphural's <c>DbStaffAction</c> (Sanctuary-fork d915c50, AGPL-3.0).
/// </summary>
public class DbStaffAction
{
    public const int MaxStaffNameLength = 64;
    public const int MaxCommandLength = 32;
    public const int MaxActionLength = 32;
    public const int MaxTargetNameLength = 64;
    public const int MaxDetailsLength = 1000;

    public ulong Id { get; set; }

    /// <summary>When it happened, in UTC. Set by the code that writes the row.</summary>
    public DateTimeOffset Created { get; set; }

    public ulong StaffUserId { get; set; }

    /// <summary>The full name of the staff member's character at the time.</summary>
    public required string StaffName { get; set; }

    /// <summary>The chat command's keyword, for example <c>mod</c>.</summary>
    public required string Command { get; set; }

    /// <summary>What was done, for example <c>Ban</c>.</summary>
    public required string Action { get; set; }

    /// <summary>The account acted on, when the action was on a player.</summary>
    public ulong? TargetUserId { get; set; }
    public ulong? TargetCharacterId { get; set; }
    public string? TargetName { get; set; }

    public string? Details { get; set; }
}
