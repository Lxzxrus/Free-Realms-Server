using System;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HousePlotsTests
{
    // One account with two characters, each with a house; another account with one.
    private const ulong User = 1, Character = 10, Alt = 11;
    private const ulong OtherUser = 2, Stranger = 20;
    private const ulong Seaside = 100, Blackspore = 101, AltsHouse = 102, StrangersHouse = 103;

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private SqliteConnection _connection = null!;

    [TestInitialize]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
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

    private void SeedHouses()
    {
        using var dbContext = CreateDbContext();
        dbContext.Database.Migrate();

        var user = new DbUser { Id = User, Username = "builder", Password = string.Empty };
        user.Characters.Add(NewCharacter(Character));
        user.Characters.Add(NewCharacter(Alt));

        var otherUser = new DbUser { Id = OtherUser, Username = "neighbour", Password = string.Empty };
        otherUser.Characters.Add(NewCharacter(Stranger));

        dbContext.Users.AddRange(user, otherUser);

        // Houses as they were before plots: nothing says which kind they are.
        dbContext.Houses.AddRange(
            new DbHouse { Id = Seaside, CharacterId = Character, ZoneDefinitionId = 1 },
            new DbHouse { Id = Blackspore, CharacterId = Character, ZoneDefinitionId = 2 },
            new DbHouse { Id = AltsHouse, CharacterId = Alt, ZoneDefinitionId = 1 },
            new DbHouse { Id = StrangersHouse, CharacterId = Stranger, ZoneDefinitionId = 1 });

        dbContext.SaveChanges();
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

    private HousePlot PlotOf(ulong houseId)
    {
        using var dbContext = CreateDbContext();
        return dbContext.Houses.AsNoTracking().Single(house => house.Id == houseId).Plot;
    }

    private DateTimeOffset? RankedSinceOf(ulong houseId)
    {
        using var dbContext = CreateDbContext();
        return dbContext.Houses.AsNoTracking().Single(house => house.Id == houseId).RankedSince;
    }

    [TestMethod]
    public void NewHouses_ArePersonal()
    {
        SeedHouses();

        foreach (var houseId in new[] { Seaside, Blackspore, AltsHouse, StrangersHouse })
            Assert.AreEqual(HousePlot.Personal, PlotOf(houseId));

        using var dbContext = CreateDbContext();
        Assert.IsNull(HousePlots.FindRankedPlot(dbContext, User));
    }

    [TestMethod]
    public void MakeRanked_GivesTheAccountOneRankedPlot()
    {
        SeedHouses();

        using (var dbContext = CreateDbContext())
        {
            var change = HousePlots.MakeRanked(dbContext, Seaside, Character, Now);

            Assert.AreEqual(PlotChangeResult.Changed, change.Result);
            Assert.IsNull(change.Replaced);
        }

        Assert.AreEqual(HousePlot.Ranked, PlotOf(Seaside));
        Assert.AreEqual(Now, RankedSinceOf(Seaside), "Its votes count from now.");
        Assert.AreEqual(HousePlot.Personal, PlotOf(Blackspore));

        using (var dbContext = CreateDbContext())
        {
            Assert.AreEqual(Seaside, HousePlots.FindRankedPlot(dbContext, User)?.Id);
            Assert.AreEqual(PlotChangeResult.Unchanged, HousePlots.MakeRanked(dbContext, Seaside, Character, Now.AddHours(1)).Result);
        }

        Assert.AreEqual(Now, RankedSinceOf(Seaside), "Saying it again doesn't restart its votes.");
    }

    [TestMethod]
    public void MakeRanked_OnAnotherHouse_MovesTheRankedPlot_EvenFromAnotherCharacter()
    {
        SeedHouses();

        using (var dbContext = CreateDbContext())
            HousePlots.MakeRanked(dbContext, Seaside, Character, Now);

        // The account's other character makes its own house the ranked plot: the account still has only one.
        using (var dbContext = CreateDbContext())
        {
            var change = HousePlots.MakeRanked(dbContext, AltsHouse, Alt, Now);

            Assert.AreEqual(PlotChangeResult.Changed, change.Result);
            Assert.AreEqual(Seaside, change.Replaced?.Id);
            Assert.AreEqual(Character, change.Replaced?.Character.Id, "The replaced plot comes with its owner, for the message.");
        }

        Assert.AreEqual(HousePlot.Personal, PlotOf(Seaside));
        Assert.IsNull(RankedSinceOf(Seaside));
        Assert.AreEqual(HousePlot.Ranked, PlotOf(AltsHouse));

        using (var dbContext = CreateDbContext())
        {
            Assert.AreEqual(1, dbContext.Houses.Count(house => house.Character.UserId == User && house.Plot == HousePlot.Ranked));
            Assert.AreEqual(AltsHouse, HousePlots.FindRankedPlot(dbContext, User)?.Id);
        }
    }

    [TestMethod]
    public void EachAccountHasItsOwnRankedPlot()
    {
        SeedHouses();

        using (var dbContext = CreateDbContext())
        {
            HousePlots.MakeRanked(dbContext, Seaside, Character, Now);
            HousePlots.MakeRanked(dbContext, StrangersHouse, Stranger, Now);
        }

        Assert.AreEqual(HousePlot.Ranked, PlotOf(Seaside));
        Assert.AreEqual(HousePlot.Ranked, PlotOf(StrangersHouse));
    }

    [TestMethod]
    public void EveryCharacterOnTheAccount_OwnsEveryHouseItBought()
    {
        SeedHouses();

        using var dbContext = CreateDbContext();

        ulong[] Owned(ulong characterId) => HouseOwnership.OwnedByAccountOf(dbContext, characterId)
            .Select(house => house.Id)
            .AsEnumerable()
            .Order()
            .ToArray();

        CollectionAssert.AreEqual(new[] { Seaside, Blackspore, AltsHouse }, Owned(Character));
        CollectionAssert.AreEqual(new[] { Seaside, Blackspore, AltsHouse }, Owned(Alt));
        CollectionAssert.AreEqual(new[] { StrangersHouse }, Owned(Stranger));
        Assert.IsEmpty(Owned(999), "A character that doesn't exist owns nothing.");
    }

    [TestMethod]
    public void AnotherAccount_CannotChangeAHousesPlot()
    {
        SeedHouses();

        using (var dbContext = CreateDbContext())
        {
            Assert.AreEqual(PlotChangeResult.NotOwner, HousePlots.MakeRanked(dbContext, Seaside, Stranger, Now).Result);
            Assert.AreEqual(PlotChangeResult.NotOwner, HousePlots.MakePersonal(dbContext, Seaside, Stranger).Result);
        }

        Assert.AreEqual(HousePlot.Personal, PlotOf(Seaside));
    }

    [TestMethod]
    public void SecondCharacterOnTheAccount_ManagesItsRankedPlot()
    {
        SeedHouses();

        using (var dbContext = CreateDbContext())
        {
            // The alt makes a house its other character bought the account's ranked plot, then takes it out again.
            Assert.AreEqual(PlotChangeResult.Changed, HousePlots.MakeRanked(dbContext, Seaside, Alt, Now).Result);
            Assert.AreEqual(HousePlot.Ranked, PlotOf(Seaside));

            Assert.AreEqual(PlotChangeResult.Changed, HousePlots.MakePersonal(dbContext, Seaside, Alt).Result);
        }

        Assert.AreEqual(HousePlot.Personal, PlotOf(Seaside));
    }

    [TestMethod]
    public void MakePersonal_TakesTheHouseOutOfTheCompetition()
    {
        SeedHouses();

        using (var dbContext = CreateDbContext())
        {
            HousePlots.MakeRanked(dbContext, Seaside, Character, Now);

            Assert.AreEqual(PlotChangeResult.NotOwner, HousePlots.MakePersonal(dbContext, Seaside, Stranger).Result);
            Assert.AreEqual(PlotChangeResult.Changed, HousePlots.MakePersonal(dbContext, Seaside, Character).Result);
            Assert.AreEqual(PlotChangeResult.Unchanged, HousePlots.MakePersonal(dbContext, Seaside, Character).Result);
            Assert.IsNull(HousePlots.FindRankedPlot(dbContext, User));
        }

        Assert.AreEqual(HousePlot.Personal, PlotOf(Seaside));
        Assert.IsNull(RankedSinceOf(Seaside));
    }

    [TestMethod]
    public void FindUserId_IsTheCharactersAccount()
    {
        SeedHouses();

        using var dbContext = CreateDbContext();
        Assert.AreEqual(User, HousePlots.FindUserId(dbContext, Alt));
        Assert.AreEqual(OtherUser, HousePlots.FindUserId(dbContext, Stranger));
        Assert.IsNull(HousePlots.FindUserId(dbContext, 999));
    }

    /// <summary>
    /// A house that existed before plots, with old-style votes and a rating, comes through the migration as a personal
    /// plot. (EF first scaffolded the migration as a rename of <c>Votes</c> to <c>Plot</c>, which would have made a
    /// house with one vote a ranked plot.)
    /// </summary>
    [TestMethod]
    public void Migration_KeepsExistingHousesWorking_AsPersonalPlots()
    {
        using (var dbContext = CreateDbContext())
        {
            // The seed below matches the schema at AddCharacterLastWheelSpin. Every later migration, including any that
            // land before AddRankedPlots, then runs over the seeded rows.
            var migrations = dbContext.Database.GetMigrations().ToList();
            var seedSchema = migrations.Single(migration => migration.EndsWith("_AddCharacterLastWheelSpin", StringComparison.Ordinal));
            Assert.IsTrue(
                migrations.IndexOf(seedSchema) < migrations.FindIndex(migration => migration.EndsWith("_AddRankedPlots", StringComparison.Ordinal)),
                "AddRankedPlots must come after the seed's schema.");

            dbContext.GetService<IMigrator>().Migrate(seedSchema);

            dbContext.Database.ExecuteSqlRaw("""
                INSERT INTO Users (Id, Username, Password, MaxCharacters, IsMember, IsAdmin, IsMod, Created)
                VALUES (1, 'builder', '', 1, 0, 0, 0, '2026-01-01');
                INSERT INTO Characters (Id, UserId, FirstName, Model, Head, HeadId, Hair, HairId, SkinTone, SkinToneId,
                    EyeColor, HairColor, ActiveProfileId, PlayTime, Gender, VipRank, MembershipStatus,
                    ChatBubbleForegroundColor, ChatBubbleBackgroundColor, ChatBubbleSize, Coins, StationCash, Created)
                VALUES (10, 1, 'Builder', 0, '', 0, '', 0, '', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, '2026-01-01');
                INSERT INTO Houses (Id, CharacterId, ZoneDefinitionId, IsLocked, IsMembersOnly, PetAutospawn, IsPublished, Votes, Rating)
                VALUES (100, 10, 1, 0, 0, 0, 1, 1, 5.0), (101, 10, 2, 0, 0, 0, 0, 0, 0);
                INSERT INTO HouseVotes (HouseId, CharacterId, Value) VALUES (100, 20, 5);
                """);

            dbContext.Database.Migrate();
        }

        using (var dbContext = CreateDbContext())
        {
            var houses = dbContext.Houses.AsNoTracking().ToList();

            Assert.HasCount(2, houses);
            Assert.IsTrue(houses.All(house => house.Plot == HousePlot.Personal), "One vote must not make a house ranked.");
            Assert.IsTrue(houses.Single(house => house.Id == 100).IsPublished);
            Assert.AreEqual(1, dbContext.HouseVotes.Count(), "Old votes are kept as history.");
            Assert.AreEqual(0, dbContext.PlotVotes.Count());
        }
    }
}
