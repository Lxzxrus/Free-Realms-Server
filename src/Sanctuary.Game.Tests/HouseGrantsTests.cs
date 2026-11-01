using System;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

/// <summary>
/// <see cref="HouseGrants.Add"/>, the one way a house is added: a lot an account already has is refused, and a cost
/// comes off the character's Station Cash in the same transaction, once.
/// </summary>
[TestClass]
public sealed class HouseGrantsTests
{
    // One account with two characters; another account with one.
    private const ulong User = 1, Character = 10, Alt = 11;
    private const ulong OtherUser = 2, Stranger = 20;
    private const ulong NoSuchCharacter = 99;

    private const int Seaside = 14;
    private const int StationCash = 100, Coins = 50;

    private SqliteConnection _connection = null!;
    private Database _database = null!;

    [TestInitialize]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _database = new Database(_connection);

        using var dbContext = _database.CreateDbContext();
        dbContext.Database.Migrate();

        var user = new DbUser { Id = User, Username = "builder", Password = string.Empty };
        user.Characters.Add(NewCharacter(Character));
        user.Characters.Add(NewCharacter(Alt));

        var otherUser = new DbUser { Id = OtherUser, Username = "neighbour", Password = string.Empty };
        otherUser.Characters.Add(NewCharacter(Stranger));

        dbContext.Users.AddRange(user, otherUser);
        dbContext.SaveChanges();
    }

    [TestCleanup]
    public void Cleanup() => _connection.Dispose();

    private static DbCharacter NewCharacter(ulong id) => new()
    {
        Id = id,
        FirstName = $"Character{id}",
        Head = string.Empty,
        HeadId = 0,
        Hair = string.Empty,
        HairId = 0,
        SkinTone = string.Empty,
        SkinToneId = 0,
        StationCash = StationCash,
        Coins = Coins
    };

    private HouseGrantResult Add(ulong characterId, int cost, out int stationCash) =>
        HouseGrants.Add(_database, NullLogger.Instance, characterId, Seaside, cost, out stationCash);

    private (int StationCash, int Coins) Balance(ulong characterId)
    {
        using var dbContext = _database.CreateDbContext();
        var character = dbContext.Characters.AsNoTracking().Single(candidate => candidate.Id == characterId);

        return (character.StationCash, character.Coins);
    }

    private ulong[] HousesOf(ulong characterId)
    {
        using var dbContext = _database.CreateDbContext();

        return dbContext.Houses.AsNoTracking()
            .Where(house => house.CharacterId == characterId && house.ZoneDefinitionId == Seaside)
            .Select(house => house.Id)
            .ToArray();
    }

    [TestMethod]
    public void AFreeAdd_ChangesNoBalance()
    {
        Assert.AreEqual(HouseGrantResult.Added, Add(Character, 0, out var stationCash));

        Assert.AreEqual(StationCash, stationCash);
        Assert.AreEqual((StationCash, Coins), Balance(Character));
        Assert.AreEqual(1, HousesOf(Character).Length);
    }

    [TestMethod]
    public void APaidAdd_TakesTheCostOnce()
    {
        Assert.AreEqual(HouseGrantResult.Added, Add(Character, 30, out var stationCash));

        Assert.AreEqual(StationCash - 30, stationCash);
        Assert.AreEqual((StationCash - 30, Coins), Balance(Character));
        Assert.AreEqual(1, HousesOf(Character).Length);

        Assert.AreEqual(HouseGrantResult.AlreadyOwned, Add(Character, 30, out _), "The same lot again is refused.");
        Assert.AreEqual((StationCash - 30, Coins), Balance(Character), "The refused one costs nothing.");
        Assert.AreEqual(1, HousesOf(Character).Length);
    }

    [TestMethod]
    public void TooLittleStationCash_AddsNothing_AndTakesNothing()
    {
        Assert.AreEqual(HouseGrantResult.NotEnoughStationCash, Add(Character, StationCash + 1, out var stationCash));

        Assert.AreEqual(0, stationCash);
        Assert.AreEqual((StationCash, Coins), Balance(Character));
        Assert.AreEqual(0, HousesOf(Character).Length);
    }

    /// <summary>Houses belong to the account: a lot another of its characters has is this character's already.</summary>
    [TestMethod]
    public void ALotAnyCharacterOnTheAccountHas_IsAlreadyOwned_AndCostsNothing()
    {
        Assert.AreEqual(HouseGrantResult.Added, Add(Alt, 0, out _));

        Assert.AreEqual(HouseGrantResult.AlreadyOwned, Add(Character, 30, out var stationCash));

        Assert.AreEqual(0, stationCash);
        Assert.AreEqual((StationCash, Coins), Balance(Character));
        Assert.AreEqual((StationCash, Coins), Balance(Alt));
        Assert.AreEqual(0, HousesOf(Character).Length);
        Assert.AreEqual(1, HousesOf(Alt).Length);

        Assert.AreEqual(HouseGrantResult.Added, Add(Stranger, 0, out _), "Another account's lot is no bar.");
    }

    [TestMethod]
    public void AnUnknownCharacter_GetsNothing()
    {
        Assert.AreEqual(HouseGrantResult.NoCharacter, Add(NoSuchCharacter, 0, out _));
        Assert.AreEqual(HouseGrantResult.NoCharacter, Add(NoSuchCharacter, 30, out _));

        Assert.AreEqual(0, HousesOf(NoSuchCharacter).Length);
    }

    [TestMethod]
    public void ANegativeCost_IsRefused()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Add(Character, -1, out _));

        Assert.AreEqual((StationCash, Coins), Balance(Character));
        Assert.AreEqual(0, HousesOf(Character).Length);
    }

    private sealed class Database(SqliteConnection connection) : IDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext() =>
            new SqliteDatabaseContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
    }
}
