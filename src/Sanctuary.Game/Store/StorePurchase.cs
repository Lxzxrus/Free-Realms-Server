using System.Collections.Generic;
using System.Globalization;

using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.GameCommerce;

namespace Sanctuary.Game.Store;

/// <summary>
/// Buying from a store: the quantity and the colour are the client's word, so they are checked before anything is
/// written. Each colour of an item is an inventory row of its own, and each pet is a row, which every login loads.
/// </summary>
public static class StorePurchase
{
    /// <summary>
    /// The most of an item with no stack limit one request may buy. The client's quantity picker stops at 99 for such an
    /// item, in the coin store and the Marketplace alike, and its count can't be typed in.
    /// </summary>
    public const int MaxQuantity = 99;

    /// <summary>
    /// The colours of the tint groups coin store items (<see cref="ItemDefinitionMetaData.TintGroupId"/>) and Station
    /// Cash bundles (<see cref="MarketingBundleDefinition.TintGroup"/>) use, as the client's colour picker lists them
    /// (its Resources/TintGroupMappings.txt). They are dye palette tints (<see cref="Housing.HousingDyeTints"/>), except
    /// group 3's mahogany: tint 96, not the palette's 229, and group 4's tints 1 to 6 (the Oversized Ears).
    /// </summary>
    public static readonly IReadOnlyDictionary<int, IReadOnlySet<int>> TintGroups = new Dictionary<int, IReadOnlySet<int>>
    {
        [1] = new HashSet<int> { 228, 232, 233, 234, 235, 236, 242, 247, 250, 253, 254, 261 },
        [2] = new HashSet<int> { 227, 231, 232, 233, 234, 235, 237, 243, 245, 249, 256, 258, 260, 262 },
        [3] = new HashSet<int> { 96, 230, 238, 239, 240, 241, 244, 246, 248, 251, 252, 255, 257, 259, 263, 264 },
        [4] = new HashSet<int> { 1, 2, 3, 4, 5, 6 },
        [6] = new HashSet<int> { 227, 231, 232, 233, 234, 235, 237, 243, 245, 249, 256, 258, 263, 264 },
        [12] = new HashSet<int> { 227, 231, 233, 234, 235, 237, 243, 245, 249, 256, 258, 261, 263, 264 },
        [13] = new HashSet<int> { 228, 236, 242, 247, 250, 253, 254, 261, 263, 264 }
    };

    /// <summary>
    /// Whether one request may buy <paramref name="quantity"/> of <paramref name="item"/>: at least one, and no more than
    /// its stack limit, or <see cref="MaxQuantity"/> when it has none. A limit also counts what is already owned, which
    /// the store checks against the database.
    /// </summary>
    public static bool IsQuantityAllowed(ClientItemDefinition item, long quantity) =>
        quantity > 0 && quantity <= (item.MaxStackSize > 0 ? item.MaxStackSize : MaxQuantity);

    /// <summary>
    /// Whether the coin store sells <paramref name="item"/> in <paramref name="tint"/>: a colour of its tint group, which
    /// is what the colour picker sends, the item's own tint, which the store sends when there is no colour to pick, or 0,
    /// which the client sends for an item that isn't tintable.
    /// </summary>
    public static bool IsOfferedTint(ClientItemDefinition item, int tintGroupId, int tint) =>
        tint == 0 || tint == item.Icon.TintId || TintGroups.TryGetValue(tintGroupId, out var tints) && tints.Contains(tint);

    /// <summary>
    /// Whether one Station Cash order may buy <paramref name="quantity"/> of <paramref name="bundle"/>: at least one, and
    /// no more than its MaxPerOrder, or <see cref="MaxQuantity"/> when it has none (no shipped bundle has one). That is as
    /// far as the Marketplace's quantity picker goes, and the Station Cash merchant's stops at 99 too. It counts
    /// bundles, not items, so a pack of six clouds still sells 99 packs at once.
    /// </summary>
    public static bool IsOrderQuantityAllowed(StoreBundleDefinition bundle, long quantity) =>
        quantity > 0 && quantity <= (bundle.MaxPerOrder > 0 ? bundle.MaxPerOrder : MaxQuantity);

    /// <summary>
    /// Whether the Station Cash store sells <paramref name="bundle"/> in <paramref name="tint"/>: a colour of its tint
    /// group, which is what the colour picker sends, its own tint (its image's), which the client sends for a tintable
    /// bundle when no colour is picked, or 0, which it sends for a bundle that isn't tintable. The group is the bundle's,
    /// not its item's coin store group: the Marketplace's and the merchant's pickers both show the bundle's.
    /// </summary>
    public static bool IsOfferedTint(MarketingBundleDefinition bundle, int tint) =>
        tint == 0 || tint == ParseId(bundle.Image.Tint) ||
        TintGroups.TryGetValue(ParseId(bundle.TintGroup), out var tints) && tints.Contains(tint);

    // A bundle's tint and tint group are numbers sent as text, and the client reads a missing one as 0.
    private static int ParseId(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
}
