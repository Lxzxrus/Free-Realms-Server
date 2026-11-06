using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;

namespace Sanctuary.Game.Housing;

/// <summary>One of a deleted character's houses, which moved to <paramref name="HolderId"/>, on the same account.</summary>
public readonly record struct HouseMove(ulong HouseId, int ZoneDefinitionId, ulong HolderId);

/// <param name="Moved">The houses that moved to another character on the account.</param>
/// <param name="Unmoved">
/// The houses no other character on the account could take, which go with the character when it's deleted.
/// </param>
/// <param name="OtherCharacters">How many other characters the account has.</param>
public sealed record HouseHandoverResult(IReadOnlyList<HouseMove> Moved, IReadOnlyList<ulong> Unmoved, int OtherCharacters);

/// <summary>
/// Evergrove: what happens to a character's houses when the character is deleted
/// (<c>docs/design/evergrove-housing.md</c>, section 8). Houses belong to the account, so each moves to the account's
/// oldest remaining character that doesn't have that lot yet. The oldest, so where houses go is plain to see and never
/// depends on who played last, and they gather on one character rather than scattering. A house goes with the deleted
/// character only when no other character can hold it: every one of them already has that lot (an account keeps one
/// house of each lot), or the account has no other character (a house is always on a character).
/// </summary>
public static class HouseHandover
{
    /// <summary>
    /// Moves the houses on <paramref name="characterId"/> to other characters on its account. Call it in the
    /// transaction that deletes the character, before the delete, as <see cref="CharacterDeletion.Delete"/> does: the
    /// delete's cascade then takes only the houses left on it. Make the transaction serializable, so two deletes on one
    /// account at once can't both give one character the same lot. The moves are written to the database straight
    /// away, not through the change tracker.
    /// </summary>
    /// <remarks>
    /// A house that moves keeps everything but the character it's on: its id, which its votes are kept by; its plot and
    /// <c>RankedSince</c>, so a ranked plot stays ranked with its votes; its fixtures, name and settings. It stays on the
    /// same account, so nothing about who may vote on it, or which plot is the account's ranked one, changes.
    /// </remarks>
    public static HouseHandoverResult MoveOff(DatabaseContext dbContext, ulong characterId)
    {
        if (HousePlots.FindUserId(dbContext, characterId) is not ulong userId)
            return new HouseHandoverResult([], [], 0);

        var houses = dbContext.Houses
            .AsNoTracking()
            .Where(house => house.CharacterId == characterId)
            .Select(house => new { house.Id, house.ZoneDefinitionId })
            .ToList()
            // SQLite can't sort by ulong in SQL, so sort after loading.
            .OrderBy(house => house.Id)
            .ToList();

        var heirs = dbContext.Characters
            .AsNoTracking()
            .Where(character => character.UserId == userId && character.Id != characterId)
            .Select(character => new { character.Id, character.Created })
            .ToList()
            // SQLite can't sort by DateTimeOffset or ulong in SQL, so sort after loading.
            .OrderBy(character => character.Created)
            .ThenBy(character => character.Id)
            .Select(character => character.Id)
            .ToList();

        if (houses.Count == 0)
            return new HouseHandoverResult([], [], heirs.Count);

        // The lots the other characters have. A character holds one house of each lot at most (the unique index on
        // Houses), but before task 35 two characters on an account could each buy the same lot.
        var taken = dbContext.Houses
            .AsNoTracking()
            .Where(house => heirs.Contains(house.CharacterId))
            .Select(house => new { house.CharacterId, house.ZoneDefinitionId })
            .AsEnumerable()
            .Select(house => (house.CharacterId, house.ZoneDefinitionId))
            .ToHashSet();

        var moved = new List<HouseMove>();
        var unmoved = new List<ulong>();

        foreach (var house in houses)
        {
            ulong? holderId = null;

            foreach (var heir in heirs)
            {
                if (!taken.Contains((heir, house.ZoneDefinitionId)))
                {
                    holderId = heir;
                    break;
                }
            }

            if (holderId is not ulong newHolderId)
            {
                unmoved.Add(house.Id);
                continue;
            }

            dbContext.Houses
                .Where(candidate => candidate.Id == house.Id)
                .ExecuteUpdate(setters => setters.SetProperty(candidate => candidate.CharacterId, newHolderId));

            // The character's houses are each on a different lot, so this never turns another of them away; it keeps the
            // unique index whatever the rows say.
            taken.Add((newHolderId, house.ZoneDefinitionId));
            moved.Add(new HouseMove(house.Id, house.ZoneDefinitionId, newHolderId));
        }

        return new HouseHandoverResult(moved, unmoved, heirs.Count);
    }
}
