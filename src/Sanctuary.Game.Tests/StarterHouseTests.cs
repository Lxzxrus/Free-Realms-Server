using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database.Entities;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The free Apartment every account owns (<c>docs/design/evergrove-housing.md</c>, section 10), given by
/// <see cref="HouseManager.EnsureStarterHouse"/> at the session's first world login: once per account, a bare house on
/// the character logging in, worth nothing.
/// </summary>
[TestClass]
public sealed class StarterHouseTests
{
    private const ulong Character = QuestWorld.CharacterId, Alt = 8;
    private const ulong Stranger = 20;

    private HouseWorld _world = null!;

    [TestInitialize]
    public void Setup() => _world = new HouseWorld();

    [TestCleanup]
    public void Cleanup() => _world.Dispose();

    private StarterHouseResult Login(ulong characterId, ulong userId) =>
        _world.Houses.EnsureStarterHouse(HouseWorld.PlayerOn(characterId, userId));

    private StarterHouseResult Login() => _world.Houses.EnsureStarterHouse(_world.Player);

    private int ItemRows()
    {
        using var dbContext = _world.Quests.CreateDbContext();
        return dbContext.Characters.SelectMany(character => character.Items).Count();
    }

    [TestMethod]
    public void ANewAccount_GetsOneBareApartment_OnTheCharacterLoggingIn()
    {
        _world.SetBalance(Character, stationCash: 40, coins: 15);

        Assert.AreEqual(StarterHouseResult.Granted, Login());

        var apartment = _world.HousesOf(_world.Account).Single();

        Assert.AreEqual(HouseWorld.Apartment, apartment.ZoneDefinitionId);
        Assert.AreEqual(Character, apartment.CharacterId, "Visitors see the character who logged in as its owner.");
        Assert.AreEqual(HousePlot.Personal, apartment.Plot);
        Assert.IsNull(apartment.RankedSince);
        Assert.IsFalse(apartment.IsPublished, "Not in the directory or the competition until its owner says so.");
        Assert.IsFalse(apartment.IsLocked);
        Assert.IsFalse(apartment.IsMembersOnly);
        Assert.AreEqual(2000, apartment.MaxFixtureCount);
        Assert.IsNull(apartment.Name, "It shows as the lot's own name.");
        Assert.AreEqual(0, apartment.Fixtures.Count, "Nothing in it.");

        Assert.AreEqual((40, 15), _world.Balance(Character), "No coins or Station Cash, in or out.");
        Assert.AreEqual(0, ItemRows(), "No deed or anything else in inventory.");
    }

    [TestMethod]
    public void AnotherLogin_ChangesNothing()
    {
        Assert.AreEqual(StarterHouseResult.Granted, Login());
        var apartment = _world.HousesOf(_world.Account).Single().Id;

        Assert.AreEqual(StarterHouseResult.AlreadyOwned, Login());
        Assert.AreEqual(StarterHouseResult.AlreadyOwned, Login());

        Assert.AreEqual(apartment, _world.HousesOf(_world.Account).Single().Id);
    }

    /// <summary>It's the account's: another character on it gets none, and has the first one's.</summary>
    [TestMethod]
    public void AnAltOnTheAccount_GetsNone_AndHasTheFirst()
    {
        _world.AddCharacter(_world.Account, Alt);

        Assert.AreEqual(StarterHouseResult.Granted, Login());
        Assert.AreEqual(StarterHouseResult.AlreadyOwned, Login(Alt, _world.Account));

        var apartment = _world.HousesOf(_world.Account).Single();
        Assert.AreEqual(Character, apartment.CharacterId);

        var altsHouses = _world.Houses.GetOwnedHouses(Alt);
        Assert.AreEqual(apartment.Id, altsHouses.Single().Id, "The alt's houses are the account's.");
    }

    [TestMethod]
    public void AnAccountThatBoughtTheApartment_GetsNone()
    {
        _world.AddCharacter(_world.Account, Alt);
        var bought = _world.AddHouse(Alt, HouseWorld.Apartment);

        Assert.AreEqual(StarterHouseResult.AlreadyOwned, Login());

        Assert.AreEqual(bought, _world.HousesOf(_world.Account).Single().Id);
    }

    /// <summary>Nate's decision 1: an Apartment for every account without one, whatever other lots it has.</summary>
    [TestMethod]
    public void AnAccountWithOtherLotsOnly_GetsOne()
    {
        var seaside = _world.AddHouse(Character, HouseWorld.Seaside);

        Assert.AreEqual(StarterHouseResult.Granted, Login());

        var houses = _world.HousesOf(_world.Account);
        Assert.AreEqual(2, houses.Count);
        Assert.AreEqual(seaside, houses[0].Id, "The Seaside Lot is untouched.");
        Assert.AreEqual(HouseWorld.Seaside, houses[0].ZoneDefinitionId);
        Assert.AreEqual(HouseWorld.Apartment, houses[1].ZoneDefinitionId);
    }

