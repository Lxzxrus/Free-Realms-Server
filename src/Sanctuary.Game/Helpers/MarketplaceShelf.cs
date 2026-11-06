using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet.Common.GameCommerce;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// What the Marketplace's shelf offers, as each zone's welcome sends it (<c>BaseZone</c>). Worked out here, rather than
/// in the zone, so the tests can check it against the shipped store.
/// </summary>
public static class MarketplaceShelf
{
    /// <summary>The item type of a lot's deed: a bundle that holds one sells a lot.</summary>
    public const int HouseItemType = 16;

    /// <summary>
    /// The store's bundles the shelf offers, in the store's order, each as the shelf shows it. A bundle with an item the
    /// server doesn't know is left off. Of the lots, only those the server serves are offered, free while
    /// <see cref="HousingOptions.FreeLots"/> is on, and not the starter house while every account is given it
    /// (<see cref="StarterHouse.BundleLeftOffTheShelf"/>).
    /// </summary>
    public static IEnumerable<AppStoreBundleDefinition> Offered(StoreDefinition store, ClientItemDefinitionCollection items,
        ZoneDefinitionCollection zones, HousingOptions housingOptions)
    {
        var servedLotBundleIds = zones.Values
            .OfType<HousingZoneDefinition>()
            .Select(definition => definition.StoreBundleId)
            .ToHashSet();

        // Evergrove: every account is given the starter house (the Apartment), so while it is, it isn't offered.
        var starterHouseBundleId = StarterHouse.BundleLeftOffTheShelf(housingOptions, zones);

        foreach (var storeBundle in store.Bundles.Values)
        {
            var containsHouseItem = storeBundle.Entries.Any(entry =>
                items.TryGetValue(entry.MarketingItemId, out var definition) &&
                definition.Type == HouseItemType);

            if (containsHouseItem && !servedLotBundleIds.Contains(storeBundle.Id))
                continue;

            if (storeBundle.Id == starterHouseBundleId)
                continue;

            if (!storeBundle.Entries.All(entry => items.ContainsKey(entry.MarketingItemId)))
                continue;

            yield return containsHouseItem && housingOptions.FreeLots
                ? storeBundle.AsFree()
                : storeBundle;
        }
    }
}
