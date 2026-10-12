using System;

namespace Sanctuary.Database.Entities;

/// <summary>
/// A collection entry a character picked up directly (an exploration coin), as opposed to one it holds as an item.
/// </summary>
public class DbCharacterCollectionEntry
{
    public int EntryId { get; set; }
    public ulong CharacterId { get; set; }
    public DbCharacter Character { get; set; } = null!;
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
