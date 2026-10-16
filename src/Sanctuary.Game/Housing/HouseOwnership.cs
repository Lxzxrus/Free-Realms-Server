using System.Linq;

using Sanctuary.Database;
using Sanctuary.Database.Entities;

namespace Sanctuary.Game.Housing;

/// <summary>
/// Houses belong to their account (<c>docs/design/evergrove-housing.md</c>, section 2): every character on the account
/// may enter, build in, publish, lock and manage them as the character who bought them can. That character,
/// <see cref="DbHouse.CharacterId"/>, is still who the house is shown as belonging to, and its friends are the house's
/// friends, so nothing changes for visitors.
/// </summary>
public static class HouseOwnership
{
    /// <summary>The houses a character's account owns, whichever of its characters bought them.</summary>
    public static IQueryable<DbHouse> OwnedByAccountOf(DatabaseContext dbContext, ulong characterId)
    {
        var userIds = dbContext.Characters
            .Where(character => character.Id == characterId)
            .Select(character => character.UserId);

        return dbContext.Houses.Where(house => userIds.Contains(house.Character.UserId));
    }
}
