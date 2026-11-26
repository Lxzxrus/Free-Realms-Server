using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The staff log table. <c>ChatCommandManager.LogAction</c> needs a Player, which a test can't build, so this tests
/// the writer it hands the action to.
/// </summary>
[TestClass]
public sealed class StaffActionLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 14, 3, 0, TimeSpan.Zero);

    private SqliteConnection _connection = null!;

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Factory(Func<DatabaseContext> create) : IDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext() => create();
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception));
    }

    [TestInitialize]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var dbContext = CreateDbContext();
        dbContext.Database.Migrate();
    }

    [TestCleanup]
    public void Cleanup() => _connection.Dispose();

    private DatabaseContext CreateDbContext() => new SqliteDatabaseContext(new DbContextOptionsBuilder().UseSqlite(_connection).Options);

    private StaffActionLog Log(ILogger? logger = null) =>
        new(new Factory(CreateDbContext), new FixedTime(Now), logger ?? NullLogger.Instance);

    [TestMethod]
    public void TryWrite_StoresTheActionWithTheTimeFromTheClock()
    {
        var written = Log().TryWrite(new StaffActionEntry(
            StaffUserId: 7,
            StaffName: "Ariel Mod",
            Command: "mod",
            Action: "Ban",
            TargetUserId: 9,
            TargetCharacterId: 90,
            TargetName: "Bob Smith",
            Details: "Until: 2026-12-01; Reason: spam"));

        Assert.IsTrue(written);

        using var dbContext = CreateDbContext();
        var row = dbContext.StaffActions.Single();

        Assert.AreEqual(Now, row.Created);
        Assert.AreEqual(7UL, row.StaffUserId);
        Assert.AreEqual("Ariel Mod", row.StaffName);
        Assert.AreEqual("mod", row.Command);
        Assert.AreEqual("Ban", row.Action);
        Assert.AreEqual(9UL, row.TargetUserId);
        Assert.AreEqual(90UL, row.TargetCharacterId);
        Assert.AreEqual("Bob Smith", row.TargetName);
        Assert.AreEqual("Until: 2026-12-01; Reason: spam", row.Details);
    }

    [TestMethod]
    public void TryWrite_AnActionWithoutATargetKeepsTheTargetEmpty()
    {
        Assert.IsTrue(Log().TryWrite(new StaffActionEntry(7, "Ariel Mod", "npc", "Spawn NPC")));

        using var dbContext = CreateDbContext();
        var row = dbContext.StaffActions.Single();

        Assert.IsNull(row.TargetUserId);
        Assert.IsNull(row.TargetCharacterId);
        Assert.IsNull(row.TargetName);
        Assert.IsNull(row.Details);
    }

    [TestMethod]
    public void TryWrite_CutsEveryTextToItsColumn()
    {
        var long1000 = new string('d', 5000);

        Assert.IsTrue(Log().TryWrite(new StaffActionEntry(
            7, new string('s', 200), new string('c', 200), new string('a', 200), 9, 90, new string('t', 200), long1000)));

        using var dbContext = CreateDbContext();
        var row = dbContext.StaffActions.Single();

        Assert.AreEqual(DbStaffAction.MaxStaffNameLength, row.StaffName.Length);
        Assert.AreEqual(DbStaffAction.MaxCommandLength, row.Command.Length);
        Assert.AreEqual(DbStaffAction.MaxActionLength, row.Action.Length);
        Assert.AreEqual(DbStaffAction.MaxTargetNameLength, row.TargetName!.Length);
        Assert.AreEqual(DbStaffAction.MaxDetailsLength, row.Details!.Length);
    }

    [TestMethod]
    public void Truncate_DoesNotSplitASurrogatePair()
    {
        var cut = StaffActionLog.Truncate(new string('a', 31) + "\U0001F600", 32);

        Assert.AreEqual(31, cut.Length);
    }

    [TestMethod]
    public void TryWrite_AddsARowEachTime_SoHistoryKeepsEverything()
    {
        var log = Log();

        for (var i = 0; i < 3; i++)
            Assert.IsTrue(log.TryWrite(new StaffActionEntry(7, "Ariel Mod", "mod", "Mute", 9)));

        using var dbContext = CreateDbContext();
        Assert.AreEqual(3, dbContext.StaffActions.Count());
    }

    [TestMethod]
    public void TryWrite_SurvivesADatabaseThatCannotBeOpened()
    {
        var logger = new RecordingLogger();
        var log = new StaffActionLog(
            new Factory(() => throw new InvalidOperationException("The database is down.")),
            new FixedTime(Now),
            logger);

        var written = log.TryWrite(new StaffActionEntry(7, "Ariel Mod", "mod", "Ban", 9));

        Assert.IsFalse(written);
        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Error && e.Exception is InvalidOperationException), "The failure is logged.");
    }

    [TestMethod]
    public void TryWrite_SurvivesAFailedSave()
    {
        // A database nobody has migrated: the table is missing, so the save fails after the context was made.
        using var bare = new SqliteConnection("Data Source=:memory:");
        bare.Open();

        var logger = new RecordingLogger();
        var log = new StaffActionLog(
            new Factory(() => new SqliteDatabaseContext(new DbContextOptionsBuilder().UseSqlite(bare).Options)),
            new FixedTime(Now),
            logger);

        Assert.IsFalse(log.TryWrite(new StaffActionEntry(7, "Ariel Mod", "mod", "Ban", 9)));
        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Error && e.Exception is not null));
    }

    [TestMethod]
    public void TryWrite_SurvivesHavingNoDatabaseAtAll()
    {
        var logger = new RecordingLogger();
        var log = new StaffActionLog(null, new FixedTime(Now), logger);

        Assert.IsFalse(log.TryWrite(new StaffActionEntry(7, "Ariel Mod", "mod", "Ban", 9)));
        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Warning));
    }
}
