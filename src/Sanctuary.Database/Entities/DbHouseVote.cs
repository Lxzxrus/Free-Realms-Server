using System;

namespace Sanctuary.Database.Entities;

/// <summary>
/// A vote from before weekly voting: one per character per house, for good. Nothing writes these any more; they're kept
/// as history. Votes now are <see cref="DbPlotVote"/>.
/// </summary>
public class DbHouseVote
{
    public ulong HouseId { get; set; }
    public DbHouse House { get; set; } = null!;

    public ulong CharacterId { get; set; }
    public int Value { get; set; }

    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
