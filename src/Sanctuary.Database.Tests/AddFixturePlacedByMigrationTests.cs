using System.Collections.Generic;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;

namespace Sanctuary.Database.Tests;

/// <summary>
/// Houses are shared by every character on their account since task 35, so each fixture records the character that
/// placed it. Before then only the character that bought a house could build in it, and AddFixturePlacedBy fills that
/// character in for every fixture already placed, keeping every house and fixture.
/// </summary>
[TestClass]
public class AddFixturePlacedByMigrationTests
{
    private const string MigrationName = "AddFixturePlacedBy";

    private static IEnumerable<object[]> Migrations()
    {
        yield return [new Sanctuary.Database.MySql.Migrations.AddFixturePlacedBy()];
        yield return [new Sanctuary.Database.Sqlite.Migrations.AddFixturePlacedBy()];
    }

    [TestMethod]
    [DynamicData(nameof(Migrations))]
    public void AddsTheColumn_AndFillsItFromTheHousesBuyer(Migration migration)
    {
        var operations = migration.UpOperations;

        var column = operations.OfType<AddColumnOperation>().Single();
        Assert.AreEqual("HouseFixtures", column.Table);
        Assert.AreEqual(nameof(DbHouseFixture.PlacedByCharacterId), column.Name);
        Assert.IsFalse(column.IsNullable);

        var backfill = operations.OfType<SqlOperation>().Single();
        StringAssert.Contains(backfill.Sql, "UPDATE HouseFixtures SET PlacedByCharacterId");
        StringAssert.Contains(backfill.Sql, "SELECT Houses.CharacterId FROM Houses WHERE Houses.Id = HouseFixtures.HouseId");

        Assert.IsFalse(operations.OfType<DropColumnOperation>().Any(), "Nothing is dropped.");
        Assert.IsFalse(operations.OfType<DropTableOperation>().Any(), "Nothing is dropped.");
    }

    [TestMethod]
    public void KeepsEveryHouseAndFixture_OnSqlite()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder().UseSqlite(connection).Options;

        // Two characters on one account each bought a house, as they could before houses were shared, and a third
        // house belongs to another account. Every fixture was placed by its house's buyer.
        using (var dbContext = new SqliteDatabaseContext(options))
        {
            dbContext.Database.Migrate();

            var user = new DbUser { Id = 1, Username = "builder", Password = string.Empty };
            user.Characters.Add(NewCharacter(10));
            user.Characters.Add(NewCharacter(11));

            var neighbour = new DbUser { Id = 2, Username = "neighbour", Password = string.Empty };
            neighbour.Characters.Add(NewCharacter(20));

            dbContext.Users.AddRange(user, neighbour);
            dbContext.Houses.AddRange(
                NewHouse(100, 10, 1, fixtures: 3, plot: HousePlot.Ranked),
                NewHouse(101, 11, 1, fixtures: 2),
                NewHouse(102, 20, 2, fixtures: 1),
                NewHouse(103, 10, 2, fixtures: 0));
            dbContext.SaveChanges();
        }

        var before = Snapshot(options);

        // Back to the schema before the column existed, then forward through the migration again.
        using (var dbContext = new SqliteDatabaseContext(options))
        {
            var migrations = dbContext.Database.GetMigrations().ToList();
            var index = migrations.FindIndex(migration => migration.EndsWith("_" + MigrationName));
            Assert.IsTrue(index > 0, "The migration exists and isn't the first.");

            var migrator = dbContext.GetService<IMigrator>();
            migrator.Migrate(migrations[index - 1]);
            migrator.Migrate();
        }

        var after = Snapshot(options);

        CollectionAssert.AreEqual(before.Houses, after.Houses, "Every house is kept, as it was.");
        CollectionAssert.AreEqual(
            before.Fixtures.Select(fixture => (fixture.Id, fixture.HouseId)).ToList(),
            after.Fixtures.Select(fixture => (fixture.Id, fixture.HouseId)).ToList(),
            "Every fixture is kept, in its house.");

        var buyers = after.Houses.ToDictionary(house => house.Id, house => house.CharacterId);
        foreach (var fixture in after.Fixtures)
            Assert.AreEqual(buyers[fixture.HouseId], fixture.PlacedByCharacterId, $"Fixture {fixture.Id} was placed by its house's buyer.");
    }

    private static (List<(ulong Id, ulong CharacterId, int ZoneDefinitionId, HousePlot Plot)> Houses, List<DbHouseFixture> Fixtures) Snapshot(DbContextOptions options)
    {
        using var dbContext = new SqliteDatabaseContext(options);

        var houses = dbContext.Houses.AsNoTracking()
            .AsEnumerable()
            .OrderBy(house => house.Id)
            .Select(house => (house.Id, house.CharacterId, house.ZoneDefinitionId, house.Plot))
            .ToList();
        var fixtures = dbContext.HouseFixtures.AsNoTracking()
            .OrderBy(fixture => fixture.Id)
            .ToList();

        return (houses, fixtures);
    }

    private static DbHouse NewHouse(ulong id, ulong characterId, int zoneDefinitionId, int fixtures, HousePlot plot = HousePlot.Personal)
    {
        var house = new DbHouse { Id = id, CharacterId = characterId, ZoneDefinitionId = zoneDefinitionId, Plot = plot };

        for (var index = 0; index < fixtures; index++)
            house.Fixtures.Add(new DbHouseFixture { ItemDefinitionId = 22940, PlacedByCharacterId = characterId });

        return house;
    }

    private static DbCharacter NewCharacter(ulong id) => new()
    {
        Id = id,
        FirstName = $"Character{id}",
        Head = string.Empty,
        HeadId = 0,
        Hair = string.Empty,
        HairId = 0,
        SkinTone = string.Empty,
        SkinToneId = 0
    };
}
