using System.Collections.Generic;
using System.Linq;

using Sanctuary.Core.Helpers;
using Sanctuary.Database.Entities;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Housing;

/// <summary>
/// Who may be in a house. A house belongs to its account (see <see cref="HouseOwnership"/>), so every character on
/// that account may always come in. So may the friends of the character who bought it; anyone else only while the
/// house is published and unlocked.
/// </summary>
public static class HouseAccess
{
    /// <summary>
    /// Whether a player's account owns the house, which lets the player do whatever the owner can. Account 0 is no
    /// account, and owns nothing.
    /// </summary>
    public static bool IsOwner(ulong playerUserId, ulong ownerUserId)
    {
        return playerUserId != 0 && playerUserId == ownerUserId;
    }

    /// <summary>Whether the character who bought the house is on a player's friend list.</summary>
    public static bool IsOwnersFriend(IEnumerable<FriendData> friends, ulong ownerCharacterId)
    {
        var ownerGuid = GuidHelper.GetPlayerGuid(ownerCharacterId);

        return friends.Any(friend => friend.Guid == ownerGuid);
    }

    /// <summary>Whether a player may stay in, or come into, the house while it is locked.</summary>
    /// <param name="ownerUserId">The account that owns the house.</param>
    /// <param name="ownerCharacterId">The character who bought it.</param>
    public static bool MayBeInLockedHouse(
        ulong playerUserId,
        IEnumerable<FriendData> friends,
        ulong ownerUserId,
        ulong ownerCharacterId)
    {
        return IsOwner(playerUserId, ownerUserId) || IsOwnersFriend(friends, ownerCharacterId);
    }

    /// <summary>
    /// Whether a player may enter a house. Its account always may. A members-only house turns away everyone else who
    /// isn't a member, and a house that is unpublished or locked turns away everyone but the friends of the character
    /// who bought it.
    /// </summary>
    /// <param name="ownerUserId">The account that owns <paramref name="house"/>.</param>
    public static bool MayEnter(
        ulong playerUserId,
        IEnumerable<FriendData> friends,
        bool isMember,
        DbHouse house,
        ulong ownerUserId)
    {
        if (IsOwner(playerUserId, ownerUserId))
            return true;

        if (house.IsMembersOnly && !isMember)
            return false;

        return (house.IsPublished && !house.IsLocked) || IsOwnersFriend(friends, house.CharacterId);
    }
}
