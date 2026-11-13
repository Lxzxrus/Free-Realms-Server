using System.Linq;

using Sanctuary.Database;
using Sanctuary.Database.Entities;

namespace Sanctuary.Game.Housing;

/// <summary>
/// Houses belong to their account (<c>docs/design/evergrove-housing.md</c>, section 2): every character on the account
/// may enter, build in, publish, lock and manage them as the character holding them can. That character,
/// <see cref="DbHouse.CharacterId"/>, is who visitors see the house as belonging to, and its friends are the house's
/// friends. It changes only when that character is deleted and the house moves to another character on the account
/// (<see cref="HouseHandover"/>).
/// </summary>
public static class HouseOwnership
{
    /// <summary>The houses a character's account owns, whichever of its characters holds them.</summary>
    public static IQueryable<DbHouse> OwnedByAccountOf(DatabaseContext dbContext, ulong characterId)
    {
        var userIds = dbContext.Characters
            .Where(character => character.Id == characterId)
            .Select(character => character.UserId);

        return dbContext.Houses.Where(house => userIds.Contains(house.Character.UserId));
    }

    /// <summary>
    /// The house a character holds on a lot, with the account that owns it, or <c>null</c>. A character holds one house
    /// of each lot at most (the unique index on <c>Houses</c>).
    /// </summary>
    public static (ulong HouseId, ulong UserId)? FindHeld(
        DatabaseContext dbContext,
        ulong characterId,
        int zoneDefinitionId)
    {
        var house = dbContext.Houses
            .Where(candidate => candidate.CharacterId == characterId && candidate.ZoneDefinitionId == zoneDefinitionId)
            .Select(candidate => new { candidate.Id, candidate.Character.UserId })
            .SingleOrDefault();

        return house is null ? null : (house.Id, house.UserId);
    }

    /// <summary>
    /// Evergrove: the character holding a house now, or <c>null</c> if there's no such house. It can change while the
    /// house is loaded (<see cref="HouseHandover"/>), so code in a house asks, rather than going by whom it was loaded
    /// for.
    /// </summary>
    public static ulong? FindHolder(DatabaseContext dbContext, ulong houseId)
    {
        return dbContext.Houses
            .Where(house => house.Id == houseId)
            .Select(house => (ulong?)house.CharacterId)
            .FirstOrDefault();
    }
}
