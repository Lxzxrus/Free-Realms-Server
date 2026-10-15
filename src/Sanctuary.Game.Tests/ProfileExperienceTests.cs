using System;
using System.IO;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Stars paid to a job are saved on that job's row, so a relog brings them back; and the packets that show them use
/// the layout from upstream OSFR PR #120.
/// </summary>
[TestClass]
public sealed class ProfileExperienceTests
{
    private const ulong CharacterId = 7;
    private const ulong OtherCharacterId = 8;

    private const int Adventurer = 1;
    private const int Brawler = 2;
    private const int Medic = 11;

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

        foreach (var characterId in new[] { CharacterId, OtherCharacterId })
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

            if (characterId == CharacterId)
                character.Profiles.Add(new DbProfile { Id = Brawler, Level = 20 });

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

    /// <summary>A fresh context, the way the login query reads the character back.</summary>
    private DbProfile ReadProfile(ulong characterId, int profileId)
    {
        using var dbContext = CreateDbContext();

        return dbContext.Characters
            .AsNoTracking()
            .Include(character => character.Profiles)
            .Single(character => character.Id == characterId)
            .Profiles.Single(profile => profile.Id == profileId);
    }

    private bool Grant(ulong characterId, int profileId, int amount, out ExperienceGain gain)
    {
        using var dbContext = CreateDbContext();

        if (!RewardHelper.TryAddExperience(_curve, dbContext, characterId, profileId, amount, out gain))
            return false;

        Assert.AreEqual(1, dbContext.SaveChanges());
        return true;
    }

    [TestMethod]
    public void Stars_AreSavedOnTheJobAndComeBackOnRelog()
    {
        Assert.IsTrue(Grant(CharacterId, Adventurer, 60, out var gain));
        Assert.AreEqual(new ExperienceGain(Adventurer, 60, OldLevel: 1, Level: 1, LevelXp: 60), gain);
        Assert.IsFalse(gain.LeveledUp);

        var saved = ReadProfile(CharacterId, Adventurer);
        Assert.AreEqual(1, saved.Level);
        Assert.AreEqual(60, saved.LevelXP);

        // What the login sends for that row: the level bar 60% through level 1 (100 stars).
        var profile = new ClientPcProfile { Id = Adventurer };
        RewardHelper.SetRank(_curve, profile, saved.Level, saved.LevelXP);
        Assert.AreEqual(1, profile.Rank);
        Assert.AreEqual(60, profile.RankPercent);
        Assert.AreEqual(60, profile.StarsEarned);
    }

    [TestMethod]
    public void LevelUps_AreSavedAndAddUpAcrossGrants()
    {
        // 100 finishes level 1 and 150 finishes level 2, so 250 is two levels with nothing over.
        Assert.IsTrue(Grant(CharacterId, Adventurer, 250, out var gain));
        Assert.AreEqual(new ExperienceGain(Adventurer, 250, OldLevel: 1, Level: 3, LevelXp: 0), gain);
        Assert.IsTrue(gain.LeveledUp);

        Assert.IsTrue(Grant(CharacterId, Adventurer, 230, out gain));
        Assert.AreEqual(new ExperienceGain(Adventurer, 230, OldLevel: 3, Level: 4, LevelXp: 30), gain);

        var saved = ReadProfile(CharacterId, Adventurer);
        Assert.AreEqual(4, saved.Level);
        Assert.AreEqual(30, saved.LevelXP);
    }

    [TestMethod]
    public void Stars_ChangeOnlyThatCharactersJob()
    {
        Assert.IsTrue(Grant(CharacterId, Adventurer, 120, out _));

        Assert.AreEqual(2, ReadProfile(CharacterId, Adventurer).Level);

        var otherJob = ReadProfile(CharacterId, Brawler);
        Assert.AreEqual(20, otherJob.Level);
        Assert.AreEqual(0, otherJob.LevelXP);

        var otherCharacter = ReadProfile(OtherCharacterId, Adventurer);
        Assert.AreEqual(1, otherCharacter.Level);
        Assert.AreEqual(0, otherCharacter.LevelXP);
    }

