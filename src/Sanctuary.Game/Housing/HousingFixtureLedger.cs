using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;

using Sanctuary.Database;
using Sanctuary.Database.Entities;

namespace Sanctuary.Game.Housing;

/// <summary>
/// The database half of placing and picking up fixtures: what a house gains and what an inventory loses or gets
/// back. <see cref="HousingZoneRuntime"/> owns the packets, the locking and the transaction around these.
/// </summary>
public static class HousingFixtureLedger
{
    /// <summary>
    /// Where a placed fixture comes from. A creative source has no inventory stack behind it, and
    /// <see cref="ItemRecordId"/> is its <see cref="HousingCreativeCatalog"/> record id.
    /// </summary>
    public readonly record struct Source(int ItemDefinitionId, int TintId, int ItemRecordId, bool IsCreative);

    /// <summary>
    /// Adds a fixture to the house. An inventory source uses up one item from its stack; a creative source touches no
    /// inventory. Saves the changes.
    /// </summary>
    /// <returns>The new fixture, or <c>null</c> if the house isn't the character's, is full, or the stack is gone.</returns>
    public static DbHouseFixture? Place(
        DatabaseContext dbContext,
        ulong houseId,
        ulong characterId,
        Guid placementToken,
        Source source,
        Vector4 position,
        Quaternion rotation,
        float scale)
    {
        var house = dbContext.Houses
            .Include(candidate => candidate.Fixtures)
            .FirstOrDefault(candidate =>
                candidate.Id == houseId &&
                candidate.CharacterId == characterId);

        if (house is null || house.Fixtures.Count >= house.MaxFixtureCount)
            return null;

        if (!source.IsCreative)
        {
            var item = dbContext.Items.FirstOrDefault(candidate =>
                candidate.CharacterId == characterId &&
                candidate.Id == source.ItemRecordId &&
                candidate.Definition == source.ItemDefinitionId &&
                candidate.Count > 0);

            if (item is null)
                return null;

            if (item.Count == 1)
                dbContext.Items.Remove(item);
            else
                item.Count--;
        }

        var fixture = new DbHouseFixture
        {
            HouseId = house.Id,
            PlacementToken = placementToken,
            ItemDefinitionId = source.ItemDefinitionId,
            TintId = source.TintId,
            IsCreative = source.IsCreative,
            PositionX = position.X,
            PositionY = position.Y,
            PositionZ = position.Z,
            PositionW = position.W,
            RotationX = rotation.X,
            RotationY = rotation.Y,
            RotationZ = rotation.Z,
            RotationW = rotation.W,
            Scale = scale,
            Created = DateTimeOffset.UtcNow
        };

        house.FurnitureScore++;
        dbContext.HouseFixtures.Add(fixture);
        dbContext.SaveChanges();

        return fixture;
    }

    /// <summary>
    /// Removes one fixture from the house. A fixture placed from inventory goes back to it; a creative fixture just
    /// goes. Saves the changes.
    /// </summary>
    /// <param name="returnedItem">The stack the fixture went back to, or <c>null</c> for a creative fixture.</param>
    /// <returns><c>false</c>, changing nothing, if the fixture isn't in the character's house or can't be returned.</returns>
    public static bool TryPickup(
        DatabaseContext dbContext,
        ulong houseId,
        ulong characterId,
        int fixtureId,
        out DbItem? returnedItem)
    {
        returnedItem = null;

        var fixture = dbContext.HouseFixtures.FirstOrDefault(candidate =>
            candidate.Id == fixtureId &&
            candidate.HouseId == houseId &&
            candidate.House.CharacterId == characterId);

        if (fixture is null)
            return false;

        if (!fixture.IsCreative)
        {
            returnedItem = TryReturnInventoryItem(dbContext, characterId, fixture.ItemDefinitionId, fixture.TintId, 1);
            if (returnedItem is null)
                return false;
        }

        dbContext.HouseFixtures.Remove(fixture);

        var house = dbContext.Houses.First(candidate => candidate.Id == houseId);
        house.FurnitureScore = Math.Max(0, house.FurnitureScore - 1);
        dbContext.SaveChanges();

        return true;
    }

    /// <summary>
    /// Removes every fixture from the house, returning those placed from inventory. Saves the changes.
    /// </summary>
    /// <returns><c>false</c>, changing nothing, if the house isn't the character's or a stack can't be returned.</returns>
    public static bool TryPickupAll(
        DatabaseContext dbContext,
        ulong houseId,
        ulong characterId,
        out List<int> removedFixtureIds,
        out List<DbItem> returnedItems)
    {
        removedFixtureIds = [];
        returnedItems = [];

        var house = dbContext.Houses
            .Include(candidate => candidate.Fixtures)
            .FirstOrDefault(candidate =>
                candidate.Id == houseId &&
                candidate.CharacterId == characterId);

        if (house is null)
            return false;

        foreach (var group in house.Fixtures
                     .Where(fixture => !fixture.IsCreative)
                     .GroupBy(fixture => new { fixture.ItemDefinitionId, fixture.TintId }))
        {
            var returnedItem = TryReturnInventoryItem(
                dbContext,
                characterId,
                group.Key.ItemDefinitionId,
                group.Key.TintId,
                group.Count());
            if (returnedItem is null)
            {
                returnedItems.Clear();
                return false;
            }

            returnedItems.Add(returnedItem);
        }

        removedFixtureIds = house.Fixtures.Select(fixture => fixture.Id).ToList();
        dbContext.HouseFixtures.RemoveRange(house.Fixtures);
        house.FurnitureScore = 0;
        dbContext.SaveChanges();

        return true;
    }

    private static DbItem? TryReturnInventoryItem(
        DatabaseContext dbContext,
        ulong characterId,
        int itemDefinitionId,
        int tintId,
        int count)
    {
        if (count <= 0)
            return null;

        var item = dbContext.Items.FirstOrDefault(candidate =>
            candidate.CharacterId == characterId &&
            candidate.Definition == itemDefinitionId &&
            candidate.Tint == tintId);

        if (item is not null)
        {
            var newCount = (long)item.Count + count;
            if (item.Count < 0 || newCount > int.MaxValue)
                return null;

            item.Count = (int)newCount;
            return item;
        }

        var persistedMaxId = dbContext.Items
            .Where(candidate => candidate.CharacterId == characterId)
            .Select(candidate => (int?)candidate.Id)
            .Max() ?? 0;
        var pendingMaxId = dbContext.ChangeTracker
            .Entries<DbItem>()
            .Where(entry => entry.Entity.CharacterId == characterId)
            .Select(entry => entry.Entity.Id)
            .DefaultIfEmpty()
            .Max();
        var maxId = Math.Max(persistedMaxId, pendingMaxId);
        if (maxId == int.MaxValue)
            return null;

        var nextId = maxId + 1;
        item = new DbItem
        {
            Id = nextId,
            CharacterId = characterId,
            Definition = itemDefinitionId,
            Tint = tintId,
            Count = count
        };
        dbContext.Items.Add(item);
        return item;
    }
}