    [TestMethod]
    public void OtherAccounts_AreUntouched_AndGetTheirOwn()
    {
        var neighbour = _world.AddAccount();
        _world.AddCharacter(neighbour, Stranger, stationCash: 5, coins: 6);

        Assert.AreEqual(StarterHouseResult.Granted, Login());

        Assert.AreEqual(0, _world.HousesOf(neighbour).Count);
        Assert.AreEqual((5, 6), _world.Balance(Stranger));

        Assert.AreEqual(StarterHouseResult.Granted, Login(Stranger, neighbour), "Another account's Apartment is no bar.");

        Assert.AreEqual(Stranger, _world.HousesOf(neighbour).Single().CharacterId);
        Assert.AreEqual(Character, _world.HousesOf(_world.Account).Single().CharacterId);
    }

    /// <summary>
    /// Deleting the character who holds it takes the Apartment and its build with it (design section 7), and pays
    /// nobody anything. The account's next login, on another character, brings a new one, as empty as the first was: an
    /// Apartment is worth nothing, so no record of the first is needed.
    /// </summary>
    [TestMethod]
    public void DeletingTheHolder_PaysNothing_AndTheNextLoginBringsAnEmptyApartment()
    {
        _world.AddCharacter(_world.Account, Alt, stationCash: 30, coins: 12);
        _world.SetBalance(Character, stationCash: 40, coins: 15);

        Assert.AreEqual(StarterHouseResult.Granted, Login());
        var first = _world.HousesOf(_world.Account).Single().Id;

        using (var dbContext = _world.Quests.CreateDbContext())
        {
            dbContext.HouseFixtures.AddRange(
                new DbHouseFixture { HouseId = first, ItemDefinitionId = 36340, IsCreative = true, PlacedByCharacterId = Character },
                new DbHouseFixture { HouseId = first, ItemDefinitionId = 36340, IsCreative = true, PlacedByCharacterId = Alt });
            dbContext.SaveChanges();
        }

        // As CharacterDeleteRequestHandler deletes a character: its friends and ignores, then the character.
        using (var dbContext = _world.Quests.CreateDbContext())
        {
            var holder = dbContext.Characters.Single(character => character.Id == Character);

            dbContext.Friends.RemoveRange(dbContext.Friends.Where(friend => friend.CharacterId == Character || friend.FriendCharacterId == Character));
            dbContext.Ignores.RemoveRange(dbContext.Ignores.Where(ignore => ignore.CharacterId == Character || ignore.IgnoreCharacterId == Character));
            dbContext.Remove(holder);
            dbContext.SaveChanges();
        }

        Assert.AreEqual(0, _world.HousesOf(_world.Account).Count, "The Apartment went with its holder.");

        using (var dbContext = _world.Quests.CreateDbContext())
            Assert.AreEqual(0, dbContext.HouseFixtures.Count(), "Its build went with it.");

        Assert.AreEqual((30, 12), _world.Balance(Alt), "Nobody was paid for it.");
        Assert.AreEqual(0, ItemRows());

        Assert.AreEqual(StarterHouseResult.Granted, Login(Alt, _world.Account));

        var second = _world.HousesOf(_world.Account).Single();
        Assert.AreNotEqual(first, second.Id);
        Assert.AreEqual(Alt, second.CharacterId);
        Assert.AreEqual(0, second.Fixtures.Count, "The new one is empty.");
        Assert.AreEqual((30, 12), _world.Balance(Alt));
        Assert.AreEqual(0, ItemRows());

        Assert.AreEqual(StarterHouseResult.AlreadyOwned, Login(Alt, _world.Account), "It's given once.");
    }

    [TestMethod]
    [DataRow(0, DisplayName = "0, off")]
    [DataRow(1, DisplayName = "1, the world zone")]
    [DataRow(424242, DisplayName = "no zone at all")]
    public void TheSettingOff_GivesNothing(int zoneId)
    {
        _world.Options.StarterHouseZoneId = zoneId;

        Assert.AreEqual(StarterHouseResult.Off, Login());
        Assert.AreEqual(StarterHouseResult.Off, Login());

        Assert.AreEqual(0, _world.HousesOf(_world.Account).Count);

        var warnings = _world.Logged.Entries.Count(entry => entry.Level == LogLevel.Warning);
        Assert.AreEqual(zoneId == 0 ? 0 : 1, warnings, "A setting that names no lot is logged, once; 0 is simply off.");
    }

    [TestMethod]
    public void AnotherLotInTheSetting_IsTheOneGiven()
    {
        _world.Options.StarterHouseZoneId = HouseWorld.Seaside;

        Assert.AreEqual(StarterHouseResult.Granted, Login());

        Assert.AreEqual(HouseWorld.Seaside, _world.HousesOf(_world.Account).Single().ZoneDefinitionId);
    }

