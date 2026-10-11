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

public interface IHouseManager
{
    IReadOnlyList<DbHouse> GetOwnedHouses(ulong characterId);

    EnterHouseResult EnterOwnedHouse(Player player, int zoneDefinitionId);
    EnterHouseResult EnterHouse(Player player, ulong houseGuid);
    EnterHouseResult VisitHouse(Player player, int zoneDefinitionId, string ownerName);

    bool LeaveHouse(Player player);

    /// <summary>
    /// Sends everyone who may not be in a locked house (see <see cref="HouseAccess"/>) back to the world.
    /// </summary>
    /// <returns>How many players were sent out.</returns>
    int SendOutLockedOutVisitors(HousingZone zone);
}
