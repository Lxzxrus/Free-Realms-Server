using Sanctuary.Game.Collections;

namespace Sanctuary.Game.Resources.Definitions;

public sealed class CollectionEntryDefinition
{
    public int Id { get; set; }
    public int NameId { get; set; }
    public int IconId { get; set; }
    public int IconTintId { get; set; }

    /// <summary>
    /// The item that marks this entry collected while the character owns it. 0 for a direct entry, which is
    /// collected once by picking up the collection node that names it and is remembered per character.
    /// </summary>
    public int ItemDefinitionId { get; set; }

    public bool IsDirect => ItemDefinitionId == 0;

    public bool IsCollected(CollectionProgress progress)
    {
        return IsDirect
            ? progress.CollectedEntryIds.Contains(Id)
            : progress.OwnedItemDefinitionIds.Contains(ItemDefinitionId);
    }
}
