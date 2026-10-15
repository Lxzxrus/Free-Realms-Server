using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Database.Entities;

namespace Sanctuary.Game.Housing;

public enum PlotChangeResult
{
    /// <summary>The house is now the kind of plot asked for.</summary>
    Changed,

    /// <summary>The house already was that kind of plot.</summary>
    Unchanged,

    /// <summary>The character doesn't own the house.</summary>
    NotOwner
}

/// <param name="Replaced">
/// The account's previous ranked plot, now personal, when <see cref="HousePlots.MakeRanked"/> moved the ranked plot
/// from another house. Its <see cref="DbHouse.Character"/> is loaded.
/// </param>
public readonly record struct PlotChange(PlotChangeResult Result, DbHouse? Replaced = null);

/// <summary>
/// An account's two plots (<c>docs/design/evergrove-housing.md</c>, section 2). A house belongs to the character that
/// bought it, but the ranked plot is per account: one house across all the account's characters is
/// <see cref="HousePlot.Ranked"/>, and every other house is personal. Existing houses are personal.
/// </summary>
public static class HousePlots
{
    /// <summary>The account a character belongs to, or <c>null</c> if there's no such character.</summary>
    public static ulong? FindUserId(DatabaseContext dbContext, ulong characterId)
    {
        return dbContext.Characters
            .Where(character => character.Id == characterId)
            .Select(character => (ulong?)character.UserId)
            .FirstOrDefault();
    }

    /// <summary>
    /// The account's ranked plot, with its <see cref="DbHouse.Character"/>, or <c>null</c> if it has none.
    /// </summary>
    public static DbHouse? FindRankedPlot(DatabaseContext dbContext, ulong userId)
    {
        return dbContext.Houses
            .AsNoTracking()
            .Include(house => house.Character)
            .Where(house => house.Character.UserId == userId && house.Plot == HousePlot.Ranked)
            .AsEnumerable()
            // SQLite can't sort by ulong in SQL, so sort after loading.
            .OrderBy(house => house.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Makes a house its account's ranked plot from <paramref name="now"/>: only votes from then on count for it.
    /// Whichever house was the account's ranked plot before, on any of its characters, becomes personal, and its votes
    /// this week stop counting. Saves the changes.
    /// </summary>
    public static PlotChange MakeRanked(DatabaseContext dbContext, ulong houseId, ulong ownerCharacterId, DateTimeOffset now)
    {
        var house = dbContext.Houses
            .Include(candidate => candidate.Character)
            .FirstOrDefault(candidate => candidate.Id == houseId && candidate.CharacterId == ownerCharacterId);

        if (house is null)
            return new PlotChange(PlotChangeResult.NotOwner);

        var userId = house.Character.UserId;
        var others = dbContext.Houses
            .Include(candidate => candidate.Character)
            .Where(candidate =>
                candidate.Character.UserId == userId &&
                candidate.Plot == HousePlot.Ranked &&
                candidate.Id != houseId)
            .ToList();

        if (house.Plot == HousePlot.Ranked && others.Count == 0)
            return new PlotChange(PlotChangeResult.Unchanged);

        foreach (var other in others)
        {
            other.Plot = HousePlot.Personal;
            other.RankedSince = null;
        }

        if (house.Plot != HousePlot.Ranked)
        {
            house.Plot = HousePlot.Ranked;
            house.RankedSince = now;
        }

        dbContext.SaveChanges();

        return new PlotChange(PlotChangeResult.Changed, others.OrderBy(other => other.Id).FirstOrDefault());
    }

    /// <summary>
    /// Makes a house a personal plot, which takes it out of the competition. Its votes are kept, but if it becomes the
    /// ranked plot again they don't count again. Saves the change.
    /// </summary>
    public static PlotChange MakePersonal(DatabaseContext dbContext, ulong houseId, ulong ownerCharacterId)
    {
        var house = dbContext.Houses
            .FirstOrDefault(candidate => candidate.Id == houseId && candidate.CharacterId == ownerCharacterId);

        if (house is null)
            return new PlotChange(PlotChangeResult.NotOwner);

        if (house.Plot == HousePlot.Personal)
            return new PlotChange(PlotChangeResult.Unchanged);

        house.Plot = HousePlot.Personal;
        house.RankedSince = null;
        dbContext.SaveChanges();

        return new PlotChange(PlotChangeResult.Changed);
    }
}
