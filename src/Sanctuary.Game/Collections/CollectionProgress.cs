using System.Collections.Generic;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Collections;

/// <summary>
/// What one character has collected: the item definitions it owns (item-based entries) and the entry ids it has
/// picked up directly (exploration coins, which leave nothing in the inventory).
/// </summary>
public readonly record struct CollectionProgress(
    IReadOnlySet<int> OwnedItemDefinitionIds,
    IReadOnlySet<int> CollectedEntryIds)
{
    public static readonly CollectionProgress Empty = new(new HashSet<int>(), new HashSet<int>());

    public static CollectionProgress From(Player player)
    {
        var owned = new HashSet<int>();

        foreach (var item in player.Items)
            owned.Add(item.Definition);

        return new CollectionProgress(owned, player.CollectedCollectionEntries);
    }
}
