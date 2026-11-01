using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A pet's treasure rest is saved as its chest pays out (<see cref="PetHelper.SaveTreasureFoundAt"/>), not only at
/// logout, so a gateway restart while the player is online doesn't forget it and let them dig up another chest.
/// </summary>
[TestClass]
public sealed class PetTreasureSaveTests
{
    private const ulong CharacterId = 7;
    private const ulong OtherCharacterId = 8;
    private const int PetId = 1;
    private const int PetDefinition = 1;
    private const int NoTint = 0;
    private const int Adventurer = 1;

    private static readonly DateTimeOffset FoundAt = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

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
            Username = "player",
            Password = string.Empty
        };

        // Both characters have a pet 1: pet ids count per character.
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
            character.Pets.Add(PetHelper.CreatePet(PetId, PetDefinition, NoTint));
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

    /// <summary>The pet's treasure time, read back as the login reads it into <see cref="Entities.Player.PetTreasureFoundAt"/>.</summary>
    private DateTimeOffset? TreasureFoundAt(ulong characterId)
    {
        using var dbContext = CreateDbContext();

        return dbContext.Pets.AsNoTracking()
            .Single(pet => pet.CharacterId == characterId && pet.Id == PetId)
            .TreasureFoundAt;
    }

    private void Save(ulong characterId, int petId)
    {
        using var dbContext = CreateDbContext();

        PetHelper.SaveTreasureFoundAt(dbContext, characterId, petId, FoundAt);
    }

    [TestMethod]
    public void ATreasureFound_IsSavedAtOnce()
    {
        Save(CharacterId, PetId);

        Assert.AreEqual(FoundAt, TreasureFoundAt(CharacterId));
    }

    [TestMethod]
    public void AfterARestart_ThePlayersPetsStillRest()
    {
        Save(CharacterId, PetId);

        var treasureFoundAt = TreasureFoundAt(CharacterId);

        Assert.IsNotNull(treasureFoundAt);

        var loaded = new Dictionary<int, DateTimeOffset> { [PetId] = treasureFoundAt.Value };

        Assert.IsTrue(PetHelper.IsTreasureOnCooldown(loaded, FoundAt + TimeSpan.FromHours(1)));
        Assert.IsFalse(PetHelper.IsTreasureOnCooldown(loaded, FoundAt + PetHelper.TreasureCooldown));
    }

    [TestMethod]
    public void OnlyThatCharactersPet_IsChanged()
    {
        Save(CharacterId, PetId);

        Assert.IsNull(TreasureFoundAt(OtherCharacterId));
    }

    [TestMethod]
    public void APetTheCharacterDoesntHave_ChangesNothing()
    {
        Save(CharacterId, PetId + 1);

        Assert.IsNull(TreasureFoundAt(CharacterId));
        Assert.IsNull(TreasureFoundAt(OtherCharacterId));
    }

    /// <summary>The zone the chest and its owner are in; nothing on it is called before the chest pays.</summary>
    public class StandInZone : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class UnreachableDatabase : IDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext() => throw new RetryLimitExceededException("The database can't be reached.");
    }

    /// <summary>
    /// The rest is saved before the chest pays. A chest whose rest can't be saved used to stay opened, unpaid, and was
    /// never removed, since its despawn removes only a chest it can open.
    /// </summary>
    [TestMethod]
    public void AChestWhoseRestCantBeSaved_PaysNothing_AndClosesAgain()
    {
        var zone = DispatchProxy.Create<IZone, StandInZone>();
        var player = new Player(null!, null!, null!, null!, null!) { Guid = GuidHelper.GetPlayerGuid(CharacterId), Zone = zone };
        var chest = new PetTreasureChest(zone, player.Guid, "Treasure") { PetId = PetId, FoundAt = FoundAt };

        Assert.IsFalse(PetHelper.TryOpenTreasureChest(player, chest, null!, null!, new UnreachableDatabase(), NullLogger.Instance,
            out var itemDrop));
        Assert.IsNull(itemDrop);
        Assert.IsTrue(chest.TryOpen(), "Closed again, for the player to open later or its despawn to remove.");
    }
}
