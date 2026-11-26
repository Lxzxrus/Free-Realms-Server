using System;

namespace Sanctuary.Database.Entities;

/// <summary>What a report is about.</summary>
public enum ReportKind : byte
{
    /// <summary>A player, from the chat log's "Report" or /report.</summary>
    Player = 1,

    /// <summary>A house, from the house side panel's "Report Offensive".</summary>
    House = 2,

    /// <summary>The bug report window.</summary>
    Bug = 3
}

public enum ReportStatus : byte
{
    Open = 0,
    Closed = 1
}

/// <summary>
/// One in-game report: a player, a house or a bug. Rows are history, so none of the ids is a foreign key: the row stays
/// when an account or character is deleted. Everything the client sent is untrusted and capped; the server's own view
/// (<see cref="ServerContext"/>) is what staff compare it with. The shape follows the report tables of Sulphural's fork
/// (Sanctuary-fork d915c50, AGPL-3.0); the packets are BubbaJoeX's (Sanctuary 982a800, AGPL-3.0).
/// </summary>
public class DbReport
{
    public const int MaxReporterNameLength = 64;
    public const int MaxTargetNameLength = 64;
    public const int MaxZoneLength = 64;
    public const int MaxPositionLength = 64;
    public const int MaxClientTextLength = 4000;
    public const int MaxServerContextLength = 4000;
    public const int MaxCloseNoteLength = 200;

    public ulong Id { get; set; }

    public ReportKind Kind { get; set; }

    /// <summary>When it was reported, in UTC. Set by the code that writes the row.</summary>
    public DateTimeOffset Created { get; set; }

    public ulong ReporterUserId { get; set; }
    public ulong ReporterCharacterId { get; set; }
    public required string ReporterName { get; set; }

    /// <summary>The player (or, for a house, its owner) as the server knows them, or what the client typed if unknown.</summary>
    public string? TargetName { get; set; }
    public ulong? TargetCharacterId { get; set; }
    public ulong? TargetUserId { get; set; }

    /// <summary>The reported house's id, for <see cref="ReportKind.House"/>.</summary>
    public ulong? HouseId { get; set; }

    /// <summary>Where the reporter was.</summary>
    public string Zone { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;

    /// <summary>What the reporter's client said: its chat lines, the house names, or the bug's fields. Not to be trusted.</summary>
    public string ClientText { get; set; } = string.Empty;

    /// <summary>What the server knows: the target's last chat lines as the server relayed them, or "target offline".</summary>
    public string ServerContext { get; set; } = string.Empty;

    public ReportStatus Status { get; set; }

    public ulong? ClosedByUserId { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? CloseNote { get; set; }
}
