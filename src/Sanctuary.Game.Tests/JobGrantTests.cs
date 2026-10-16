using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Jobs;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A job granted while playing is saved before anything else, comes back on relog, and a second grant changes
/// nothing; its starter kit fits the character's gender.
/// </summary>
[TestClass]
public sealed class JobGrantTests
{
    private const ulong MaleCharacterId = 7;
    private const ulong FemaleCharacterId = 8;

    private const int Male = 1;
    private const int Female = 2;

    private const int Adventurer = ProfileHelper.AdventurerProfileId;
    private const int Fisherman = 137;
    private const int CardDuelist = 120;
    private const int Referee = (int)SpecialProfileIds.Referee;

    // The Adventurer's clothes, already on both characters (item rows 1 to 3).
    private const int Shirt = 1001;
    private const int Pants = 1002;
    private const int Shoes = 1003;

    // The Fisherman's kit: a hat for either, a vest each, and the Adventurer's shoes again.
    private const int Hat = 2001;
    private const int MaleVest = 2002;
    private const int FemaleVest = 2003;

    private SqliteConnection _connection = null!;
    private ResourceManager _resourceManager = null!;

    [TestInitialize]
    public void Setup()
    {
        Directory.CreateDirectory(ResourceManager.BaseDirectory);
        _resourceManager = new ResourceManager(NullLogger<ResourceManager>.Instance);
        StopWatcher(_resourceManager);

        AddItem(Shirt, slot: 3);
        AddItem(Pants, slot: 4);
        AddItem(Shoes, slot: 5);
        AddItem(Hat, slot: 1);
        AddItem(MaleVest, slot: 3, genderUsage: Male);
        AddItem(FemaleVest, slot: 3, genderUsage: Female);

        AddProfile(Adventurer);
        AddProfile(Fisherman, Hat, MaleVest, FemaleVest, Shoes);
        AddProfile(CardDuelist);
        AddProfile(Referee, Hat);

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var dbContext = CreateDbContext();
        dbContext.Database.Migrate();

        var user = new DbUser
        {
            Username = "player",
            Password = string.Empty
        };

        foreach (var (characterId, gender) in new[] { (MaleCharacterId, Male), (FemaleCharacterId, Female) })
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
                Gender = gender,
                ActiveProfileId = Adventurer
            };

            var adventurer = new DbProfile { Id = Adventurer, Level = 1 };

            foreach (var (id, definition) in new[] { (1, Shirt), (2, Pants), (3, Shoes) })
            {
                var item = new DbItem { Id = id, Definition = definition, Count = 1 };
                character.Items.Add(item);
                adventurer.Items.Add(item);
            }

            character.Profiles.Add(adventurer);
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

    [TestMethod]
    public void Grant_SavesTheJobAtLevelOneWithItsKit_AndItComesBackOnRelog()
    {
        Assert.AreEqual(JobGrantResult.Granted, Grant(MaleCharacterId, Fisherman));

        var character = ReadCharacter(MaleCharacterId);
        var fisherman = character.Profiles.Single(profile => profile.Id == Fisherman);

        Assert.AreEqual(1, fisherman.Level);
        Assert.AreEqual(0, fisherman.LevelXP);
        CollectionAssert.AreEquivalent(new[] { Hat, MaleVest, Shoes }, fisherman.Items.Select(item => item.Definition).ToArray());

        // The Adventurer is untouched.
        CollectionAssert.AreEquivalent(new[] { Shirt, Pants, Shoes },
            character.Profiles.Single(profile => profile.Id == Adventurer).Items.Select(item => item.Definition).ToArray());
    }

    [TestMethod]
    public void Grant_ReusesItemsTheCharacterOwns()
    {
        Grant(MaleCharacterId, Fisherman);

        var character = ReadCharacter(MaleCharacterId);

        Assert.AreEqual(1, character.Items.Count(item => item.Definition == Shoes), "the shoes aren't bought twice");
        Assert.AreEqual(5, character.Items.Count, "three clothes, the hat and the vest");
        Assert.AreEqual(character.Items.Count, character.Items.Select(item => item.Id).Distinct().Count());
    }

