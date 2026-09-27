using System.Linq;

using Sanctuary.Database;

namespace Sanctuary.Game.Housing;

/// <summary>
/// The on/off settings an owner can flip from the house panel. Each maps to a column on <c>DbHouse</c>.
/// </summary>
public enum HouseSetting
{
    Locked,
    FloraAllowed,
    PetAutospawn
}

public static class HouseSettings
{
    /// <summary>
    /// Flips <paramref name="setting"/> on the house and saves it.
    /// </summary>
    /// <returns>The new value, or <c>null</c> if <paramref name="ownerId"/> doesn't own that house.</returns>
    public static bool? Toggle(DatabaseContext dbContext, ulong houseId, ulong ownerId, HouseSetting setting)
    {
        var house = dbContext.Houses.FirstOrDefault(candidate =>
            candidate.Id == houseId &&
            candidate.CharacterId == ownerId);

        if (house is null)
            return null;

        bool value;

        switch (setting)
        {
            case HouseSetting.Locked:
                value = house.IsLocked = !house.IsLocked;
                break;

            case HouseSetting.FloraAllowed:
                value = house.IsFloraAllowed = !house.IsFloraAllowed;
                break;

            case HouseSetting.PetAutospawn:
                value = house.PetAutospawn = !house.PetAutospawn;
                break;

            default:
                return null;
        }

        dbContext.SaveChanges();

        return value;
    }
}
