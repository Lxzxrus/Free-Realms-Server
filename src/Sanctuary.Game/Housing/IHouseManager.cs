using System.Collections.Generic;

using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Housing;

public enum EnterHouseResult
{
    Success,
    HouseNotFound,
    NotAuthorized,
    UnsupportedSourceZone,
    ZoneUnavailable,
    TransferFailed
}

public enum HouseInviteResult
{
    Sent,

    /// <summary>The inviter isn't standing in a house.</summary>
    NotInHouse,

    /// <summary>The inviter's account doesn't own the house they're in.</summary>
    NotOwner,

    /// <summary>No such player is online.</summary>
    NotOnline,

    /// <summary>The player invited themselves, or another character on the house's account, which needs no invite.</summary>
    NoInviteNeeded,

    /// <summary>The player invited is already in the house.</summary>
    AlreadyThere,

    /// <summary>The player invited ignores the inviter. The inviter isn't told, as with a tell.</summary>
    Ignored,

    /// <summary>The same player was invited into this house moments ago (<see cref="HouseInvites.Cooldown"/>).</summary>
    TooSoon
}

public interface IHouseManager
{
    /// <summary>
    /// The houses the character's account owns, whichever of its characters bought them, with their
    /// <see cref="DbHouse.Character"/>.
    /// </summary>
    IReadOnlyList<DbHouse> GetOwnedHouses(ulong characterId);

    /// <summary>
    /// Evergrove: gives the player's account its free starter house (<see cref="StarterHouse"/>, the Apartment) if none of
    /// its characters has one: a bare house on the player's character, free whatever <see cref="HousingOptions.FreeLots"/>
    /// says, added the way an order adds one (<see cref="HouseGrants"/>). Never throws, so a login never fails on it.
    /// </summary>
    StarterHouseResult EnsureStarterHouse(Player player);

    /// <summary>Enters the player's account's house on that lot, preferring the player's own if there are two.</summary>
    EnterHouseResult EnterOwnedHouse(Player player, int zoneDefinitionId);
    EnterHouseResult EnterHouse(Player player, ulong houseGuid);
    EnterHouseResult VisitHouse(Player player, int zoneDefinitionId, string ownerName);

    bool LeaveHouse(Player player);

    /// <summary>
    /// Sends everyone who may not be in a locked house (see <see cref="HouseAccess"/>) back to the world.
    /// </summary>
    /// <returns>How many players were sent out.</returns>
    int SendOutLockedOutVisitors(HousingZone zone);

    /// <summary>
    /// Invites a player into the house the inviter is standing in, which their account must own. The invite lets them in
    /// for <see cref="HouseInvites.Lifetime"/>, even if the house is locked or unpublished.
    /// </summary>
    HouseInviteResult InviteToHouse(Player inviter, ulong inviteeGuid, out Player? invitee);

    /// <summary>Declines the player's invite into the house, and tells whoever sent it, if they're online.</summary>
    /// <returns>Whether there was an invite.</returns>
    bool DeclineInvite(Player invitee, ulong houseGuid);
}