    [TestMethod]
    public void SecondGrant_ChangesNothing()
    {
        Assert.AreEqual(JobGrantResult.Granted, Grant(MaleCharacterId, Fisherman));

        var before = ReadCharacter(MaleCharacterId);

        Assert.AreEqual(JobGrantResult.AlreadyHad, Grant(MaleCharacterId, Fisherman));

        var after = ReadCharacter(MaleCharacterId);

        Assert.AreEqual(before.Profiles.Count, after.Profiles.Count);
        Assert.AreEqual(before.Items.Count, after.Items.Count);
        CollectionAssert.AreEquivalent(
            before.Profiles.Single(profile => profile.Id == Fisherman).Items.Select(item => item.Id).ToArray(),
            after.Profiles.Single(profile => profile.Id == Fisherman).Items.Select(item => item.Id).ToArray());
    }

    [TestMethod]
    public void StarterKit_FitsTheCharactersGender()
    {
        Grant(MaleCharacterId, Fisherman);
        Grant(FemaleCharacterId, Fisherman);

        int[] Kit(ulong characterId) => ReadCharacter(characterId).Profiles.Single(profile => profile.Id == Fisherman)
            .Items.Select(item => item.Definition).ToArray();

        CollectionAssert.AreEquivalent(new[] { Hat, MaleVest, Shoes }, Kit(MaleCharacterId));
        CollectionAssert.AreEquivalent(new[] { Hat, FemaleVest, Shoes }, Kit(FemaleCharacterId));
        Assert.IsFalse(ReadCharacter(FemaleCharacterId).Items.Any(item => item.Definition == MaleVest),
            "the other gender's piece isn't handed out");
    }

    [TestMethod]
    public void AJobWithoutAKit_WearsTheAdventurersClothes()
    {
        Assert.AreEqual(JobGrantResult.Granted, Grant(MaleCharacterId, CardDuelist));

        var character = ReadCharacter(MaleCharacterId);

        CollectionAssert.AreEquivalent(new[] { 1, 2, 3 },
            character.Profiles.Single(profile => profile.Id == CardDuelist).Items.Select(item => item.Id).ToArray());
        Assert.AreEqual(3, character.Items.Count, "no new items");
    }

    [TestMethod]
    public void StaffAndUnknownJobs_AreRefusedAndNothingIsSaved()
    {
        Assert.AreEqual(JobGrantResult.StaffJob, Grant(MaleCharacterId, Referee));
        Assert.AreEqual(JobGrantResult.StaffJob, Grant(MaleCharacterId, (int)SpecialProfileIds.Enforcer));
        Assert.AreEqual(JobGrantResult.UnknownJob, Grant(MaleCharacterId, 9999));
        Assert.AreEqual(JobGrantResult.CharacterNotFound, Grant(12345, Fisherman));

        Assert.AreEqual(1, ReadCharacter(MaleCharacterId).Profiles.Count);
    }

    [TestMethod]
    public void TheClientsJob_WearsTheKitOnePieceASlot()
    {
        Grant(FemaleCharacterId, Fisherman);

        var fisherman = ReadCharacter(FemaleCharacterId).Profiles.Single(profile => profile.Id == Fisherman);
        var clientProfile = ProfileHelper.CreateClientProfile(_resourceManager.Profiles[Fisherman], fisherman, _resourceManager);

        Assert.AreEqual(Fisherman, clientProfile.Id);
        Assert.AreEqual(1, clientProfile.Rank);
        CollectionAssert.AreEquivalent(new[] { 1, 3, 5 }, clientProfile.Items.Keys.ToArray());
        Assert.AreEqual(fisherman.Items.Single(item => item.Definition == FemaleVest).Id, clientProfile.Items[3].Id);
    }