    /// <summary>The Apartment is every account's, so it never costs Station Cash, even when lots do.</summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(500)]
    public void WithLotsNotFree_TheApartmentStillIs(int stationCash)
    {
        _world.Options.FreeLots = false;
        _world.SetBalance(Character, stationCash, coins: 0);

        Assert.AreEqual(StarterHouseResult.Granted, Login());

        Assert.AreEqual((stationCash, 0), _world.Balance(Character));
        Assert.AreEqual(1, _world.HousesOf(_world.Account).Count);
    }

    [TestMethod]
    public void AnUnknownCharacter_GetsNothing_AndTheLoginGoesOn()
    {
        Assert.AreEqual(StarterHouseResult.Failed, Login(424242, _world.Account));

        Assert.AreEqual(0, _world.HousesOf(_world.Account).Count);
    }

    /// <summary>
    /// Nate's decision 5: while every account is given the Apartment, the Marketplace leaves its bundle off the Lots &amp;
    /// Farms shelf (<see cref="Helpers.MarketplaceShelf"/>, checked on the shipped store by
    /// <see cref="HousingLotZoneTests"/>) and refuses an order for it at no charge (the order handler), as buying it could
    /// only fail. With the grant off, or set to no lot the server serves, it's back on the shelf like any lot. Another lot
    /// in the setting is the one left off, and only that one.
    /// </summary>
    [TestMethod]
    public void TheMarketplace_LeavesOffOnlyTheLotEveryAccountIsGiven()
    {
        var zones = _world.Quests.Resources.Zones;

        Assert.IsTrue(zones.TryGetValue(HouseWorld.Apartment, out var apartment));
        Assert.IsTrue(zones.TryGetValue(HouseWorld.Seaside, out var seaside));

        Assert.AreEqual(900224, ((HousingZoneDefinition)apartment).StoreBundleId, "The Apartment's Marketplace bundle.");
        Assert.AreEqual(900224, StarterHouse.BundleLeftOffTheShelf(new HousingOptions(), zones));

        foreach (var off in new[] { 0, 1, 424242 })
            Assert.IsNull(StarterHouse.BundleLeftOffTheShelf(new HousingOptions { StarterHouseZoneId = off }, zones), $"Setting {off}.");

        Assert.AreEqual(((HousingZoneDefinition)seaside).StoreBundleId,
            StarterHouse.BundleLeftOffTheShelf(new HousingOptions { StarterHouseZoneId = HouseWorld.Seaside }, zones));
    }

    /// <summary>
    /// The chat line on a grant goes to a character who has quests; one with none yet is sent to Samantha by the welcome
    /// line instead, and gets nothing more to read.
    /// </summary>
    [TestMethod]
    public void TheLineOnAGrant_IsForACharacterWithQuests()
    {
        Assert.IsTrue(_world.Quests.Resources.Zones.TryGetValue(HouseWorld.Apartment, out var zone));
        var apartment = (HousingZoneDefinition)zone;

        Assert.IsNull(StarterHouse.LineOnGrant(_world.Player, apartment));

        _world.Player.Quests.AddCompleted(2563);

        var line = StarterHouse.LineOnGrant(_world.Player, apartment);

        Assert.IsNotNull(line);
        StringAssert.Contains(line, "free Apartment");
        StringAssert.Contains(line, "My Houses");
        StringAssert.Contains(line, "!house enter apartment");
    }

    /// <summary>
    /// The shipped setting is the Apartment (Nate, 1 November): <c>Houses.json</c> entry 24, whose zone is 1024, a lot the
    /// server serves. The setting's own default agrees, for a config without it.
    /// </summary>
    [TestMethod]
    public void TheShippedSetting_IsTheApartment()
    {
        var gatewayJson = JsonNode.Parse(File.ReadAllText(FindSourceFile("Sanctuary.Gateway", "gateway.json")))!;
        var shipped = (int)gatewayJson["Housing"]!["StarterHouseZoneId"]!;

        Assert.AreEqual(HouseWorld.Apartment, shipped);
        Assert.AreEqual(shipped, new HousingOptions().StarterHouseZoneId);

        var lot = StarterHouse.Lot(new HousingOptions { StarterHouseZoneId = shipped }, _world.Quests.Resources.Zones);
        Assert.IsNotNull(lot);
        Assert.AreEqual("hsg_hum_condo", lot.Name);

        var houses = new HouseDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(houses.Load(FindSourceFile("Resources", "Houses.json")));

        var entry = houses.Values.Single(house => house.ZoneId == shipped);
        Assert.AreEqual(24, entry.Id);
        Assert.AreEqual(420151, entry.NameId);
        Assert.AreEqual(entry.NameId, lot.NameId);
    }

    private static string FindSourceFile(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);

            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"src/{string.Join('/', relativePath)} not found above the test output.");
    }
}
