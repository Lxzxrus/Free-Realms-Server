using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Store;

/// <summary>
/// Buying from a store: the quantity is the client's word, so it is checked before anything is written. Each pet is a
/// database row of its own, which every login loads.
/// </summary>
public static class StorePurchase
{
    /// <summary>
    /// The most of an item with no stack limit one request may buy. The client's quantity picker stops at 99 for such an
    /// item, in the coin store and the Marketplace alike, and its count can't be typed in.
    /// </summary>
    public const int MaxQuantity = 99;

    /// <summary>
    /// Whether one request may buy <paramref name="quantity"/> of <paramref name="item"/>: at least one, and no more than
    /// its stack limit, or <see cref="MaxQuantity"/> when it has none. A limit also counts what is already owned, which
    /// the store checks against the database.
    /// </summary>
    public static bool IsQuantityAllowed(ClientItemDefinition item, long quantity) =>
        quantity > 0 && quantity <= (item.MaxStackSize > 0 ? item.MaxStackSize : MaxQuantity);
}
