using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The quest world (<see cref="QuestWorld"/>) with the shipped lots loaded and a real <see cref="HouseManager"/> on its
/// database: what the starter house, the Housing door and going into a house touch. Its player is
/// <see cref="QuestWorld.CharacterId"/>, on <see cref="Account"/>, with no house. The house manager has no zone manager,
/// so nothing here moves a player into a house.
/// </summary>
public sealed class HouseWorld : IDisposable
{
    public const int Apartment = 1024;
    public const int Seaside = 14;
    public const int BlacksporeHouse = 22;

    private ulong _nextUserId = 100;

    public HouseWorld()
    {
        Quests = new QuestWorld();
        Assert.IsTrue(Quests.Resources.Zones.Load(FindSourceDirectory("Resources", "Zones")), "The zone files failed to load.");

        Account = Quests.ReadCharacter().UserId;
        Player.UserId = Account;

        Houses = new HouseManager(Quests.Resources, null!, Quests.DbContextFactory, new HouseInvites(), Logged,
            Microsoft.Extensions.Options.Options.Create(Options));
    }

    public QuestWorld Quests { get; }

    /// <summary>The quest world's player, on <see cref="Account"/>.</summary>
    public Player Player => Quests.Player;

    public ulong Account { get; }

    /// <summary>The settings the house manager reads, as it reads them: a test may change them before it acts.</summary>
    public HousingOptions Options { get; } = new();

    public HouseManager Houses { get; }

    /// <summary>What the house manager logged.</summary>
    public RecordingLogger<HouseManager> Logged { get; } = new();

    /// <summary>A new account, with no characters.</summary>
    public ulong AddAccount()
    {
        var userId = _nextUserId++;

        using var dbContext = Quests.CreateDbContext();
        dbContext.Users.Add(new DbUser { Id = userId, Username = $"account{userId}", Password = string.Empty });
        dbContext.SaveChanges();

        return userId;
    }

    /// <summary>A character saved on the account, with that much Station Cash and coins.</summary>
    public void AddCharacter(ulong userId, ulong characterId, int stationCash = 0, int coins = 0)
    {
        using var dbContext = Quests.CreateDbContext();
        dbContext.Characters.Add(new DbCharacter
        {
            Id = characterId,
            UserId = userId,
            FirstName = $"Character{characterId}",
            FullName = $"Character{characterId}",
            Head = string.Empty,
            HeadId = 0,
            Hair = string.Empty,
            HairId = 0,
            SkinTone = string.Empty,
            SkinToneId = 0,
            StationCash = stationCash,
            Coins = coins
        });
        dbContext.SaveChanges();
    }

    /// <summary>Sets a saved character's Station Cash and coins.</summary>
    public void SetBalance(ulong characterId, int stationCash, int coins)
    {
        using var dbContext = Quests.CreateDbContext();
        dbContext.Characters
            .Where(character => character.Id == characterId)
            .ExecuteUpdate(setters => setters
                .SetProperty(character => character.StationCash, stationCash)
                .SetProperty(character => character.Coins, coins));
    }

    /// <summary>A saved character's Station Cash and coins.</summary>
    public (int StationCash, int Coins) Balance(ulong characterId)
    {
        using var dbContext = Quests.CreateDbContext();
        var character = dbContext.Characters.AsNoTracking().Single(candidate => candidate.Id == characterId);

        return (character.StationCash, character.Coins);
    }

    /// <summary>A house the character bought, as an order saves it, changed as the test says.</summary>
    public ulong AddHouse(ulong characterId, int zoneDefinitionId, Action<DbHouse>? change = null)
    {
        var house = new DbHouse { CharacterId = characterId, ZoneDefinitionId = zoneDefinitionId };
        change?.Invoke(house);

        using var dbContext = Quests.CreateDbContext();
        dbContext.Houses.Add(house);
        dbContext.SaveChanges();

        return house.Id;
    }

    /// <summary>Every house of the account, whoever on it holds them, with their fixtures, by id.</summary>
    public List<DbHouse> HousesOf(ulong userId)
    {
        using var dbContext = Quests.CreateDbContext();

        return dbContext.Houses
            .AsNoTracking()
            .Include(house => house.Fixtures)
            .Where(house => house.Character.UserId == userId)
            .AsEnumerable()
            .OrderBy(house => house.Id)
            .ToList();
    }

    /// <summary>A player online on the character, who is sent nothing.</summary>
    public static Player PlayerOn(ulong characterId, ulong userId) =>
        new(null!, null!, null!, null!, null!)
        {
            Guid = GuidHelper.GetPlayerGuid(characterId),
            CharacterId = characterId,
            UserId = userId
        };

    public void Dispose() => Quests.Dispose();

    private static string FindSourceDirectory(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);

            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "hsg_hum_condo.json")))
                return candidate;
        }

        throw new DirectoryNotFoundException($"src/{string.Join('/', relativePath)} not found above the test output.");
    }

    /// <summary>Every line logged, with its level.</summary>
    public sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
