using System.Collections.Generic;
using System.Linq;

using Sanctuary.Core.Helpers;
using Sanctuary.Database.Entities;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Housing;

/// <summary>
/// Who may be in a house. The owner and the owner's friends may always come in; anyone else only while the house is
/// published and unlocked.
/// </summary>
public static class HouseAccess
{
    /// <summary>Whether the owner is on a player's friend list.</summary>
    public static bool IsOwnersFriend(IEnumerable<FriendData> friends, ulong ownerCharacterId)
    {
        var ownerGuid = GuidHelper.GetPlayerGuid(ownerCharacterId);

        return friends.Any(friend => friend.Guid == ownerGuid);
    }

    /// <summary>Whether a player may stay in, or come into, the house while it is locked.</summary>
    public static bool MayBeInLockedHouse(ulong playerGuid, IEnumerable<FriendData> friends, ulong ownerCharacterId)
    {
        return GuidHelper.GetPlayerId(playerGuid) == ownerCharacterId || IsOwnersFriend(friends, ownerCharacterId);
    }

    /// <summary>
    /// Whether a player may enter a house. The owner always may. A members-only house turns away everyone else who
    /// isn't a member, and a house that is unpublished or locked turns away everyone but the owner's friends.
    /// </summary>
    public static bool MayEnter(ulong playerGuid, IEnumerable<FriendData> friends, bool isMember, DbHouse house)
    {
        if (GuidHelper.GetPlayerId(playerGuid) == house.CharacterId)
            return true;

        if (house.IsMembersOnly && !isMember)
            return false;

        return (house.IsPublished && !house.IsLocked) || IsOwnersFriend(friends, house.CharacterId);
    }
}
