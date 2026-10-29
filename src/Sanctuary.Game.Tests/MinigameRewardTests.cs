using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Minigames.MatchThree;
using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A won match-three game's stars go to the job its data names, and only for a player who has that job: anyone else
/// gets the coins alone, and no job is made for them. <see cref="MinigameReward.Pay"/> pays the stars through
/// <see cref="RewardHelper.TryGrantExperience"/>, as Cooking does; this checks its saving step,
/// <see cref="RewardHelper.TryAddExperience"/>, with every shipped game's reward (the packets after it need a live
/// connection).
/// </summary>
[TestClass]
public sealed class MinigameRewardTests
{
    private const ulong EveryJobCharacterId = 7;
    private const ulong AdventurerCharacterId = 8;

    private const int Adventurer = 1;

    private static RankLevelDefinitionCollection _curve = null!;
    private static List<MatchThreeGameDefinition> _games = null!;

    private SqliteConnection _connection = null!;

    [ClassInitialize]
    public static void LoadResources(TestContext _)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var resources = Path.Combine(directory.FullName, "Resources");

            if (!File.Exists(Path.Combine(resources, "MatchThree.json")))
                continue;

            _curve = new RankLevelDefinitionCollection(NullLogger.Instance);
            Assert.IsTrue(_curve.Load(Path.Combine(resources, "RankLevels.json")));

            var file = Path.Combine(resources, "MatchThree.json");
            Assert.IsTrue(MatchThreeDefinitions.TryLoad(file, NullLogger.Instance, out var matchThree));
            _games = matchThree.Games;
            return;
        }

        Assert.Fail("src/Resources/MatchThree.json not found above the test output.");
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

        foreach (var characterId in new[] { EveryJobCharacterId, AdventurerCharacterId })
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

            // One character has every job the games pay stars to; the other is new, an Adventurer only.
            if (characterId == EveryJobCharacterId)
                foreach (var profileId in _games.Select(game => game.RewardProfileId).Distinct())
                    character.Profiles.Add(new DbProfile { Id = profileId, Level = 1 });

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

    /// <summary>A win's stars, saved the way <see cref="RewardHelper.TryGrantExperience"/> saves them.</summary>
    private bool PayStars(ulong characterId, MinigameReward reward)
    {
        using var dbContext = CreateDbContext();

        if (!RewardHelper.TryAddExperience(_curve, dbContext, characterId, reward.ProfileId, reward.Experience, out _))
            return false;

        Assert.AreEqual(1, dbContext.SaveChanges());
        return true;
    }

    /// <summary>The character's jobs, read back as the login reads them: level and stars into it.</summary>
    private Dictionary<int, (int Level, int LevelXp)> Jobs(ulong characterId)
    {
        using var dbContext = CreateDbContext();

        return dbContext.Profiles.AsNoTracking()
            .Where(profile => profile.CharacterId == characterId)
            .ToDictionary(profile => profile.Id, profile => (profile.Level, profile.LevelXP));
    }

    [TestMethod]
    public void AWinsStars_GoToTheGamesJob()
    {
        foreach (var game in _games)
            Assert.IsTrue(PayStars(EveryJobCharacterId, game.Reward), $"{game.Name}: stars for job {game.RewardProfileId}.");

        var jobs = Jobs(EveryJobCharacterId);

        // Each job from level 1, by the stars of the games that pay it.
        foreach (var job in _games.GroupBy(game => game.RewardProfileId))
            Assert.AreEqual(_curve.Accrue(1, 0, job.Sum(game => game.Experience)), jobs[job.Key], $"Job {job.Key}.");

        Assert.AreEqual((1, 0), jobs[Adventurer], "Not the job the player is wearing: the game's.");
    }

    [TestMethod]
    public void WithoutTheJob_NoStars_AndNoJobIsMade()
    {
        foreach (var game in _games)
            Assert.IsFalse(PayStars(AdventurerCharacterId, game.Reward), $"{game.Name}: no job {game.RewardProfileId} to pay.");

        var jobs = Jobs(AdventurerCharacterId);

        CollectionAssert.AreEqual(new[] { Adventurer }, jobs.Keys.ToArray());
        Assert.AreEqual((1, 0), jobs[Adventurer]);
    }
}
