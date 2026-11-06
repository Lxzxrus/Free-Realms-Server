using System;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Housing;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A deleted character's houses move to another character on its account (<see cref="HouseHandover"/>). Characters are
/// deleted here by the Login server's own delete (<see cref="CharacterDeletion"/>): the move, then the character, in one
/// transaction, with the database's cascade on <c>Houses.CharacterId</c> taking whatever is left on it.
/// </summary>
[TestClass]
public sealed class HouseHandoverTests
{
    // Wednesday of the week that starts on Monday 12 October 2026.
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RankedSince = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);

    // The builder's account has three characters. Eldest was made first, then Gone, then Younger; ids needn't follow,
    // so Eldest's is the highest. The neighbour's character is older than all of them, on another account.
    private const ulong Builder = 1, Gone = 10, Younger = 11, Eldest = 12;
    private const ulong Neighbour = 2, Stranger = 20;
    private const ulong Voters = 3, Voter = 30;
    private const ulong Loner = 4, LastCharacter = 40;

    private const int Seaside = 1, Blackspore = 2, Snowhill = 3;

    private static readonly CompetitionOptions Options = new();

    private SqliteConnection _connection = null!;

    [TestInitialize]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var dbContext = CreateDbContext();
        dbContext.Database.Migrate();

        var longAgo = Now.AddDays(-60);

        dbContext.Users.AddRange(
            User(Builder, longAgo,
                Character(Gone, longAgo.AddDays(2)),
                Character(Younger, longAgo.AddDays(3)),
                Character(Eldest, longAgo.AddDays(1))),
            User(Neighbour, longAgo, Character(Stranger, longAgo.AddDays(-30))),
            User(Voters, longAgo, Character(Voter, longAgo, playTime: 600)),
            User(Loner, longAgo, Character(LastCharacter, longAgo)));

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

    private static DbUser User(ulong id, DateTimeOffset created, params DbCharacter[] characters)
    {
        var user = new DbUser { Id = id, Username = $"user{id}", Password = string.Empty, Created = created };

        foreach (var character in characters)
            user.Characters.Add(character);

        return user;
    }

    private static DbCharacter Character(ulong id, DateTimeOffset created, int playTime = 0) => new()
    {
        Id = id,
        FirstName = $"Character{id}",
        Head = string.Empty,
        HeadId = 0,
        Hair = string.Empty,
        HairId = 0,
        SkinTone = string.Empty,
        SkinToneId = 0,
        Created = created,
        PlayTime = playTime
    };

    private void AddHouses(params DbHouse[] houses)
    {
        using var dbContext = CreateDbContext();
        dbContext.Houses.AddRange(houses);
        dbContext.SaveChanges();
    }

    private static DbHouse House(ulong id, ulong characterId, int lot, int fixtures = 0)
    {
        var house = new DbHouse { Id = id, CharacterId = characterId, ZoneDefinitionId = lot, FurnitureScore = fixtures };

        for (var index = 0; index < fixtures; index++)
            house.Fixtures.Add(new DbHouseFixture { ItemDefinitionId = 22940 + index, PlacedByCharacterId = characterId });

        return house;
    }

    /// <summary>Deletes the character as the Login server does, with <see cref="CharacterDeletion.Delete"/>.</summary>
    private HouseHandoverResult Delete(ulong characterId)
    {
        using var dbContext = CreateDbContext();
        var character = dbContext.Characters.Single(candidate => candidate.Id == characterId);

        var handover = CharacterDeletion.Delete(dbContext, character);

        Assert.IsNotNull(handover, "The character was deleted.");
        return handover;
    }

    private DbHouse? Find(ulong houseId)
    {
        using var dbContext = CreateDbContext();
        return dbContext.Houses
            .AsNoTracking()
            .Include(house => house.Fixtures)
            .SingleOrDefault(house => house.Id == houseId);
    }

    private ulong? HolderOf(ulong houseId) => Find(houseId)?.CharacterId;

    private PlotVoteOutcome Vote(ulong characterId, ulong houseId, int stars)
    {
        using var dbContext = CreateDbContext();
        return PlotVoting.Cast(dbContext, characterId, houseId, stars, Now, Options);
    }

    private PlotStanding StandingOf(ulong houseId)
    {
        using var dbContext = CreateDbContext();
        return PlotVoting.Standings(dbContext, Options.WeekContaining(Now), Options.MinimumVotes).For(houseId);
    }

    [TestMethod]
    public void Houses_MoveToTheAccountsOldestRemainingCharacter()
    {
        AddHouses(House(100, Gone, Seaside), House(101, Gone, Blackspore));

        var handover = Delete(Gone);

        Assert.AreEqual(Eldest, HolderOf(100), "Eldest was made first, though its id is the highest.");
        Assert.AreEqual(Eldest, HolderOf(101));
        CollectionAssert.AreEqual(
            new[] { new HouseMove(100, Seaside, Eldest), new HouseMove(101, Blackspore, Eldest) },
            handover.Moved.ToArray());
        Assert.AreEqual(0, handover.Unmoved.Count);
        Assert.AreEqual(2, handover.OtherCharacters);

        using var dbContext = CreateDbContext();

        Assert.IsFalse(dbContext.Characters.Any(character => character.Id == Gone));
        Assert.IsFalse(dbContext.Houses.Any(house => house.CharacterId == Stranger),
            "An older character on another account is never given the houses.");

        // The Gateway finds a house's instance by the character holding it and the lot.
        Assert.AreEqual(100UL, dbContext.Houses.Single(house => house.CharacterId == Eldest && house.ZoneDefinitionId == Seaside).Id);
        CollectionAssert.AreEquivalent(
            new ulong[] { 100, 101 },
            HouseOwnership.OwnedByAccountOf(dbContext, Younger).Select(house => house.Id).ToArray(),
            "Every character on the account still has them.");
    }

    [TestMethod]
    public void ALotTheOldestHas_GoesToTheNextOldest()
    {
        AddHouses(House(100, Gone, Seaside), House(101, Gone, Blackspore), House(110, Eldest, Seaside, fixtures: 2));

        var handover = Delete(Gone);

        Assert.AreEqual(Younger, HolderOf(100));
        Assert.AreEqual(Eldest, HolderOf(101));
        Assert.AreEqual(Eldest, HolderOf(110));
        Assert.AreEqual(2, Find(110)!.Fixtures.Count, "Eldest's own Seaside is untouched.");
        Assert.AreEqual(2, handover.Moved.Count);
        Assert.AreEqual(0, handover.Unmoved.Count);
    }

    /// <summary>
    /// An account keeps one house of each lot (section 8). Two characters could each buy one before task 35, so the
    /// deleted character's copy can't move when every other character has the lot; it goes with the character, as every
    /// house did before. The copies the remaining characters hold are untouched.
    /// </summary>
    [TestMethod]
    public void ALotEveryOtherCharacterHas_GoesWithTheCharacter()
    {
        AddHouses(
            House(100, Gone, Seaside, fixtures: 3),
            House(101, Gone, Blackspore),
            House(110, Eldest, Seaside, fixtures: 2),
            House(111, Younger, Seaside, fixtures: 1));

        var handover = Delete(Gone);

        Assert.IsNull(Find(100), "The duplicate went with its character.");
        Assert.AreEqual(Eldest, HolderOf(101), "Its other house still moved.");
        Assert.AreEqual(Eldest, HolderOf(110));
        Assert.AreEqual(Younger, HolderOf(111));
        Assert.AreEqual(2, Find(110)!.Fixtures.Count);
        Assert.AreEqual(1, Find(111)!.Fixtures.Count);
        CollectionAssert.AreEqual(new ulong[] { 100 }, handover.Unmoved.ToArray());
        Assert.AreEqual(2, handover.OtherCharacters);

        using var dbContext = CreateDbContext();
        Assert.AreEqual(3, dbContext.HouseFixtures.Count(), "The duplicate's fixtures went with it; no others did.");
    }

    /// <summary>
    /// The copy that goes may be the account's ranked plot. A house on a character the player isn't deleting is still
    /// never touched: the account is left with no ranked plot until it makes another, and the plot's votes are kept.
    /// </summary>
    [TestMethod]
    public void ARankedCopyEveryOtherCharacterHas_GoesToo()
    {
        AddHouses(House(100, Gone, Seaside), House(110, Eldest, Seaside, fixtures: 2), House(111, Younger, Seaside));
        AddRankedPlot(100);
        Assert.AreEqual(PlotVoteResult.Cast, Vote(Voter, 100, 5).Result);

        var handover = Delete(Gone);

        Assert.IsNull(Find(100));
        CollectionAssert.AreEqual(new ulong[] { 100 }, handover.Unmoved.ToArray());
        Assert.AreEqual(HousePlot.Personal, Find(110)!.Plot, "No other copy is made the ranked plot in its place.");
        Assert.AreEqual(HousePlot.Personal, Find(111)!.Plot);
        Assert.AreEqual(2, Find(110)!.Fixtures.Count);

        using var dbContext = CreateDbContext();
        Assert.IsNull(HousePlots.FindRankedPlot(dbContext, Builder));
        Assert.AreEqual(100UL, dbContext.PlotVotes.Single().HouseId, "The vote is kept as history.");
    }

    /// <summary>
    /// Houses follow the account from character to character, the ranked plot still ranked, until its last character
    /// takes them with it. The account itself stays.
    /// </summary>
    [TestMethod]
    public void HousesFollowTheAccount_UntilItsLastCharacter()
    {
        AddHouses(House(100, Gone, Seaside), House(110, Eldest, Blackspore), House(120, Younger, Snowhill));
        AddRankedPlot(100);

        Delete(Gone);
        Assert.AreEqual(Eldest, HolderOf(100));

        var second = Delete(Eldest);

        CollectionAssert.AreEqual(
            new[] { new HouseMove(100, Seaside, Younger), new HouseMove(110, Blackspore, Younger) },
            second.Moved.ToArray(),
            "A house that moved once moves again with the rest.");
        Assert.AreEqual(Younger, HolderOf(120));
        Assert.AreEqual(HousePlot.Ranked, Find(100)!.Plot);
        Assert.AreEqual(RankedSince, Find(100)!.RankedSince);

        var last = Delete(Younger);

        Assert.AreEqual(0, last.OtherCharacters);
        Assert.AreEqual(0, last.Moved.Count);
        CollectionAssert.AreEqual(new ulong[] { 100, 110, 120 }, last.Unmoved.ToArray());

        using var dbContext = CreateDbContext();
        Assert.IsFalse(dbContext.Houses.Any(house => house.Id == 100 || house.Id == 110 || house.Id == 120));
        Assert.IsTrue(dbContext.Users.Any(user => user.Id == Builder));
    }

    /// <summary>Characters made at the same moment: the lower id counts as older, so the choice never varies.</summary>
    [TestMethod]
    public void CharactersMadeAtOnce_TheLowerIdTakesTheHouses()
    {
        const ulong Twins = 5, First = 50, Second = 51, Third = 52;
        var made = Now.AddDays(-10);

        using (var dbContext = CreateDbContext())
        {
            dbContext.Users.Add(User(Twins, made, Character(Third, made), Character(Second, made), Character(First, made)));
            dbContext.SaveChanges();
        }

        AddHouses(House(150, Second, Seaside));

        var handover = Delete(Second);

        CollectionAssert.AreEqual(new[] { new HouseMove(150, Seaside, First) }, handover.Moved.ToArray());
    }

    /// <summary>
    /// No character is left to hold the houses, and a house is always on a character, so they go with the last one,
    /// as before. Their past votes are kept for history, like any deleted house's.
    /// </summary>
    [TestMethod]
    public void TheLastCharactersHouses_GoWithIt()
    {
        AddHouses(House(140, LastCharacter, Seaside, fixtures: 2), House(141, LastCharacter, Blackspore));
        AddRankedPlot(141);
        Assert.AreEqual(PlotVoteResult.Cast, Vote(Voter, 141, 5).Result);

        var handover = Delete(LastCharacter);

        Assert.IsNull(Find(140));
        Assert.IsNull(Find(141));
        Assert.AreEqual(0, handover.Moved.Count);
        CollectionAssert.AreEqual(new ulong[] { 140, 141 }, handover.Unmoved.ToArray());
        Assert.AreEqual(0, handover.OtherCharacters);

        using var dbContext = CreateDbContext();
        Assert.AreEqual(0, dbContext.HouseFixtures.Count());
        Assert.AreEqual(141UL, dbContext.PlotVotes.Single().HouseId, "The vote is kept as history.");
        Assert.IsFalse(PlotVoting.Standings(dbContext, Options.WeekContaining(Now), Options.MinimumVotes).Plots.ContainsKey(141));
    }

    /// <summary>
    /// The ranked plot keeps its id, which its votes are kept by, and its RankedSince, which they count from: it stays
    /// ranked, entered and scored exactly as before, and its account still can't vote on it.
    /// </summary>
    [TestMethod]
    public void ARankedPlot_StaysRankedWithItsVotes()
    {
        AddHouses(House(100, Gone, Seaside), House(110, Younger, Blackspore));
        AddRankedPlot(100);
        Assert.AreEqual(PlotVoteResult.Cast, Vote(Voter, 100, 4).Result);
        var before = StandingOf(100);

        Delete(Gone);

        var house = Find(100)!;
        Assert.AreEqual(Eldest, house.CharacterId);
        Assert.AreEqual(HousePlot.Ranked, house.Plot);
        Assert.AreEqual(RankedSince, house.RankedSince);
        Assert.IsTrue(house.IsPublished);
        Assert.AreEqual(before, StandingOf(100), "Same votes, same score.");
        Assert.AreEqual(1, before.Votes);

        Assert.AreEqual(PlotVoteResult.Unchanged, Vote(Voter, 100, 4).Result, "The voter's vote this week still counts.");
        var changed = Vote(Voter, 100, 2);
        Assert.AreEqual(PlotVoteResult.Changed, changed.Result);
        Assert.AreEqual(4, changed.PreviousStars);
        Assert.AreEqual(PlotVoteResult.OwnAccount, Vote(Eldest, 100, 5).Result);
        Assert.AreEqual(PlotVoteResult.OwnAccount, Vote(Younger, 100, 5).Result);

        using var dbContext = CreateDbContext();
        Assert.AreEqual(100UL, HousePlots.FindRankedPlot(dbContext, Builder)?.Id);

        // Still the account's one ranked plot: making another house ranked turns it back into a personal plot.
        var change = HousePlots.MakeRanked(dbContext, 110, Younger, Now);
        Assert.AreEqual(PlotChangeResult.Changed, change.Result);
        Assert.AreEqual(100UL, change.Replaced?.Id);
        Assert.AreEqual(HousePlot.Personal, Find(100)!.Plot);
    }

    [TestMethod]
    public void Fixtures_StayWithTheHouse()
    {
        var house = House(100, Gone, Seaside, fixtures: 3);
        house.Name = "Tide Pools";
        house.Description = "Shells everywhere";
        house.IsLocked = true;
        house.Fixtures.First().PlacedByCharacterId = Younger;
        AddHouses(house);

        var before = Describe(Find(100)!);

        Delete(Gone);

        var moved = Find(100)!;
        var after = Describe(moved);

        Assert.AreEqual(Eldest, moved.CharacterId);
        Assert.AreEqual(3, after.Length);
        CollectionAssert.AreEqual(before, after, "The same parts, where they were.");
        CollectionAssert.AreEquivalent(
            new[] { Younger, Gone, Gone },
            moved.Fixtures.Select(fixture => fixture.PlacedByCharacterId).ToArray(),
            "Who placed each part is history, and outlives the character.");
        Assert.AreEqual(3, moved.FurnitureScore);
        Assert.AreEqual("Tide Pools", moved.Name);
        Assert.AreEqual("Shells everywhere", moved.Description);
        Assert.IsTrue(moved.IsLocked);
    }

    /// <summary>
    /// A house loaded when it changes hands stays one instance. The zone manager keys a house's instance by the house,
    /// and looking it up under the new holder finds the same house on the same account, so whoever comes in joins the
    /// instance loaded for the deleted character. Code in the house asks who holds it now.
    /// </summary>
    [TestMethod]
    public void AMovedHouse_IsTheSameHouseUnderItsNewHolder()
    {
        AddHouses(House(100, Gone, Seaside));

        using (var dbContext = CreateDbContext())
        {
            Assert.AreEqual((100UL, Builder), HouseOwnership.FindHeld(dbContext, Gone, Seaside));
            Assert.AreEqual(Gone, HouseOwnership.FindHolder(dbContext, 100));
        }

        Delete(Gone);

        using (var dbContext = CreateDbContext())
        {
            Assert.AreEqual((100UL, Builder), HouseOwnership.FindHeld(dbContext, Eldest, Seaside));
            Assert.IsNull(HouseOwnership.FindHeld(dbContext, Gone, Seaside));
            Assert.IsNull(HouseOwnership.FindHeld(dbContext, Younger, Seaside), "Only its holder finds it by lot.");
            Assert.AreEqual(Eldest, HouseOwnership.FindHolder(dbContext, 100));
            Assert.IsNull(HouseOwnership.FindHolder(dbContext, 999));
        }
    }

    /// <summary>
    /// A lock lets the friends of the character holding the house stay, so after a move, the new holder's. The deleted
    /// character's friends are nobody's now. Every character on the account may stay, as before.
    /// </summary>
    [TestMethod]
    public void AfterAMove_ALockLetsTheNewHoldersFriendsStay()
    {
        AddHouses(House(100, Gone, Seaside));
        Delete(Gone);

        using var dbContext = CreateDbContext();
        var holder = HouseOwnership.FindHolder(dbContext, 100)!.Value;

        Assert.IsTrue(HouseAccess.MayBeInLockedHouse(Neighbour, [Friend(Eldest)], Builder, holder));
        Assert.IsFalse(HouseAccess.MayBeInLockedHouse(Neighbour, [Friend(Gone)], Builder, holder));
        Assert.IsTrue(HouseAccess.MayBeInLockedHouse(Builder, [], Builder, holder));
    }

    /// <summary>
    /// The rest of the delete doesn't get in the houses' way: a guild leader's houses move like anyone's, while its guild
    /// gets a new leader and its friend and ignore entries go, both ways.
    /// </summary>
    [TestMethod]
    public void AGuildLeaderWithFriends_StillHandsOnItsHouses()
    {
        AddHouses(House(100, Gone, Seaside));

        using (var dbContext = CreateDbContext())
        {
            dbContext.Guilds.Add(new DbGuild
            {
                Id = 1,
                Name = "Builders",
                Members =
                {
                    new DbGuildMember { Id = Gone, Role = GuildRole.Leader.Id },
                    new DbGuildMember { Id = Stranger, Role = GuildRole.Member.Id }
                }
            });
            dbContext.Friends.AddRange(
                new DbFriend { CharacterId = Gone, FriendCharacterId = Stranger },
                new DbFriend { CharacterId = Stranger, FriendCharacterId = Gone });
            dbContext.Ignores.Add(new DbIgnore { CharacterId = Voter, IgnoreCharacterId = Gone });
            dbContext.SaveChanges();

            foreach (var member in dbContext.Characters.Where(character => character.Id == Gone || character.Id == Stranger))
                member.GuildMemberId = member.Id;

            dbContext.SaveChanges();
        }

        var handover = Delete(Gone);

        CollectionAssert.AreEqual(new[] { new HouseMove(100, Seaside, Eldest) }, handover.Moved.ToArray());
        Assert.AreEqual(Eldest, HolderOf(100));

        using (var dbContext = CreateDbContext())
        {
            Assert.IsFalse(dbContext.Characters.Any(character => character.Id == Gone));
            Assert.IsFalse(dbContext.Friends.Any(), "Its friend entries went, both ways.");
            Assert.IsFalse(dbContext.Ignores.Any());
            Assert.AreEqual(GuildRole.Leader.Id, dbContext.GuildMembers.Single(member => member.Id == Stranger).Role,
                "The guild's other member leads it now.");
        }
    }

    [TestMethod]
    public void ACharacterWithNoHouses_MovesNothing()
    {
        AddHouses(House(110, Eldest, Seaside));

        var handover = Delete(Gone);

        Assert.AreEqual(0, handover.Moved.Count);
        Assert.AreEqual(0, handover.Unmoved.Count);
        Assert.AreEqual(Eldest, HolderOf(110));
    }

    private static FriendData Friend(ulong characterId) => new() { Guid = GuidHelper.GetPlayerGuid(characterId) };

    private static string[] Describe(DbHouse house)
    {
        return house.Fixtures
            .OrderBy(fixture => fixture.Id)
            .Select(fixture => $"{fixture.Id} {fixture.PlacementToken} {fixture.ItemDefinitionId} {fixture.PlacedByCharacterId}")
            .ToArray();
    }

    private void AddRankedPlot(ulong houseId)
    {
        using var dbContext = CreateDbContext();
        var house = dbContext.Houses.Single(candidate => candidate.Id == houseId);
        house.Plot = HousePlot.Ranked;
        house.RankedSince = RankedSince;
        house.IsPublished = true;
        dbContext.SaveChanges();
    }
}
