using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Sanctuary.Game.Collections;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Resources.Definitions;

public sealed class CollectionDefinition
{
    // The reward icon and text the client shows for coins and experience (the 2014 captures use these in every
    // collection whose reward is not an item).
    public const int CurrencyIconId = 3901;
    public const int CurrencyNameId = 2416;

    public int Id { get; set; }
    public int NameId { get; set; }
    public int CategoryId { get; set; }
    public int DescriptionId { get; set; }
    public int IconId { get; set; }
    public int IconTintId { get; set; }

    public int RewardCoins { get; set; }
    public int RewardExperience { get; set; }
    public int RewardProfileId { get; set; }
    public int RewardTextId { get; set; }
    public List<int> RewardItemIds { get; set; } = [];

    public List<CollectionEntryDefinition> Entries { get; set; } = [];

    public bool IsStarted(CollectionProgress progress)
    {
        return Entries.Any(entry => entry.IsCollected(progress));
    }

    public bool IsComplete(CollectionProgress progress)
    {
        return Entries.Count > 0 && Entries.All(entry => entry.IsCollected(progress));
    }

    public bool TryGetEntry(int entryId, [MaybeNullWhen(false)] out CollectionEntryDefinition entry, out int index)
    {
        for (index = 0; index < Entries.Count; index++)
        {
            if (Entries[index].Id == entryId)
            {
                entry = Entries[index];
                return true;
            }
        }

        entry = null;
        index = -1;
        return false;
    }

    public ClientCollection CreateClientCollection(ulong playerGuid, CollectionProgress progress,
        Func<int, ClientItemDefinition?> itemDefinitions)
    {
        var collection = new ClientCollection
        {
            Id = Id,
            NameId = NameId,
            DescriptionId = DescriptionId,
            CategoryId = CategoryId,
            IconId = IconId,
            IconTintId = IconTintId,
            PlayerGuid = playerGuid,
            RewardTextId = RewardTextId
        };

        FillRewardBundle(collection.Reward, itemDefinitions);

        for (var index = 0; index < Entries.Count; index++)
            collection.Entries.Add(CreateClientCollectionEntry(Entries[index], index, Entries[index].IsCollected(progress)));

        return collection;
    }

    /// <summary>
    /// Writes this collection's reward into <paramref name="bundle"/>: coins, experience and the reward items, with
    /// the first item's icon and name as the bundle's own (as the captured collections carry them).
    /// </summary>
    public void FillRewardBundle(RewardBundleBase bundle, Func<int, ClientItemDefinition?> itemDefinitions)
    {
        bundle.Unknown1 = RewardCoins;
        bundle.IconId = CurrencyIconId;
        bundle.NameId = CurrencyNameId;

        if (RewardExperience > 0)
        {
            bundle.Entries.Add(new RewardBundleEntryExperience
            {
                IconId = CurrencyIconId,
                NameId = CurrencyNameId,
                Quantity = RewardExperience,
                DefinitionId = RewardProfileId
            });
        }

        var first = true;

        foreach (var itemDefinitionId in RewardItemIds)
        {
            var itemDefinition = itemDefinitions(itemDefinitionId);

            if (itemDefinition is null)
                continue;

            if (first)
            {
                bundle.IconId = itemDefinition.Icon.Id;
                bundle.NameId = itemDefinition.NameId;
                first = false;
            }

            bundle.Entries.Add(new RewardBundleEntryItem
            {
                IconId = itemDefinition.Icon.Id,
                TintId = itemDefinition.Icon.TintId,
                NameId = itemDefinition.NameId,
                DefinitionId = itemDefinitionId
            });
        }
    }

    public ClientCollectionEntry CreateClientCollectionEntry(CollectionEntryDefinition entry, int index, bool collected)
    {
        return new ClientCollectionEntry
        {
            Id = entry.Id,
            DefinitionId = entry.Id,
            Index = index + 1,
            CollectionId = Id,
            NameId = entry.NameId,
            IconId = entry.IconId,
            IconTintId = entry.IconTintId,
            Collected = collected
        };
    }
}
