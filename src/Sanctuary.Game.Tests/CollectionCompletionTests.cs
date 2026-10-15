using System;
using System.IO;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Collections;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A finished collection pays once per character (threat model S6): the Briarwood mushrooms count the items a
/// character holds, so selling one and picking it up again finishes the collection again, and that must not pay
/// its stars again.
/// </summary>
[TestClass]
public sealed class CollectionCompletionTests
{
    private const ulong MedicCharacterId = 7;
    private const ulong AdventurerCharacterId = 8;

    private const int Adventurer = 1;
    private const int Medic = 11;

    private static readonly CollectionDefinition Briarwood = new()
    {
        Id = 10,
        RewardCoins = 9,
        RewardExperience = 50,
        RewardProfileId = Medic
    };

    private static readonly CollectionDefinition ItemsOnly = new()
    {
        Id = 9001,
        RewardItemIds = [1180]
    };

    private static RankLevelDefinitionCollection _curve = null!;

    private SqliteConnection _connection = null!;

    [ClassInitialize]
    public static void LoadCurve(TestContext _)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", "RankLevels.json");

            if (!File.Exists(candidate))
                continue;

            _curve = new RankLevelDefinitionCollection(NullLogger.Instance);
            Assert.IsTrue(_curve.Load(candidate));
            return;
        }

        Assert.Fail("src/Resources/RankLevels.json not found above the test output.");
    }

    [TestInitialize]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var dbContext = CreateDbContext();
        dbContext.Database.Migrate();

        var user = new DbUser
        {
            Username = "player",
            Password = string.Empty
        };

        foreach (var characterId in new[] { MedicCharacterId, AdventurerCharacterId })
        {
            var character = new DbCharacter
            {
                Id = characterId,
                FirstName = $"Character{characterId}",
                Head = string.Empty,
                HeadId = 0,
                Hair = string.Empty,
                HairId = 0,
                SkinTone = string.Empty,
                SkinToneId = 0,
                ActiveProfileId = Adventurer
            };

            character.Profiles.Add(new DbProfile { Id = Adventurer, Level = 1 });

            if (characterId == MedicCharacterId)
                character.Profiles.Add(new DbProfile { Id = Medic, Level = 1 });

            user.Characters.Add(character);
        }

        dbContext.Users.Add(user);
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

    /// <summary>One finish, saved the way the interact handler saves it, in its own context.</summary>
    private bool Finish(ulong characterId, CollectionDefinition definition, out ExperienceGain? experience)
    {
        using var dbContext = CreateDbContext();

        var paid = CollectionCompletion.TryRecord(dbContext, _curve, characterId, definition, out experience);
        dbContext.SaveChanges();

        return paid;
    }

    private DbProfile ReadProfile(ulong characterId, int profileId)
    {
        using var dbContext = CreateDbContext();

        return dbContext.Profiles.AsNoTracking()
            .Single(profile => profile.CharacterId == characterId && profile.Id == profileId);
    }

    [TestMethod]
    public void FirstFinish_PaysItsStarsToTheJobItNames()
    {
        Assert.IsTrue(Finish(MedicCharacterId, Briarwood, out var experience));
        Assert.AreEqual(new ExperienceGain(Medic, 50, OldLevel: 1, Level: 1, LevelXp: 50), experience);

        Assert.AreEqual(50, ReadProfile(MedicCharacterId, Medic).LevelXP);
        Assert.AreEqual(0, ReadProfile(MedicCharacterId, Adventurer).LevelXP, "Only the Medic job is paid.");
    }

    [TestMethod]
    public void FinishingAgain_PaysNothing()
    {
        Assert.IsTrue(Finish(MedicCharacterId, Briarwood, out _));

        // Sold a mushroom, picked it up again: the collection is finished again.
        Assert.IsFalse(Finish(MedicCharacterId, Briarwood, out var experience));
        Assert.IsNull(experience);
        Assert.IsFalse(Finish(MedicCharacterId, Briarwood, out _));

        Assert.AreEqual(50, ReadProfile(MedicCharacterId, Medic).LevelXP);

        using var dbContext = CreateDbContext();
        Assert.AreEqual(1, dbContext.CharacterCompletedCollections.Count());
    }

    [TestMethod]
    public void EachCharacterAndCollection_PaysOnce()
    {
        Assert.IsTrue(Finish(MedicCharacterId, Briarwood, out _));
        Assert.IsTrue(Finish(AdventurerCharacterId, Briarwood, out _), "Another character's finish is its own.");
        Assert.IsTrue(Finish(MedicCharacterId, ItemsOnly, out _), "Another collection is its own.");
        Assert.IsFalse(Finish(AdventurerCharacterId, Briarwood, out _));
    }

    /// <summary>
    /// Most characters don't have the Medic job the mushrooms pay. The finish still counts (its items, when it has
    /// any, are paid), but there is nowhere to put the stars, so none are paid, now or later.
    /// </summary>
    [TestMethod]
    public void WithoutTheJob_TheFinishCountsButNoStarsArePaid()
    {
        Assert.IsTrue(Finish(AdventurerCharacterId, Briarwood, out var experience));
        Assert.IsNull(experience);
        Assert.AreEqual(0, ReadProfile(AdventurerCharacterId, Adventurer).LevelXP);

        Assert.IsFalse(Finish(AdventurerCharacterId, Briarwood, out _));
    }

    [TestMethod]
    public void ACollectionWithoutStars_StillPaysOnce()
    {
        Assert.IsTrue(Finish(MedicCharacterId, ItemsOnly, out var experience));
        Assert.IsNull(experience);
        Assert.IsFalse(Finish(MedicCharacterId, ItemsOnly, out _));
    }

    [TestMethod]
    public void ASecondFinishInTheSameSave_PaysNothing()
    {
        using var dbContext = CreateDbContext();

        Assert.IsTrue(CollectionCompletion.TryRecord(dbContext, _curve, MedicCharacterId, Briarwood, out _));
        Assert.IsFalse(CollectionCompletion.TryRecord(dbContext, _curve, MedicCharacterId, Briarwood, out var experience));
        Assert.IsNull(experience);

        dbContext.SaveChanges();

        Assert.AreEqual(50, ReadProfile(MedicCharacterId, Medic).LevelXP);
    }

    [TestMethod]
    public void TheShippedMushrooms_PayFiftyMedicStars()
    {
        string? path = null;

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", "Collections.json");

            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
        }

        Assert.IsNotNull(path);

        var collections = new CollectionDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(collections.Load(path));

        var paying = collections.Values.Where(collection => collection.RewardExperience > 0).ToArray();

        Assert.HasCount(1, paying, "Only the Briarwood mushrooms pay stars today.");
        Assert.AreEqual(Briarwood.Id, paying[0].Id);
        Assert.AreEqual(Briarwood.RewardExperience, paying[0].RewardExperience);
        Assert.AreEqual(Briarwood.RewardProfileId, paying[0].RewardProfileId);
    }
}
