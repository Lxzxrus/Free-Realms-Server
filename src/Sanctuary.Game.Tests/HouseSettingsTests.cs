using System;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HouseSettingsTests
{
    private const ulong HouseId = 1;
    private const ulong OwnerId = 7;
    private const ulong VisitorId = 8;

    private SqliteConnection _connection = null!;

    [TestInitialize]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var dbContext = CreateDbContext();
        dbContext.Database.Migrate();

        var user = new DbUser
        {
            Username = "owner",
            Password = string.Empty
        };

        foreach (var characterId in new[] { OwnerId, VisitorId })
        {
            user.Characters.Add(new DbCharacter
            {
                Id = characterId,
                FirstName = $"Character{characterId}",
                Head = string.Empty,
                HeadId = 0,
                Hair = string.Empty,
                HairId = 0,
                SkinTone = string.Empty,
                SkinToneId = 0
            });
        }

        dbContext.Users.Add(user);
        dbContext.Houses.Add(new DbHouse
        {
            Id = HouseId,
            CharacterId = OwnerId,
            ZoneDefinitionId = 1
        });
        dbContext.SaveChanges();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection.Dispose();
    }

    private DatabaseContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder()
            .UseSqlite(_connection)
            .Options;

        return new SqliteDatabaseContext(options);
    }

    private DbHouse ReadHouse()
    {
        using var dbContext = CreateDbContext();
        return dbContext.Houses.AsNoTracking().Single(house => house.Id == HouseId);
    }

    [TestMethod]
    public void NewHouse_IsUnlockedWithFloraAndNoPetAutospawn()
    {
        var house = ReadHouse();

        Assert.IsFalse(house.IsLocked);
        Assert.IsTrue(house.IsFloraAllowed);
        Assert.IsFalse(house.PetAutospawn);
    }

    [TestMethod]
    [DataRow(HouseSetting.Locked)]
    [DataRow(HouseSetting.FloraAllowed)]
    [DataRow(HouseSetting.PetAutospawn)]
    public void Toggle_FlipsAndSavesOnlyThatSetting(HouseSetting setting)
    {
        var before = ReadHouse();

        using (var dbContext = CreateDbContext())
        {
            var value = HouseSettings.Toggle(dbContext, HouseId, OwnerId, setting);
            Assert.AreEqual(!Get(before, setting), value);
        }

        var after = ReadHouse();

        foreach (var other in Enum.GetValues<HouseSetting>())
        {
            var expected = other == setting ? !Get(before, other) : Get(before, other);
            Assert.AreEqual(expected, Get(after, other), $"{other} after toggling {setting}");
        }
    }

    [TestMethod]
    public void ToggleTwice_RestoresTheSetting()
    {
        using (var dbContext = CreateDbContext())
        {
            Assert.AreEqual(true, HouseSettings.Toggle(dbContext, HouseId, OwnerId, HouseSetting.Locked));
            Assert.AreEqual(false, HouseSettings.Toggle(dbContext, HouseId, OwnerId, HouseSetting.Locked));
        }

        Assert.IsFalse(ReadHouse().IsLocked);
    }

    [TestMethod]
    public void Toggle_ByAnotherCharacter_ChangesNothing()
    {
        using (var dbContext = CreateDbContext())
            Assert.IsNull(HouseSettings.Toggle(dbContext, HouseId, VisitorId, HouseSetting.Locked));

        Assert.IsFalse(ReadHouse().IsLocked);
    }

    [TestMethod]
    public void Toggle_OnAMissingHouse_ChangesNothing()
    {
        using var dbContext = CreateDbContext();

        Assert.IsNull(HouseSettings.Toggle(dbContext, HouseId + 1, OwnerId, HouseSetting.Locked));
    }

    private static bool Get(DbHouse house, HouseSetting setting) => setting switch
    {
        HouseSetting.Locked => house.IsLocked,
        HouseSetting.FloraAllowed => house.IsFloraAllowed,
        HouseSetting.PetAutospawn => house.PetAutospawn,
        _ => throw new ArgumentOutOfRangeException(nameof(setting))
    };
}
