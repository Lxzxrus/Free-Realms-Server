using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Jobs;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;
using Sanctuary.Packet.Common;
using Sanctuary.UdpLibrary;
using Sanctuary.UdpLibrary.Abstractions;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The quest manager with what a quest's steps and its turn-in touch: one character saved in an in-memory database, an
/// Adventurer; the job manager the trainers use; and that character online in an empty zone, sent packets that go
/// nowhere. The shipped rank levels price the stars; the Adventurer and Chef jobs have no kit of their own.
/// </summary>
public sealed class QuestWorld : IDisposable
{
    public const ulong CharacterId = 7;
    public const int Adventurer = ProfileHelper.AdventurerProfileId;
    public const int Chef = 45;

    private readonly SqliteConnection _connection;

    public QuestWorld()
    {
        Directory.CreateDirectory(ResourceManager.BaseDirectory);
        Resources = new ResourceManager(NullLogger<ResourceManager>.Instance);
        StopWatcher(Resources);

        Assert.IsTrue(Resources.RankLevels.Load(FindSourceFile("Resources", "RankLevels.json")));

        foreach (var profileId in new[] { Adventurer, Chef })
            Resources.Profiles.TryAdd(profileId, new ClientProfileData { Id = profileId });

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using (var dbContext = CreateDbContext())
        {
            dbContext.Database.Migrate();

            var character = new DbCharacter
            {
                Id = CharacterId,
                FirstName = "Player",
                Head = string.Empty,
                HeadId = 0,
                Hair = string.Empty,
                HairId = 0,
                SkinTone = string.Empty,
                SkinToneId = 0,
                ActiveProfileId = Adventurer
            };

            character.Profiles.Add(new DbProfile { Id = Adventurer, Level = 1 });

            var user = new DbUser { Username = "player", Password = string.Empty };
            user.Characters.Add(character);

            dbContext.Users.Add(user);
            dbContext.SaveChanges();
        }

        var database = new Database(CreateDbContext);

        // As the Gateway builds them: the job manager refreshes trainer badges through the quest manager, which takes
        // the job manager lazily.
        JobManager jobs = null!;
        Quests = new QuestManager(Resources, null!, database, new Lazy<IJobManager>(() => jobs), NullLogger<QuestManager>.Instance);
        Jobs = jobs = new JobManager(Resources, database, null!, Quests, NullLogger<JobManager>.Instance);

        // The pet tests' connection that never connects, so the packets a step or a turn-in sends go nowhere.
        var connection = new UdpConnection(DispatchProxy.Create<IUdpManager, PetTreasureSaveTests.NoNetwork>(),
            new SocketAddress(AddressFamily.InterNetwork), timeout: 0);

        Player = new Player(null!, connection, Resources, null!, null!)
        {
            Guid = GuidHelper.GetPlayerGuid(CharacterId),
            CharacterId = CharacterId,
            Zone = DispatchProxy.Create<IZone, EmptyZone>()
        };

        AddClientJob(Adventurer);
        Player.ActiveProfileId = Adventurer;
    }

    public ResourceManager Resources { get; }

    public QuestManager Quests { get; }

    public JobManager Jobs { get; }

    public Player Player { get; }

    /// <summary>A quest the server has, offered by nobody in particular.</summary>
    public QuestDefinition Add(QuestDefinition quest)
    {
        Resources.Quests.Quests[quest.QuestId] = quest;
        return quest;
    }

    /// <summary>The player takes the quest, as from its offer: in the log and saved.</summary>
    public QuestProgress Accept(QuestDefinition quest)
    {
        Add(quest);
        Quests.AcceptQuest(Player, quest.QuestId);

        Assert.IsTrue(Player.Quests.TryGetInProgress(quest.QuestId, out var progress), $"Quest {quest.QuestId} wasn't taken.");
        return progress;
    }

    /// <summary>A job the character has already, saved and loaded, as if a trainer had given it before this login.</summary>
    public void Give(int profileId)
    {
        using (var dbContext = CreateDbContext())
        {
            dbContext.Profiles.Add(new DbProfile { CharacterId = CharacterId, Id = profileId, Level = 1 });
            dbContext.SaveChanges();
        }

        AddClientJob(profileId);
    }

    /// <summary>The character as the login query reads it back.</summary>
    public DbCharacter ReadCharacter()
    {
        using var dbContext = CreateDbContext();

        return dbContext.Characters
            .AsNoTracking()
            .Include(character => character.Profiles)
            .Include(character => character.Quests)
                .ThenInclude(quest => quest.Progress)
            .Single(character => character.Id == CharacterId);
    }

    public DatabaseContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder()
            .UseSqlite(_connection)
            .Options;

        return new SqliteDatabaseContext(options);
    }

    public void Dispose() => _connection.Dispose();

    private void AddClientJob(int profileId) =>
        ProfileHelper.AddClientProfile(Player, ProfileHelper.CreateClientProfile(Resources.Profiles[profileId],
            new DbProfile { Id = profileId, Level = 1 }, Resources));

    private sealed class Database(Func<DatabaseContext> createDbContext) : IDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext() => createDbContext();
    }

    /// <summary>A zone with no NPCs in it: the badges and markers a step refreshes find nobody to update.</summary>
    public class EmptyZone : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            switch (targetMethod?.Name)
            {
                case "get_" + nameof(IZone.Npcs):
                    return Array.Empty<Npc>();
                case nameof(IZone.TryGetNpc):
                    args![1] = null;
                    return false;
                default:
                    throw new NotSupportedException(targetMethod?.Name);
            }
        }
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

    private static void StopWatcher(ResourceManager resourceManager)
    {
        var watcher = (FileSystemWatcher)typeof(ResourceManager)
            .GetField("_fileSystemWatcher", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(resourceManager)!;

        watcher.EnableRaisingEvents = false;
        watcher.Dispose();
    }
}
