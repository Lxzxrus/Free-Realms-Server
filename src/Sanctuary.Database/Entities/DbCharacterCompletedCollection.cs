using System;

namespace Sanctuary.Database.Entities;

/// <summary>
/// A collection a character has finished. Its reward is paid with this row, so finishing it again (selling an entry
/// item of an item-based collection and picking it up again) pays nothing.
/// </summary>
public class DbCharacterCompletedCollection
{
    public int CollectionId { get; set; }
    public ulong CharacterId { get; set; }
    public DbCharacter Character { get; set; } = null!;
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}
