using Sanctuary.Packet.Common.GameCommerce;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// What the Station Cash store charges for one of a bundle (PacketInGamePurchasePlaceOrderPacketHandler). Anyone who
/// isn't a member pays the list price. A member pays the bundle's MembersOnlyPrice for housing inventory, and its
/// MemberDiscount (a price, despite the name) for anything else. Every Evergrove account is a member. The client shows
/// members MembersOnlyPrice, so a bundle whose two member prices differ shows one price and charges another.
/// </summary>
public static class StoreBundlePricing
{
    public static int GetCost(AppStoreBundleDefinition bundle, bool isMember, bool containsHousingInventory) =>
        !isMember
            ? bundle.Price
            : containsHousingInventory
                ? bundle.MembersOnlyPrice
                : bundle.MemberDiscount;
}
