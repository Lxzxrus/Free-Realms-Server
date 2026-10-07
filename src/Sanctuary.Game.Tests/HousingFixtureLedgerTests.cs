using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Trading;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Creative fixtures (task 21) never take from or give to inventory, and fixtures placed from inventory still work
/// as before: that is the whole of what <c>Housing:CreativeMode</c> off means for the database.
/// </summary>
[TestClass]
public sealed class HousingFixtureLedgerTests
{
    private const ulong HouseId = 1;
    private const ulong OwnerId = 7;
    private const ulong VisitorId = 8;

    private const int ChairDefinitionId = 22940;
    private const int ChairTintId = 231;
    private const int ChairStackId = 3;

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

        // The owner already bought two of the chair the tests place.
        dbContext.Items.Add(new DbItem
        {
            Id = ChairStackId,
            CharacterId = OwnerId,
            Definition = ChairDefinitionId,
            Tint = ChairTintId,
            Count = 2
        });
        dbContext.SaveChanges();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection.Dispose();
    }

    [TestMethod]
    public void CreativePlace_LeavesInventoryAlone()
    {
        var before = ReadItems();

        var fixture = Place(Creative());

        Assert.IsNotNull(fixture);
        Assert.IsTrue(fixture.IsCreative);
        Assert.AreEqual(ChairDefinitionId, fixture.ItemDefinitionId);
        AssertItems(before, ReadItems());
        Assert.AreEqual(1, ReadHouse().FurnitureScore);
    }

    [TestMethod]
    public void CreativePlace_IsUnlimited()
    {
        var before = ReadItems();

        for (var i = 0; i < 10; i++)
            Assert.IsNotNull(Place(Creative()), $"placement {i + 1}");

        Assert.AreEqual(10, ReadFixtures().Count);
        AssertItems(before, ReadItems());
    }

    [TestMethod]
    public void CreativePlace_StillRespectsTheHouseFixtureLimit()
    {
        using (var dbContext = CreateDbContext())
        {
            dbContext.Houses.Single(house => house.Id == HouseId).MaxFixtureCount = 1;
            dbContext.SaveChanges();
        }

        Assert.IsNotNull(Place(Creative()));
        Assert.IsNull(Place(Creative()));
    }

    [TestMethod]
    public void CreativePlace_InSomeoneElsesHouse_IsRefused()
    {
        using var dbContext = CreateDbContext();

        Assert.IsNull(HousingFixtureLedger.Place(
            dbContext, HouseId, VisitorId, Guid.NewGuid(), Creative(), Vector4.Zero, Quaternion.Identity, 1f));
    }

    [TestMethod]
    public void CreativePickup_GivesNothingBack()
    {
        var before = ReadItems();
        var fixture = Place(Creative())!;

        using (var dbContext = CreateDbContext())
        {
            Assert.IsTrue(HousingFixtureLedger.TryPickup(dbContext, HouseId, OwnerId, fixture.Id, out var returned));
            Assert.IsNull(returned);
        }

        Assert.AreEqual(0, ReadFixtures().Count);
        AssertItems(before, ReadItems());
        Assert.AreEqual(0, ReadHouse().FurnitureScore);
    }

    [TestMethod]
    public void CreativePickup_OfAPieceThatIsNotInStock_CreatesNoItem()
    {
        // A creative piece of a fixture the owner never bought: picking it up must not create a stack.
        var lamp = new HousingFixtureLedger.Source(38803, 0, HousingCreativeCatalog.RecordIdBase + 38803, true);
        var fixture = Place(lamp)!;

        using (var dbContext = CreateDbContext())
            Assert.IsTrue(HousingFixtureLedger.TryPickup(dbContext, HouseId, OwnerId, fixture.Id, out _));

        Assert.IsFalse(ReadItems().Any(item => item.Definition == 38803));
    }

    [TestMethod]
    public void CreativePickupAll_ReturnsOnlyPiecesPlacedFromInventory()
    {
        Place(Creative());
        Place(Creative());
        Place(FromInventory());

        List<int> removed;
        List<DbItem> returned;
        using (var dbContext = CreateDbContext())
            Assert.IsTrue(HousingFixtureLedger.TryPickupAll(dbContext, HouseId, OwnerId, out removed, out returned));

        Assert.AreEqual(3, removed.Count);
        Assert.AreEqual(1, returned.Count);
        Assert.AreEqual(0, ReadFixtures().Count);

        // Two bought, one placed and returned: still two. The creative pieces added nothing.
        var chairs = ReadItems().Single(item => item.Definition == ChairDefinitionId);
        Assert.AreEqual(2, chairs.Count);
        Assert.AreEqual(1, ReadItems().Count);
    }

    [TestMethod]
    public void CreativeFixture_NeverReachesATrade()
    {
        // Place and pick up creative pieces every way there is, then build the owner's side of a trade the way
        // TradeCommitter does: from the item rows. Nothing creative is there to offer.
        var first = Place(Creative())!;
        Place(Creative());

        using (var dbContext = CreateDbContext())
            HousingFixtureLedger.TryPickup(dbContext, HouseId, OwnerId, first.Id, out _);

        using (var dbContext = CreateDbContext())
            HousingFixtureLedger.TryPickupAll(dbContext, HouseId, OwnerId, out _, out _);

        var stacks = ReadItems()
            .Select(item => new TradeStackSnapshot(
                item.Id, item.Definition, item.Tint, item.Count, 0, true, false, PetProtected: false, 0))
            .ToList();
        Assert.AreEqual(1, stacks.Count);
        Assert.AreEqual(2, stacks[0].Count);

        var owner = new TradePartySnapshot(OwnerId, 0, stacks);
        var visitor = new TradePartySnapshot(VisitorId, 0, []);

        // Offering the creative entry's record id, as a forged client might.
        var creativeOffer = new Dictionary<int, int> { [Creative().ItemRecordId] = 1 };
        var result = TradeTransferPlanner.TryPlan(
            new TradePlanningRequest(owner, creativeOffer, 0, visitor, new Dictionary<int, int>(), 0),
            out _);
        Assert.IsFalse(result.Succeeded);

        // Offering more chairs than were bought.
        var inflatedOffer = new Dictionary<int, int> { [ChairStackId] = 3 };
        result = TradeTransferPlanner.TryPlan(
            new TradePlanningRequest(owner, inflatedOffer, 0, visitor, new Dictionary<int, int>(), 0),
            out _);
        Assert.IsFalse(result.Succeeded);
    }

    // With Housing:CreativeMode off the runtime only places from inventory: today's behaviour.

    [TestMethod]
    public void InventoryPlace_UsesUpOneItem()
    {
        var fixture = Place(FromInventory());

        Assert.IsNotNull(fixture);
        Assert.IsFalse(fixture.IsCreative);
        Assert.AreEqual(ChairTintId, fixture.TintId);
        Assert.AreEqual(1, ReadItems().Single(item => item.Id == ChairStackId).Count);
    }

    [TestMethod]
    public void InventoryPlace_OfTheLastItem_RemovesTheStack()
    {
        Assert.IsNotNull(Place(FromInventory()));
        Assert.IsNotNull(Place(FromInventory()));

        Assert.IsFalse(ReadItems().Any(item => item.Id == ChairStackId));
        Assert.IsNull(Place(FromInventory()), "a third chair from a stack of two");
    }

    [TestMethod]
    public void InventoryPickup_GivesTheItemBack()
    {
        Place(FromInventory());
        var fixture = Place(FromInventory())!;
        Assert.IsFalse(ReadItems().Any(item => item.Id == ChairStackId));

        using (var dbContext = CreateDbContext())
        {
            Assert.IsTrue(HousingFixtureLedger.TryPickup(dbContext, HouseId, OwnerId, fixture.Id, out var returned));
            Assert.IsNotNull(returned);
        }

        var chairs = ReadItems().Single(item => item.Definition == ChairDefinitionId);
        Assert.AreEqual(1, chairs.Count);
        Assert.AreEqual(ChairTintId, chairs.Tint);
    }

    [TestMethod]
    public void InventoryPickupAll_GivesEveryItemBack()
    {
        Place(FromInventory());
        Place(FromInventory());

        using (var dbContext = CreateDbContext())
            Assert.IsTrue(HousingFixtureLedger.TryPickupAll(dbContext, HouseId, OwnerId, out _, out _));

        Assert.AreEqual(2, ReadItems().Single(item => item.Definition == ChairDefinitionId).Count);
        Assert.AreEqual(0, ReadFixtures().Count);
        Assert.AreEqual(0, ReadHouse().FurnitureScore);
    }

    [TestMethod]
    public void Pickup_BySomeoneElse_ChangesNothing()
    {
        var fixture = Place(FromInventory())!;

        using (var dbContext = CreateDbContext())
            Assert.IsFalse(HousingFixtureLedger.TryPickup(dbContext, HouseId, VisitorId, fixture.Id, out _));

        Assert.AreEqual(1, ReadFixtures().Count);
        Assert.AreEqual(1, ReadItems().Single(item => item.Id == ChairStackId).Count);
    }

    private static HousingFixtureLedger.Source Creative() =>
        new(ChairDefinitionId, 0, HousingCreativeCatalog.RecordIdBase + ChairDefinitionId, true);

    private static HousingFixtureLedger.Source FromInventory() =>
        new(ChairDefinitionId, ChairTintId, ChairStackId, false);

    private DbHouseFixture? Place(HousingFixtureLedger.Source source)
    {
        using var dbContext = CreateDbContext();
        return HousingFixtureLedger.Place(
            dbContext,
            HouseId,
            OwnerId,
            Guid.NewGuid(),
            source,
            new Vector4(1, 2, 3, 1),
            Quaternion.Identity,
            1f);
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

    private List<DbHouseFixture> ReadFixtures()
    {
        using var dbContext = CreateDbContext();
        return dbContext.HouseFixtures.AsNoTracking().Where(fixture => fixture.HouseId == HouseId).ToList();
    }

    private List<DbItem> ReadItems()
    {
        using var dbContext = CreateDbContext();
        return dbContext.Items.AsNoTracking()
            .Where(item => item.CharacterId == OwnerId)
            .OrderBy(item => item.Id)
            .ToList();
    }

    private static void AssertItems(List<DbItem> expected, List<DbItem> actual)
    {
        Assert.AreEqual(
            string.Join(", ", expected.Select(item => $"{item.Id}:{item.Definition}/{item.Tint}x{item.Count}")),
            string.Join(", ", actual.Select(item => $"{item.Id}:{item.Definition}/{item.Tint}x{item.Count}")));
    }
}