    [TestMethod]
    public void NoStars_ForAJobTheCharacterDoesNotHave()
    {
        Assert.IsFalse(Grant(CharacterId, Medic, 50, out _));
        Assert.IsFalse(Grant(OtherCharacterId, Brawler, 50, out _), "Another character's job isn't this one's.");

        using var dbContext = CreateDbContext();
        Assert.IsFalse(dbContext.Profiles.Any(profile => profile.Id == Medic));
    }

    [TestMethod]
    public void NoStars_AtTheTopLevel()
    {
        Assert.IsFalse(Grant(CharacterId, Brawler, 500, out _));

        var saved = ReadProfile(CharacterId, Brawler);
        Assert.AreEqual(20, saved.Level);
        Assert.AreEqual(0, saved.LevelXP);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-100)]
    public void NoStars_ForNothingOrLess(int amount)
    {
        Assert.IsFalse(Grant(CharacterId, Adventurer, amount, out _));
        Assert.AreEqual(0, ReadProfile(CharacterId, Adventurer).LevelXP);
    }

    [TestMethod]
    public void NoStars_WithoutALoadedCurve()
    {
        using var dbContext = CreateDbContext();

        var unloaded = new RankLevelDefinitionCollection(NullLogger.Instance);

        Assert.IsFalse(RewardHelper.TryAddExperience(unloaded, dbContext, CharacterId, Adventurer, 50, out _));
    }

    /// <summary>
    /// Opcode 38/14: the job, then the same five fields as the rank block of the profile the login sends.
    /// </summary>
    [TestMethod]
    public void ExperiencePacket_IsTheJobThenItsRankBlock()
    {
        var profile = new ClientPcProfile { Id = Adventurer };
        RewardHelper.SetRank(_curve, profile, 3, 50);

        using var expected = new PacketWriter();
        expected.Write((short)38);
        expected.Write((short)14);
        expected.Write(Adventurer);
        expected.Write(3);   // rank
        expected.Write(25);  // percent: 50 of level 3's 200 stars
        expected.Write(0);   // stars available
        expected.Write(50);  // stars earned in the level
        expected.Write(0);

        CollectionAssert.AreEqual(expected.Buffer, RewardHelper.CreateExperiencePacket(profile).Serialize());
    }

    [TestMethod]
    public void RankPacket_IsTheJobAndItsNewLevel()
    {
        var profile = new ClientPcProfile { Id = Adventurer };
        RewardHelper.SetRank(_curve, profile, 5, 0);

        using var expected = new PacketWriter();
        expected.Write((short)38);
        expected.Write((short)18);
        expected.Write(Adventurer);
        expected.Write(5);

        CollectionAssert.AreEqual(expected.Buffer, RewardHelper.CreateRankPacket(profile).Serialize());
    }

    /// <summary>
    /// The reward bundle's first two ints are the coins and the stars it shows (PR #120's names for what used to be
    /// <c>Unknown1</c> and <c>RewardKind</c>); the rename changes no bytes.
    /// </summary>
    [TestMethod]
    public void RewardBundle_CarriesCoinsThenStars()
    {
        var bundle = new RewardBundleBase { Success = false, Coins = 30, Experience = 60 };

        using var writer = new PacketWriter();
        bundle.Serialize(writer);

        var reader = new PacketReader(writer.Buffer);
        Assert.IsTrue(reader.TryRead(out bool success));
        Assert.IsFalse(success);
        Assert.IsTrue(reader.TryRead(out int coins));
        Assert.AreEqual(30, coins);
        Assert.IsTrue(reader.TryRead(out int experience));
        Assert.AreEqual(60, experience);
    }

    /// <summary>
    /// New jobs start at level 1 so they have a bar to fill. The staff jobs start at the top, whichever way the row
    /// is made: at a staff login, or with every job unlocked at character creation (which includes them).
    /// </summary>
    [TestMethod]
    [DataRow(Adventurer, 1)]
    [DataRow(Medic, 1)]
    [DataRow((int)SpecialProfileIds.Referee, 20)]
    [DataRow((int)SpecialProfileIds.Enforcer, 20)]
    public void NewJobs_StartAtLevelOneAndStaffJobsAtTheTop(int profileId, int level)
    {
        Assert.AreEqual(level, ProfileHelper.StartingLevel(profileId));
    }
}
