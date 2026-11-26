using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.ChatCommands;
using Sanctuary.Game.Reports;
using Sanctuary.Packet.Common.Chat;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Character names that are not unique or not clean: what <see cref="CharacterNameHelper"/> refuses, how staff commands and
/// reports behave when two accounts hold one full name anyway (older characters were never checked), names that would forge
/// a line of the audit file, and the chat record that survives a log out and in.
/// </summary>
[TestClass]
public sealed class ReportNameFixTests
{
    private SqliteConnection _connection = null!;
    private ManualTimeProvider _clock = null!;
    private ReportService _service = null!;

    private sealed class Factory(Func<DatabaseContext> create) : IDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext() => create();
    }

    private sealed class NullNotifier : IReportNotifier
    {
        public void Notify(ReportNotification notification)
        {
        }
    }

    private DatabaseContext CreateDbContext() => new SqliteDatabaseContext(new DbContextOptionsBuilder().UseSqlite(_connection).Options);

    private static DbCharacter NewCharacter(ulong id, string first, string last) => new()
    {
        Id = id,
        FirstName = first,
        LastName = last,
        Head = "h",
        HeadId = 1,
        Hair = "h",
        HairId = 1,
        SkinTone = "s",
        SkinToneId = 1
    };

    private static DbUser NewUser(ulong id, params DbCharacter[] characters)
    {
        var user = new DbUser { Id = id, Username = "user" + id, Password = "x", Created = DateTimeOffset.UnixEpoch };

        foreach (var character in characters)
            user.Characters.Add(character);

        return user;
    }

    [TestInitialize]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var dbContext = CreateDbContext();
        dbContext.Database.Migrate();

        dbContext.Users.AddRange(
            NewUser(1, NewCharacter(10, "Rita", "Reporter")),
            // Two accounts with one full name: "Alice Admin" is first "Alice" last "Admin", and first "Alice Admin" with no last.
            NewUser(2, NewCharacter(20, "Alice", "Admin")),
            NewUser(3, NewCharacter(30, "Alice Admin", "")),
            // One account with two characters of one full name.
            NewUser(4, NewCharacter(41, "Twin", "Twin"), NewCharacter(40, "Twin", "Twin")));

        dbContext.SaveChanges();

        _clock = new ManualTimeProvider();
        _service = new ReportService(new Factory(CreateDbContext), _clock, NullLoggerFactory.Instance, new NullNotifier());
    }

    [TestCleanup]
    public void Cleanup() => _connection.Dispose();

    private static ReportContext Reporter() => new(1, 10, "Rita Reporter", "Wugachug", "1.0 0.0 1.0");

    private static readonly (int, string, ulong)[] Lines = [(0, "rude", 1000UL)];

    private List<DbReport> Reports()
    {
        using var dbContext = CreateDbContext();
        return dbContext.Reports.AsNoTracking().ToList();
    }

    private static string Source(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, relativePath);

            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        throw new FileNotFoundException(relativePath);
    }

    // ---- Staff commands on a name two accounts hold ----

    [TestMethod]
    public void StaffTarget_OfANameTwoAccountsHold_IsRefusedNotThrownOn()
    {
        using var dbContext = CreateDbContext();

        var matches = ModChatCommand.FindByName(dbContext, "Alice Admin");

        Assert.AreEqual(2, matches.Count);
        Assert.IsFalse(ModChatCommand.TryChooseByName(matches, out _));
    }

    [TestMethod]
    public void StaffTarget_OfANameOnlyOneAccountHolds_IsTheLowestCharacterId()
    {
        using var dbContext = CreateDbContext();

        var matches = ModChatCommand.FindByName(dbContext, "Twin Twin");

        Assert.IsTrue(ModChatCommand.TryChooseByName(matches, out var chosen));
        Assert.AreEqual(40UL, chosen.Id);
        Assert.AreEqual(4UL, chosen.UserId);
    }

    [TestMethod]
    public void StaffTarget_OfANameNobodyHas_IsNotFound()
    {
        using var dbContext = CreateDbContext();

        Assert.IsFalse(ModChatCommand.TryChooseByName(ModChatCommand.FindByName(dbContext, "Nobody Here"), out _));
    }

    // ---- Reports on a name two accounts hold ----

    [TestMethod]
    public void PlayerReport_OfANameTwoAccountsHold_IsNotPutOnEitherAccount()
    {
        // Even when the online lookup found one of them first.
        var online = new OnlineReportTarget(2, 20, "Alice Admin", []);

        var result = _service.SubmitPlayerReport(Reporter(), "Alice Admin", Lines, online);

        Assert.AreEqual(ReportOutcome.Accepted, result.Outcome);

        var report = Reports().Single();

        Assert.IsNull(report.TargetUserId);
        Assert.IsNull(report.TargetCharacterId);
        Assert.AreEqual("Alice Admin", report.TargetName);
        StringAssert.Contains(report.ServerContext, "20");
        StringAssert.Contains(report.ServerContext, "30");
        StringAssert.Contains(report.ServerContext, "different accounts");
    }

    [TestMethod]
    public void PlayerReport_OfANameOneAccountHoldsTwice_IsPutOnTheLowestCharacter()
    {
        _service.SubmitPlayerReport(Reporter(), "Twin Twin", Lines, online: null);

        var report = Reports().Single();

        Assert.AreEqual(4UL, report.TargetUserId);
        Assert.AreEqual(40UL, report.TargetCharacterId);
    }

    [TestMethod]
    public void PlayerReport_WhenTheDatabaseNamesAnotherCharacterThanTheOnlineOne_UsesTheDatabasesAndLooksItUp()
    {
        var found = new OnlineReportTarget(4, 40, "Twin Twin", []);
        var asked = new List<ulong>();

        _service.SubmitPlayerReport(Reporter(), "Twin Twin", Lines, new OnlineReportTarget(4, 99, "Twin Twin", []), id =>
        {
            asked.Add(id);
            return found;
        });

        CollectionAssert.AreEqual(new[] { 40UL }, asked);
        Assert.AreEqual(40UL, Reports().Single().TargetCharacterId);
    }

    // ---- Names in the audit file and the notification ----

    [TestMethod]
    public void AuditLineText_CannotStartANewLineOrReorderTheTextBesideIt()
    {
        var safe = StaffActionLog.ForAuditLine("x\r\nmod|Ban|Actor: \"Nate\"\u202E  y\u0085z\u2028w");

        foreach (var character in safe)
            Assert.IsFalse(char.IsControl(character) || char.GetUnicodeCategory(character) is System.Globalization.UnicodeCategory.Format
                or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator, $"U+{(int)character:X4} is in the line");

        Assert.IsFalse(safe.Contains('"'));
        Assert.AreEqual("x mod|Ban|Actor: 'Nate' y z w", safe);
    }

    [TestMethod]
    public void ReportNames_AreCleanedWhenTheReportIsBuilt()
    {
        var reporter = new ReportContext(1, 10, "Rita\nReporter\u202E", "Wugachug", "1.0 0.0 1.0");

        _service.SubmitPlayerReport(reporter, "Bob Nobody", Lines, online: null);

        var report = Reports().Single();

        Assert.AreEqual("Rita Reporter", report.ReporterName);
    }

    [TestMethod]
    public void ReportOnADirtyOnlineName_KeepsACleanTargetName()
    {
        var online = new OnlineReportTarget(9, 90, "Evil\nName\u202E", []);

        _service.SubmitPlayerReport(Reporter(), "Evil Name", Lines, online);

        Assert.AreEqual("Evil Name", Reports().Single().TargetName);
    }

    // ---- Chat evidence survives a log out and in ----

    [TestMethod]
    public void ChatEvidenceStore_GivesTheSameRecordToEveryConnectionOfACharacter()
    {
        var clock = new ManualTimeProvider();
        var store = new ChatEvidenceStore(clock);

        store.For(10).Add(ChatChannel.WorldSay, "before the relog");

        // A relog is a new Player for the same character: it asks the store again.
        var afterRelog = store.For(10);

        Assert.AreEqual(1, afterRelog.Snapshot().Count);
        Assert.AreEqual("before the relog", store.Snapshot(10).Single().Text);
        Assert.AreEqual(0, store.Snapshot(11).Count);
        Assert.AreEqual(1, store.Count, "asking for a snapshot starts no record");
    }

    [TestMethod]
    public void ChatEvidenceStore_DropsRecordsWithNothingInDate_AndKeepsTheOthers()
    {
        var clock = new ManualTimeProvider();
        var store = new ChatEvidenceStore(clock);

        store.For(1).Add(ChatChannel.WorldSay, "old");
        store.For(2);

        clock.Advance(ChatEvidence.MaxAge - TimeSpan.FromMinutes(1));
        store.For(3).Add(ChatChannel.WorldSay, "recent");

        clock.Advance(TimeSpan.FromMinutes(2));
        store.For(3);

        Assert.AreEqual(1, store.Count, "the records of 1 and 2 are older than the window and hold nothing in date");
        Assert.AreEqual("recent", store.Snapshot(3).Single().Text);
    }

    [TestMethod]
    public void ChatEvidenceStore_IsBounded()
    {
        var store = new ChatEvidenceStore(new ManualTimeProvider());

        for (ulong id = 1; id <= ChatEvidenceStore.MaxCharacters + 50; id++)
            store.For(id).Add(ChatChannel.WorldSay, "hello");

        Assert.AreEqual(ChatEvidenceStore.MaxCharacters, store.Count);
    }

    [TestMethod]
    public void EmptyChatRecord_SaysItIsOnlyWhatTheServerRecorded()
    {
        var now = new DateTimeOffset(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

        var justStarted = ChatEvidence.Format([], now, 1000, now - TimeSpan.FromMinutes(4));
        var longRunning = ChatEvidence.Format([], now, 1000, now - TimeSpan.FromHours(5));

        StringAssert.Contains(justStarted, "since the server started 4 minutes ago");
        StringAssert.Contains(justStarted, "restart");
        StringAssert.Contains(longRunning, "no public chat from them recorded in the last 30 minutes");
        StringAssert.Contains(longRunning, "never recorded");
    }

    [TestMethod]
    public void PlayerReport_WithNoRecordedChat_SaysSoInTheServerContext()
    {
        var online = new OnlineReportTarget(9, 90, "Bob Nobody", [], _clock.GetUtcNow() - TimeSpan.FromMinutes(3));

        _service.SubmitPlayerReport(Reporter(), "Bob Nobody", Lines, online);

        StringAssert.Contains(Reports().Single().ServerContext, "since the server started 3 minutes ago");
    }
}
