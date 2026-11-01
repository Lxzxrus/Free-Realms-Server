using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.UdpLibrary;
using Sanctuary.UdpLibrary.Abstractions;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The quest world (<see cref="QuestWorld"/>) with the shipped lots loaded and a real <see cref="HouseManager"/> on its
/// database: what the starter house, the Housing door and going into a house touch. Its player is
/// <see cref="QuestWorld.CharacterId"/>, on <see cref="Account"/>, with no house, in a zone that is no world, so nothing
/// moves them into a house. A player made by <see cref="PlayerInTheWorld"/> stands in <see cref="World"/> instead, and a
/// move into a house takes them into a stand-in of it (<see cref="Moves"/>).
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

        World = StandIn<WorldZone>(new WorldZoneDefinition { Id = 1, Name = "FabledRealms" });
    }

    public QuestWorld Quests { get; }

    /// <summary>The quest world's player, on <see cref="Account"/>.</summary>
    public Player Player => Quests.Player;

    /// <summary>
    /// The world, with no NPCs but those a test adds (<see cref="AddNpc"/>): enough of it for the quests and a move into
    /// a house. No zone can be built in a test, so it's an unstarted one, as <see cref="ZoneTeleportsTests"/> uses.
    /// </summary>
    public WorldZone World { get; }

    /// <summary>Each move into another zone the zone manager was asked for, in order: the zone and its owner.</summary>
    public List<(int ZoneDefinitionId, ulong? OwnerId)> Moves { get; } = [];

    /// <summary>Runs as each move starts, before the player leaves where they are: what a test checks at that moment.</summary>
    public Action? OnMove { get; set; }

    /// <summary>Whether the zone manager fails every move, as when a house instance can't be made.</summary>
    public bool MovesFail { get; set; }

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

    /// <summary>
    /// A player online on the character, standing in <see cref="World"/> at its origin and wearing the Adventurer job as
    /// the quest world's player does, whom a move takes into a stand-in of the zone (<see cref="Moves"/>). What they are
    /// sent goes nowhere.
    /// </summary>
    public Player PlayerInTheWorld(ulong characterId, ulong userId)
    {
        // The pet tests' connection that never connects, as the quest world's player has.
        var connection = new UdpConnection(DispatchProxy.Create<IUdpManager, PetTreasureSaveTests.NoNetwork>(),
            new SocketAddress(AddressFamily.InterNetwork), timeout: 0);

        var player = new Player(null!, connection, Quests.Resources, new MovingZoneManager(this), new NoTrades())
        {
            Guid = GuidHelper.GetPlayerGuid(characterId),
            CharacterId = characterId,
            UserId = userId,
            Zone = World
        };

        ProfileHelper.AddClientProfile(player, ProfileHelper.CreateClientProfile(Quests.Resources.Profiles[QuestWorld.Adventurer],
            new DbProfile { Id = QuestWorld.Adventurer, Level = 1 }, Quests.Resources));
        player.ActiveProfileId = QuestWorld.Adventurer;

        return player;
    }

    /// <summary>An NPC of that <c>Npcs.json</c> id standing in <see cref="World"/>, with the GUID its fixed spawn gets.</summary>
    public Npc AddNpc(int definitionId, Vector4 position)
    {
        var npc = new Npc(World)
        {
            Guid = GuidHelper.GetNpcGuid((ulong)definitionId, GuidHelper.OpenWorldInstanceId),
            DefinitionId = definitionId
        };

        npc.UpdatePosition(position, Quaternion.Identity);
        NpcsOf(World)[npc.Guid] = npc;

        return npc;
    }

    public void Dispose() => Quests.Dispose();

    /// <summary>
    /// An unstarted zone of that definition, with no NPCs: what a move and the quests ask of a zone (its definition and its
    /// NPCs) works, and nothing else does.
    /// </summary>
    private static TZone StandIn<TZone>(BaseZoneDefinition definition, ulong? ownerId = null) where TZone : BaseZone
    {
        var zone = (TZone)RuntimeHelpers.GetUninitializedObject(typeof(TZone));

        typeof(BaseZone).GetField("_zoneDefinition", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(zone, definition);
        typeof(BaseZone).GetField("_npcs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(zone, new ConcurrentDictionary<ulong, Npc>());
        typeof(BaseZone).GetProperty(nameof(BaseZone.OwnerId))!.SetValue(zone, ownerId);

        return zone;
    }

    private static ConcurrentDictionary<ulong, Npc> NpcsOf(BaseZone zone) =>
        (ConcurrentDictionary<ulong, Npc>)typeof(BaseZone).GetField("_npcs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(zone)!;

    /// <summary>
    /// Moves a player into a stand-in of the zone they're sent to, as the Gateway's zone manager moves them into a new
    /// instance, and records it (<see cref="Moves"/>). Nothing else is asked of it here.
    /// </summary>
    private sealed class MovingZoneManager(HouseWorld world) : IZoneManager
    {
        public int StartingZoneDefinitionId => world.World.DefinitionId;

        public WorldZone StartingZone => world.World;

        public IEnumerable<IZone> Zones => throw new NotSupportedException();

        public bool Load() => throw new NotSupportedException();

        public bool TryGetPlayer(ulong guid, [MaybeNullWhen(false)] out Player player) => throw new NotSupportedException();

        public bool TryGetPlayer(string name, [MaybeNullWhen(false)] out Player player) => throw new NotSupportedException();

        public bool TryMovePlayerToZone(int zoneDefinitionId, ulong? ownerId, Player player, Vector4 position, Quaternion rotation,
            out IZone zone)
        {
            world.OnMove?.Invoke();
            world.Moves.Add((zoneDefinitionId, ownerId));

            if (world.MovesFail || !world.Quests.Resources.Zones.TryGetValue(zoneDefinitionId, out var definition))
            {
                zone = null!;
                return false;
            }

            zone = StandIn<HousingZone>(definition, ownerId);
            player.Zone = zone;
            player.UpdatePosition(position, rotation);

            return true;
        }

        public bool TryRemoveZoneInstance(IZone zone) => throw new NotSupportedException();

        public void EvictIfEmpty(IZone zone) => throw new NotSupportedException();
    }

    /// <summary>Trades with nobody: a player moving into a house has no trade to cancel.</summary>
    private sealed class NoTrades : ITradeManager
    {
        public bool CanTrade(Player requester, Player target) => false;

        public void RequestTrade(Player requester, Player target) => throw new NotSupportedException();

        public void HandleClientCommand(Player player, TradeCommand command) => throw new NotSupportedException();

        public void OnZoning(Player player)
        {
        }

        public void OnDisconnected(Player player)
        {
        }
    }

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
