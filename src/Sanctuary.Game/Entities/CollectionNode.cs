using System.Threading;

using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Entities;

public sealed class CollectionNode : Npc
{
    private int _reserved;

    public CollectionNodeTypeDefinition TypeDefinition { get; }
    public CollectionNodePoolDefinition PoolDefinition { get; }
    public CollectionNodeSpawnDefinition SpawnDefinition { get; }

    public CollectionNode(IZone zone, CollectionNodeTypeDefinition typeDefinition, CollectionNodePoolDefinition poolDefinition,
        CollectionNodeSpawnDefinition spawnDefinition)
        : base(zone)
    {
        TypeDefinition = typeDefinition;
        PoolDefinition = poolDefinition;
        SpawnDefinition = spawnDefinition;
    }

    /// <summary>
    /// True when this node ticks a collection entry the player already has. Such a node is never shown to that
    /// player and does nothing if clicked.
    /// </summary>
    public bool IsCollectedBy(Player player)
    {
        return SpawnDefinition.HasCollectionEntry &&
            player.CollectedCollectionEntries.Contains(SpawnDefinition.CollectionEntryId);
    }

    public bool TryReserve()
    {
        return Interlocked.CompareExchange(ref _reserved, 1, 0) == 0;
    }

    public void Release()
    {
        Interlocked.Exchange(ref _reserved, 0);
    }

    public void CompleteCollection()
    {
        Zone.CompleteCollectionNode(this);
    }

    /// <summary>
    /// Removes the node from one player's view with the pickup animation, leaving it in the zone for everyone else.
    /// </summary>
    public void HideFromCollector(Player player)
    {
        player.OnRemoveVisibleNpcGracefully(this,
            animate: true,
            delay: 0,
            effectDelay: 0,
            compositeEffectId: 0,
            duration: 1000);
    }

    internal void DisposeAfterCollection()
    {
        DisposeGracefully(
            animate: true,
            delay: 0,
            effectDelay: 0,
            compositeEffectId: 0,
            duration: 1000);
    }
}
