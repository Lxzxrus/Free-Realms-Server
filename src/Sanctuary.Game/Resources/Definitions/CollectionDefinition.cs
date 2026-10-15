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

    /// <summary>
    /// Girls' rewards, where their version of a boys' item has another name (Seeker's Midriff Shirt for Seeker's
    /// Shirt). Empty means the girls' twins of <see cref="RewardItemIds"/>.
    /// </summary>
    public List<int> RewardItemIdsGirls { get; set; } = [];

    /// <summary>The reward items for a character of this gender (1 boys, 2 girls), before matching each to it.</summary>
    public List<int> RewardItemIdsFor(int gender) => gender == 2 && RewardItemIdsGirls.Count > 0 ? RewardItemIdsGirls : RewardItemIds;

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

    /// <param name="itemDefinitions">Looks up a reward item, already fitted to the player's gender.</param>
    /// <param name="gender">The player's gender (1 boys, 2 girls), which picks <see cref="RewardItemIdsGirls"/>.</param>
    public ClientCollection CreateClientCollection(ulong playerGuid, CollectionProgress progress,
        Func<int, ClientItemDefinition?> itemDefinitions, int gender = 0)
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

        FillRewardBundle(collection.Reward, itemDefinitions, gender);

        for (var index = 0; index < Entries.Count; index++)
            collection.Entries.Add(CreateClientCollectionEntry(Entries[index], index, Entries[index].IsCollected(progress)));

        return collection;
    }

    /// <summary>
    /// Writes this collection's reward into <paramref name="bundle"/>: coins, experience and the reward items, with
    /// the first item's icon and name as the bundle's own (as the captured collections carry them).
    /// </summary>
    public void FillRewardBundle(RewardBundleBase bundle, Func<int, ClientItemDefinition?> itemDefinitions, int gender = 0)
    {
        bundle.Coins = RewardCoins;
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

        foreach (var itemDefinitionId in RewardItemIdsFor(gender))
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
                // The fitted item's own id: the client's tooltip reads this one (it said "Boys only" while the
                // picture was already the girls' boot, Ariel, 2026-10-14).
                DefinitionId = itemDefinition.Id
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
