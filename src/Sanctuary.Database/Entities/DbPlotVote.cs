using System;

namespace Sanctuary.Database.Entities;

/// <summary>
/// A vote in the weekly competition (<c>docs/design/evergrove-housing.md</c>, section 3): one per account per ranked
/// plot per week. Changing the vote during the week updates this row; rows from past weeks are kept for history and
/// moderation, even after the house is gone, so <see cref="HouseId"/> has no foreign key.
/// </summary>
public class DbPlotVote
{
    public ulong HouseId { get; set; }

    /// <summary>The account that owned the plot when the vote was cast.</summary>
    public ulong OwnerUserId { get; set; }

    /// <summary>The voter's account.</summary>
    public ulong UserId { get; set; }
    public DbUser User { get; set; } = null!;

    /// <summary>When the competition week the vote counts in started, in UTC.</summary>
    public DateTimeOffset WeekStart { get; set; }

    /// <summary>The character that cast the vote, or last changed it.</summary>
    public ulong CharacterId { get; set; }

    /// <summary>1 to 5 stars.</summary>
    public int Value { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
}