    /// <summary>
    /// Every trainer is an npc the world spawns, under the name the table gives it, and teaches a real job that players
    /// can have.
    /// </summary>
    [TestMethod]
    public void EveryTrainer_IsASpawnedNpcTeachingARealJob()
    {
        var src = FindSourceDirectory();
        var spawnScript = File.ReadAllText(Path.Combine(src, "Scripts", "Zone", "FabledRealms.lua"));

        using var npcsFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(src, "Resources", "Npcs.json")));
        var npcNames = npcsFile.RootElement.EnumerateArray()
            .Where(npc => npc.TryGetProperty("Name", out _))
            .ToDictionary(npc => npc.GetProperty("Id").GetInt32(), npc => npc.GetProperty("Name").GetString());

        using var profilesFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(src, "Resources", "Profiles.json")));
        var profileIds = profilesFile.RootElement.EnumerateArray().Select(profile => profile.GetProperty("Id").GetInt32()).ToHashSet();

        Assert.IsTrue(JobTrainers.All.Any(trainer => trainer.ProfileId == Fisherman), "the Fisherman has a trainer");
        Assert.IsTrue(JobTrainers.All.Any(trainer => trainer.ProfileId == 14), "the Miner has a trainer");

        foreach (var trainer in JobTrainers.All)
        {
            var npcId = (int)(trainer.NpcGuid - 100000000000);

            Assert.IsTrue(Regex.IsMatch(spawnScript, $@"spawnNpcWithGuid\({npcId}, {trainer.NpcGuid},"),
                $"{trainer.Name} ({trainer.NpcGuid}) isn't spawned by FabledRealms.lua.");
            Assert.AreEqual(trainer.Name, npcNames.GetValueOrDefault(npcId), $"npc {npcId} isn't {trainer.Name}.");
            Assert.IsTrue(profileIds.Contains(trainer.ProfileId), $"{trainer.Name} teaches unknown job {trainer.ProfileId}.");
            Assert.IsFalse(Enum.IsDefined((SpecialProfileIds)trainer.ProfileId), $"{trainer.Name} teaches a staff job.");
        }
    }

    private JobGrantResult Grant(ulong characterId, int profileId)
    {
        using var dbContext = CreateDbContext();

        return JobGrant.TrySave(dbContext, _resourceManager, NullLogger.Instance, characterId, profileId, out _);
    }

    /// <summary>A fresh context, the way the login query reads the character back.</summary>
    private DbCharacter ReadCharacter(ulong characterId)
    {
        using var dbContext = CreateDbContext();

        return dbContext.Characters
            .AsNoTracking()
            .Include(character => character.Items)
            .Include(character => character.Profiles)
                .ThenInclude(profile => profile.Items)
            .Single(character => character.Id == characterId);
    }

    private DatabaseContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder()
            .UseSqlite(_connection)
            .Options;

        return new SqliteDatabaseContext(options);
    }

    private void AddItem(int id, int slot, int genderUsage = 0)
    {
        _resourceManager.ClientItemDefinitions.TryAdd(id, new ClientItemDefinition
        {
            Id = id,
            Type = 1,
            Slot = slot,
            GenderUsage = genderUsage
        });
    }

    private void AddProfile(int id, params int[] defaultItems)
    {
        _resourceManager.Profiles.TryAdd(id, new ClientProfileData
        {
            Id = id,
            DefaultItems = [.. defaultItems]
        });
    }

    private static string FindSourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Scripts", "Zone", "FabledRealms.lua")))
                return directory.FullName;
        }

        throw new AssertFailedException("src/Scripts/Zone/FabledRealms.lua not found above the test output.");
    }

    private static void StopWatcher(ResourceManager resourceManager)
    {
        var watcher = (FileSystemWatcher)typeof(ResourceManager)
            .GetField("_fileSystemWatcher", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resourceManager)!;

        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
    }
}
