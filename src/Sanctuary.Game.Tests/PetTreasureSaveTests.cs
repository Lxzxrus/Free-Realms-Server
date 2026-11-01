using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
using Sanctuary.Game.Resources.Definitions.Rewards;
using Sanctuary.Game.Zones;
using Sanctuary.Scripting;
using Sanctuary.UdpLibrary;
using Sanctuary.UdpLibrary.Abstractions;
using Sanctuary.UdpLibrary.Configuration;

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

    /// <summary>The zone a chest is removed from, which remembers what it removed.</summary>
    public class RemovingZone : DispatchProxy
    {
        public List<ulong> RemovedNpcs { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case nameof(IZone.TryRemoveNpc):
                    RemovedNpcs.Add((ulong)args![0]!);
                    return true;
                case "get_" + nameof(IZone.ScriptManager):
                    return DispatchProxy.Create<IScriptManager, NoScripts>();
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }

    public class NoScripts : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IScriptManager.DeleteContext) ? true : throw new NotSupportedException(targetMethod?.Name);
    }

    /// <summary>
    /// The manager of a connection that never connects, so what the player is sent goes nowhere: a connection sends
    /// only once connected, and this one waits forever to send its connect request. Anything else gets a default.
    /// </summary>
    public class NoNetwork : DispatchProxy
    {
        private readonly UdpParams _params = new() { ConnectAttemptDelay = int.MaxValue };

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_" + nameof(IUdpManager.Params) => _params,
            "get_" + nameof(IUdpManager.Logger) => NullLogger.Instance,
            _ => targetMethod is { ReturnType: { IsValueType: true } returnType } && returnType != typeof(void)
                ? Activator.CreateInstance(returnType)
                : null
        };
    }

    /// <summary>Rolls the chest's coins, and the database drops as they are paid.</summary>
    private sealed class CoinsThatCantBePaid : IRewardManager
    {
        public bool TryRollReward(string rewardTableKey, out RewardDropDefinition? drop)
        {
            drop = new CurrencyRewardDropDefinition { CurrencyType = CurrencyType.Coins, Amount = 100 };
            return true;
        }

        public bool TryGrantReward(Player player, RewardDropDefinition drop, ulong sourceGuid = 0) =>
            throw new RetryLimitExceededException("The database can't be reached.");

        public bool TryGrantItem(Player player, int itemDefinitionId, int? tint, int quantity = 1, ulong sourceGuid = 0) =>
            throw new NotSupportedException();

        public bool TryGrantCurrency(Player player, CurrencyType currencyType, int amount) => throw new NotSupportedException();
    }

    private sealed class Database(Func<DatabaseContext> createDbContext) : IDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext() => createDbContext();
    }

    /// <summary>
    /// Once its rest is saved, the chest is removed before it pays, so a payout that throws (here the database drops as
    /// the coins are saved) can't leave it opened and never removed, since its despawn removes only a chest it can open.
    /// </summary>
    [TestMethod]
    public void AChestWhosePayoutThrows_IsStillRemoved_WithItsRestSaved()
    {
        var zone = DispatchProxy.Create<IZone, RemovingZone>();
        var connection = new UdpConnection(DispatchProxy.Create<IUdpManager, NoNetwork>(), new SocketAddress(AddressFamily.InterNetwork),
            timeout: 0);
        var player = new Player(null!, connection, null!, null!, null!) { Guid = GuidHelper.GetPlayerGuid(CharacterId), Zone = zone };
        var chest = new PetTreasureChest(zone, player.Guid, "Treasure")
        {
            Guid = 1234,
            PetId = PetId,
            FoundAt = FoundAt,
            CoinRewardTableKey = "TreasureCoins"
        };

        Assert.ThrowsExactly<RetryLimitExceededException>(() => PetHelper.TryOpenTreasureChest(player, chest, new CoinsThatCantBePaid(),
            null!, new Database(CreateDbContext), NullLogger.Instance, out _));

        CollectionAssert.AreEqual(new[] { chest.Guid }, ((RemovingZone)(object)zone).RemovedNpcs);
        Assert.AreEqual(FoundAt, TreasureFoundAt(CharacterId));
    }
}
