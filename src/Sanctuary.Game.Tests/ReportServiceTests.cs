using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
/// <see cref="ReportService"/> on a real (in-memory SQLite, migrated) database: what is kept, what is dropped silently,
/// and that the limits follow the clock.
/// </summary>
[TestClass]
public sealed class ReportServiceTests
{
    private const ulong ReporterUser = 1;
    private const ulong ReporterCharacter = 10;
    private const ulong TargetUser = 2;
    private const ulong TargetCharacter = 20;

    private SqliteConnection _connection = null!;
    private ManualTimeProvider _clock = null!;
    private RecordingNotifier _notifier = null!;
    private RecordingLoggerFactory _loggers = null!;
    private ReportService _service = null!;

    private sealed class Factory(Func<DatabaseContext> create) : IDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext() => create();
    }

    private sealed class RecordingNotifier : IReportNotifier
    {
        public List<ReportNotification> Sent { get; } = [];
        public bool Throws { get; set; }

        public void Notify(ReportNotification notification)
        {
            if (Throws)
                throw new InvalidOperationException("notifier broke");

            Sent.Add(notification);
        }
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<(string Category, LogLevel Level)> Entries { get; } = [];

        private sealed class Logger(string category, RecordingLoggerFactory owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => owner.Entries.Add((category, logLevel));
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, this);

        public void Dispose()
        {
        }
    }

    [TestInitialize]
    public void Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using (var dbContext = CreateDbContext())
        {
            dbContext.Database.Migrate();

            var reporter = NewUser(ReporterUser, "reporter", NewCharacter(ReporterCharacter, "Rita", "Reporter"));
            var target = NewUser(TargetUser, "target", NewCharacter(TargetCharacter, "Bob", "Smith"));

            // A second character on the reporter's account, and a house of the target's.
            reporter.Characters.Add(NewCharacter(11, "Rita", "Alt"));

            dbContext.Users.AddRange(reporter, target);
            dbContext.SaveChanges();

            dbContext.Houses.Add(new DbHouse { Id = 5, CharacterId = TargetCharacter, ZoneDefinitionId = 1, Name = "Bob's Place" });
            dbContext.Houses.Add(new DbHouse { Id = 6, CharacterId = ReporterCharacter, ZoneDefinitionId = 1, Name = "Rita's Place" });
            dbContext.SaveChanges();
        }

        _clock = new ManualTimeProvider();
        _notifier = new RecordingNotifier();
        _loggers = new RecordingLoggerFactory();
        _service = NewService();
    }

    [TestCleanup]
    public void Cleanup() => _connection.Dispose();

    private ReportService NewService() => new(new Factory(CreateDbContext), _clock, _loggers, _notifier);

    private DatabaseContext CreateDbContext() => new SqliteDatabaseContext(new DbContextOptionsBuilder().UseSqlite(_connection).Options);

    private static DbUser NewUser(ulong id, string name, DbCharacter character)
    {
        var user = new DbUser { Id = id, Username = name, Password = "x", Created = DateTimeOffset.UnixEpoch };

        user.Characters.Add(character);
        return user;
    }

    private static DbCharacter NewCharacter(ulong id, string first, string last) => new()
    {
        Id = id,
        FirstName = first,
        LastName = last,
        FullName = $"{first} {last}",
        Head = "h",
        HeadId = 1,
        Hair = "h",
        HairId = 1,
        SkinTone = "s",
        SkinToneId = 1
    };

    private static ReportContext Reporter(ulong user = ReporterUser) =>
        new(user, ReporterCharacter, "Rita Reporter", "Wugachug", "10.0 0.0 -5.5");

    private static readonly (int, string, ulong)[] Lines = [(0, "you are a noob", 1000UL), (0, "bye", 1001UL)];

    private static OnlineReportTarget Online(params string[] chat)
    {
        var evidence = new ChatEvidence(new ManualTimeProvider());

        foreach (var line in chat)
            evidence.Add(ChatChannel.WorldSay, line);

        return new OnlineReportTarget(TargetUser, TargetCharacter, "Bob Smith", evidence.Snapshot());
    }

    private List<DbReport> Reports()
    {
        using var dbContext = CreateDbContext();
        return dbContext.Reports.AsNoTracking().OrderBy(report => (long)report.Id).ToList();
    }

    // ---- Player reports ----

    [TestMethod]
    public void PlayerReport_OfAnOnlinePlayer_KeepsTheClientsLinesAndTheServersOwn()
    {
        var result = _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, Online("hello there", "I mean it"));

        Assert.AreEqual(ReportOutcome.Accepted, result.Outcome);

        var report = Reports().Single();

        Assert.AreEqual(ReportKind.Player, report.Kind);
        Assert.AreEqual(ReportStatus.Open, report.Status);
        Assert.AreEqual(_clock.GetUtcNow(), report.Created);
        Assert.AreEqual(ReporterUser, report.ReporterUserId);
        Assert.AreEqual(ReporterCharacter, report.ReporterCharacterId);
        Assert.AreEqual("Rita Reporter", report.ReporterName);
        Assert.AreEqual("Bob Smith", report.TargetName);
        Assert.AreEqual(TargetUser, report.TargetUserId);
        Assert.AreEqual(TargetCharacter, report.TargetCharacterId);
        Assert.AreEqual("Wugachug", report.Zone);
        Assert.AreEqual("10.0 0.0 -5.5", report.Position);
        StringAssert.Contains(report.ClientText, "you are a noob");
        StringAssert.Contains(report.ServerContext, "hello there");
        StringAssert.Contains(report.ServerContext, "I mean it");
        StringAssert.DoesNotMatch(report.ServerContext, new System.Text.RegularExpressions.Regex("noob"));
    }

    [TestMethod]
    public void PlayerReport_OfAnOfflinePlayer_SaysTheTargetIsOfflineAndFindsThemByName()
    {
        var result = _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, online: null);

        Assert.AreEqual(ReportOutcome.Accepted, result.Outcome);

        var report = Reports().Single();

        Assert.AreEqual("target offline", report.ServerContext);
        Assert.AreEqual(TargetUser, report.TargetUserId);
        Assert.AreEqual(TargetCharacter, report.TargetCharacterId);
    }

    [TestMethod]
    public void PlayerReport_OfANameNobodyHas_IsStillKeptWithTheTypedName()
    {
        _service.SubmitPlayerReport(Reporter(), "  Nobody   Here ", Lines, online: null);

        var report = Reports().Single();

        Assert.AreEqual("Nobody Here", report.TargetName);
        Assert.IsNull(report.TargetUserId);
        StringAssert.StartsWith(report.ServerContext, "target offline");
    }

    [TestMethod]
    public void PlayerReport_TargetNameIsCleanedAndCapped()
    {
        _service.SubmitPlayerReport(Reporter(), "Evil\n‮" + new string('x', 200), Lines, online: null);

        var report = Reports().Single();

        Assert.AreEqual(DbReport.MaxTargetNameLength, report.TargetName!.Length);
        Assert.IsFalse(report.TargetName.Contains('\n'));
        Assert.IsFalse(report.TargetName.Contains('‮'));
    }

    [TestMethod]
    public void PlayerReport_TheClientsLinesAreCleanedCappedAndLimitedToTen()
    {
        var lines = Enumerable.Range(0, 10).Select(i => (i, new string('a', 2000) + "\nsecond line", (ulong)i)).ToArray();

        _service.SubmitPlayerReport(Reporter(), "Bob Smith", lines, online: null);

        var report = Reports().Single();

        Assert.IsTrue(report.ClientText.Length <= DbReport.MaxClientTextLength);
        Assert.AreEqual(10, report.ClientText.Split('\n').Length);
        Assert.IsTrue(report.ClientText.Split('\n').All(line => line.Length < 300));
    }

    [TestMethod]
    public void PlayerReport_WithNoLinesSaysSo()
    {
        _service.SubmitPlayerReport(Reporter(), "Bob Smith", [], online: null);

        StringAssert.Contains(Reports().Single().ClientText, "no chat lines");
    }

    [TestMethod]
    public void PlayerReport_ReportingYourselfIsIgnoredEvenByAnotherCharacterOrOtherCase()
    {
        Assert.AreEqual(ReportOutcome.Ignored, _service.SubmitPlayerReport(Reporter(), "Rita Reporter", Lines, null).Outcome);
        Assert.AreEqual(ReportOutcome.Ignored, _service.SubmitPlayerReport(Reporter(), "rita reporter", Lines, null).Outcome);
        Assert.AreEqual(ReportOutcome.Ignored, _service.SubmitPlayerReport(Reporter(), "Rita Alt", Lines, null).Outcome, "own other character");

        var online = new OnlineReportTarget(ReporterUser, 11, "Rita Alt", []);
        Assert.AreEqual(ReportOutcome.Ignored, _service.SubmitPlayerReport(Reporter(), "Whoever", Lines, online).Outcome);

        Assert.AreEqual(0, Reports().Count);
    }

    [TestMethod]
    public void PlayerReport_EmptyOrControlOnlyNamesAreIgnored()
    {
        Assert.AreEqual(ReportOutcome.Ignored, _service.SubmitPlayerReport(Reporter(), null, Lines, null).Outcome);
        Assert.AreEqual(ReportOutcome.Ignored, _service.SubmitPlayerReport(Reporter(), "  \n\t ", Lines, null).Outcome);
        Assert.AreEqual(0, Reports().Count);
    }

    [TestMethod]
    public void PlayerReport_ASecondOpenReportAboutTheSamePlayerIsDroppedSilently()
    {
        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null).Outcome);
        Assert.AreEqual(ReportOutcome.Duplicate, _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, Online("x")).Outcome);

        Assert.AreEqual(1, Reports().Count);
        Assert.AreEqual(1, _notifier.Sent.Count);
    }

    [TestMethod]
    public void PlayerReport_AfterTheFirstIsClosedAnotherIsAccepted()
    {
        _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null);

        using (var dbContext = CreateDbContext())
            Assert.IsTrue(ReportQueries.TryClose(dbContext, Reports().Single().Id, 7, "done", _clock.GetUtcNow()));

        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null).Outcome);
        Assert.AreEqual(2, Reports().Count);
    }

    [TestMethod]
    public void PlayerReport_AnotherReporterMayReportTheSamePlayer()
    {
        _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null);

        AddAccount(3);

        var other = new ReportContext(3, 30, "Carl Other", "Wugachug", "0 0 0");

        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(other, "Bob Smith", Lines, null).Outcome);
    }

    [TestMethod]
    public void PlayerReport_LimitIsFivePerHourPerAccountAcrossCharacters()
    {
        // Five different targets, so the duplicate rule does not stop them first.
        for (var i = 0; i < ReportLimits.ReportsPerHour; i++)
            Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Reporter(), "Someone " + i, Lines, null).Outcome);

        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitPlayerReport(Reporter(), "Someone Else", Lines, null).Outcome);

        // Another character on the same account is the same account.
        var alt = new ReportContext(ReporterUser, 11, "Rita Alt", "Wugachug", "0 0 0");
        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitPlayerReport(alt, "Someone Else", Lines, null).Outcome);

        _clock.Advance(TimeSpan.FromMinutes(61));

        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Reporter(), "Someone Else", Lines, null).Outcome);
        Assert.AreEqual(ReportLimits.ReportsPerHour + 1, Reports().Count);
    }

    [TestMethod]
    public void PlayerReport_DuplicatesAndSelfReportsDoNotUseTheAllowance()
    {
        _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null);

        for (var i = 0; i < 5; i++)
        {
            _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null);
            _service.SubmitPlayerReport(Reporter(), "Rita Reporter", Lines, null);
        }

        for (var i = 1; i < ReportLimits.ReportsPerHour; i++)
            Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Reporter(), "Someone " + i, Lines, null).Outcome);
    }

    [TestMethod]
    public void PlayerReport_AnAccountThatKeepsTryingIsCutOffBeforeTheDatabase()
    {
        // Duplicates are cheap to drop but still counted as attempts.
        _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null);

        for (var i = 0; i < ReportLimits.AttemptsPerWindow; i++)
            _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null);

        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null).Outcome);
    }

    [TestMethod]
    public void Dropped_ReportsLogAtDebugOnly_AndAcceptedOnesReachTheNotifierOnce()
    {
        _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null);
        _loggers.Entries.Clear();

        _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null);
        _service.SubmitPlayerReport(Reporter(), "Rita Reporter", Lines, null);

        Assert.IsTrue(_loggers.Entries.Count > 0);
        Assert.IsTrue(_loggers.Entries.All(entry => entry.Level == LogLevel.Debug), "a dropped report must log at Debug only");
        Assert.AreEqual(1, _notifier.Sent.Count);
    }

    [TestMethod]
    public void ANotifierThatThrowsDoesNotLoseTheReport()
    {
        _notifier.Throws = true;

        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null).Outcome);
        Assert.AreEqual(1, Reports().Count);
    }

    [TestMethod]
    public void ADatabaseThatFailsIsReportedAsFailedNotThrown()
    {
        var broken = new ReportService(new Factory(() => throw new InvalidOperationException("db down")), _clock, _loggers, _notifier);

        Assert.AreEqual(ReportOutcome.Failed, broken.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null).Outcome);
        Assert.AreEqual(ReportOutcome.Failed, broken.SubmitBugReport(Reporter(), "<BugSubmission/>").Outcome);
        Assert.AreEqual(ReportOutcome.Failed, new ReportService(null, _clock, _loggers, _notifier).SubmitBugReport(Reporter(), "<BugSubmission/>").Outcome);
        Assert.AreEqual(0, _notifier.Sent.Count);
    }

    [TestMethod]
    public void TheNotifierGetsTheReportsFields()
    {
        _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, Online("hi"));

        var sent = _notifier.Sent.Single();

        Assert.AreEqual(ReportKind.Player, sent.Kind);
        Assert.AreEqual("Rita Reporter", sent.ReporterName);
        Assert.AreEqual("Bob Smith", sent.TargetName);
        Assert.AreEqual(Reports().Single().Id, sent.Id);
    }

    // ---- House reports ----

    [TestMethod]
    public void HouseReport_IsFoundByTheServersHouseAndOwner_NotByTheClientsNames()
    {
        var result = _service.SubmitHouseReport(Reporter(), GuidHelper.GetHouseGuid(5), GuidHelper.GetPlayerGuid(TargetCharacter), "Lies", "Somebody Else");

        Assert.AreEqual(ReportOutcome.Accepted, result.Outcome);

        var report = Reports().Single();

        Assert.AreEqual(ReportKind.House, report.Kind);
        Assert.AreEqual(5UL, report.HouseId);
        Assert.AreEqual("Bob Smith", report.TargetName);
        Assert.AreEqual(TargetUser, report.TargetUserId);
        Assert.AreEqual(TargetCharacter, report.TargetCharacterId);
        StringAssert.Contains(report.ServerContext, "Bob's Place");
        StringAssert.Contains(report.ClientText, "Lies");
        StringAssert.DoesNotMatch(report.ServerContext, new System.Text.RegularExpressions.Regex("Lies|Somebody Else|does not match"));
    }

    [TestMethod]
    public void HouseReport_NotesAnOwnerGuidThatDoesNotMatch()
    {
        _service.SubmitHouseReport(Reporter(), GuidHelper.GetHouseGuid(5), GuidHelper.GetPlayerGuid(999), "x", "y");

        StringAssert.Contains(Reports().Single().ServerContext, "does not match");
    }

    [TestMethod]
    public void HouseReport_IgnoresAGuidThatIsNotAHouseAndAHouseThatDoesNotExist()
    {
        // A player guid in the house field must not throw in GuidHelper.GetHouseId.
        Assert.AreEqual(ReportOutcome.Ignored, _service.SubmitHouseReport(Reporter(), GuidHelper.GetPlayerGuid(5), 0, "x", "y").Outcome);
        Assert.AreEqual(ReportOutcome.Ignored, _service.SubmitHouseReport(Reporter(), 0, 0, "x", "y").Outcome);
        Assert.AreEqual(ReportOutcome.Ignored, _service.SubmitHouseReport(Reporter(), GuidHelper.GetHouseGuid(999), 0, "x", "y").Outcome);
        Assert.AreEqual(0, Reports().Count);
    }

    [TestMethod]
    public void HouseReport_OfYourOwnHouseIsIgnored_AndARepeatIsDropped()
    {
        Assert.AreEqual(ReportOutcome.Ignored, _service.SubmitHouseReport(Reporter(), GuidHelper.GetHouseGuid(6), 0, "x", "y").Outcome);

        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitHouseReport(Reporter(), GuidHelper.GetHouseGuid(5), 0, "x", "y").Outcome);
        Assert.AreEqual(ReportOutcome.Duplicate, _service.SubmitHouseReport(Reporter(), GuidHelper.GetHouseGuid(5), 0, "x", "y").Outcome);
        Assert.AreEqual(1, Reports().Count);
    }

    [TestMethod]
    public void HouseAndPlayerReportsShareOneAllowance()
    {
        for (var i = 0; i < ReportLimits.ReportsPerHour - 1; i++)
            _service.SubmitPlayerReport(Reporter(), "Someone " + i, Lines, null);

        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitHouseReport(Reporter(), GuidHelper.GetHouseGuid(5), 0, "x", "y").Outcome);
        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitPlayerReport(Reporter(), "One More", Lines, null).Outcome);
    }

    // ---- Bug reports ----

    [TestMethod]
    public void BugReport_KeepsTheParsedFields_AndGivesTheCategoryToStaff()
    {
        const string xml = "<BugSubmission Category=\"Gameplay\" SubCategory=\"Quests\" Severity=\"3\" BugDescription=\"stuck\"/>";

        var result = _service.SubmitBugReport(Reporter(), xml);

        Assert.AreEqual(ReportOutcome.Accepted, result.Outcome);
        Assert.AreEqual("Gameplay", result.Summary);

        var report = Reports().Single();

        Assert.AreEqual(ReportKind.Bug, report.Kind);
        Assert.IsNull(report.TargetName);
        StringAssert.Contains(report.ClientText, "Category: Gameplay");
        StringAssert.Contains(report.ClientText, "Description: stuck");
        Assert.AreEqual("Wugachug", report.Zone);
        Assert.AreEqual("Gameplay", _notifier.Sent.Single().Summary);
    }

    [TestMethod]
    public void BugReport_WithBadXmlKeepsACutRawCopy()
    {
        var result = _service.SubmitBugReport(Reporter(), "<!DOCTYPE x [<!ENTITY a \"b\">]><BugSubmission/>" + new string('z', 10_000));

        Assert.AreEqual(ReportOutcome.Accepted, result.Outcome);

        var report = Reports().Single();

        StringAssert.Contains(report.ClientText, "Unreadable");
        StringAssert.Contains(report.ClientText, "DOCTYPE");
        Assert.IsTrue(report.ClientText.Length <= DbReport.MaxClientTextLength);
    }

    [TestMethod]
    public void BugReports_AreLimitedSeparatelyAndAreNotDeduplicated()
    {
        for (var i = 0; i < ReportLimits.BugsPerHour; i++)
            Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitBugReport(Reporter(), "<BugSubmission Category=\"same\"/>").Outcome);

        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitBugReport(Reporter(), "<BugSubmission Category=\"same\"/>").Outcome);
        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null).Outcome, "player reports have their own allowance");

        _clock.Advance(TimeSpan.FromMinutes(61));

        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitBugReport(Reporter(), "<BugSubmission/>").Outcome);
    }

    // ---- The staff's view ----

    [TestMethod]
    public void Queries_ListNewestFirstAndOnlyOpenByDefault()
    {
        for (var i = 0; i < 4; i++)
            _service.SubmitBugReport(Reporter(), $"<BugSubmission Category=\"c{i}\"/>");

        using var dbContext = CreateDbContext();
        var ids = Reports().Select(report => report.Id).ToList();

        Assert.IsTrue(ReportQueries.TryClose(dbContext, ids[1], 7, "dup", _clock.GetUtcNow()));
        Assert.IsFalse(ReportQueries.TryClose(dbContext, ids[1], 7, "again", _clock.GetUtcNow()), "closing twice");
        Assert.IsFalse(ReportQueries.TryClose(dbContext, 9999, 7, null, _clock.GetUtcNow()));

        CollectionAssert.AreEqual(new[] { ids[3], ids[2], ids[0] }, ReportQueries.List(dbContext, openOnly: true).Select(report => report.Id).ToList());
        CollectionAssert.AreEqual(new[] { ids[3], ids[2], ids[1], ids[0] }, ReportQueries.List(dbContext, openOnly: false).Select(report => report.Id).ToList());

        var closed = ReportQueries.Get(dbContext, ids[1])!;

        Assert.AreEqual(ReportStatus.Closed, closed.Status);
        Assert.AreEqual(7UL, closed.ClosedByUserId);
        Assert.AreEqual(_clock.GetUtcNow(), closed.ClosedAt);
        Assert.AreEqual("dup", closed.CloseNote);
    }

    [TestMethod]
    public void Queries_ListIsLimitedToTen()
    {
        using var dbContext = CreateDbContext();

        for (var i = 0; i < 25; i++)
            dbContext.Reports.Add(new DbReport { Kind = ReportKind.Bug, ReporterName = "r", Created = _clock.GetUtcNow() });

        dbContext.SaveChanges();

        var listed = ReportQueries.List(dbContext, openOnly: false);

        Assert.AreEqual(ReportQueries.ListLimit, listed.Count);
        Assert.IsTrue(listed.Zip(listed.Skip(1), (a, b) => a.Id > b.Id).All(descending => descending));
    }

    [TestMethod]
    public void Queries_CloseNoteIsCleanedAndCapped()
    {
        _service.SubmitBugReport(Reporter(), "<BugSubmission/>");

        using var dbContext = CreateDbContext();
        var id = Reports().Single().Id;

        Assert.IsTrue(ReportQueries.TryClose(dbContext, id, 7, "line\nbreak " + new string('n', 500), _clock.GetUtcNow()));

        var note = ReportQueries.Get(dbContext, id)!.CloseNote!;

        Assert.AreEqual(DbReport.MaxCloseNoteLength, note.Length);
        Assert.IsFalse(note.Contains('\n'));
    }

    [TestMethod]
    public void Queries_ReportsAboutAPlayerAreCountedForHistory()
    {
        _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null);
        _service.SubmitHouseReport(Reporter(), GuidHelper.GetHouseGuid(5), 0, "x", "y");
        _service.SubmitPlayerReport(Reporter(), "Someone Else", Lines, null);

        for (var i = 0; i < 7; i++)
        {
            using var dbContext = CreateDbContext();
            dbContext.Reports.Add(new DbReport { Kind = ReportKind.Player, ReporterName = "r" + i, TargetUserId = TargetUser, Created = _clock.GetUtcNow() });
            dbContext.SaveChanges();
        }

        using var context = CreateDbContext();
        var (total, recent) = ReportQueries.LoadAbout(context, TargetUser);

        Assert.AreEqual(9, total);
        Assert.AreEqual(ReportQueries.HistoryLimit, recent.Count);
        Assert.AreEqual(0, ReportQueries.LoadAbout(context, 12345).Total);

        var lines = ReportQueries.FormatAboutLines(total, recent);

        Assert.AreEqual(ReportQueries.HistoryLimit + 1, lines.Count);
        StringAssert.Contains(lines[0], "9 reports");
    }

    [TestMethod]
    public void Queries_DetailsAreBoundedAndKeepTheTwoSidesApart()
    {
        var chat = Enumerable.Range(0, 10).Select(i => new string('c', 400) + i).ToArray();

        _service.SubmitPlayerReport(Reporter(), "Bob Smith", chat.Select((text, i) => (i, text, (ulong)i)).ToArray(), Online(chat));

        var lines = ReportQueries.FormatDetails(Reports().Single());

        Assert.IsTrue(lines.Count <= 4 + 2 + 2 * ReportQueries.MaxEvidenceLines, "bounded number of lines");
        Assert.IsTrue(lines.All(line => line.Length <= ReportQueries.MaxLineLength));
        CollectionAssert.Contains(lines, "What the reporter's client says:");
        CollectionAssert.Contains(lines, "What the server saw:");
    }

    [TestMethod]
    public void Queries_StaffNoticesHaveTheDocumentedShape()
    {
        _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null);
        _service.SubmitBugReport(Reporter(), "<BugSubmission Category=\"Gameplay\"/>");

        var all = Reports();
        var player = all[0];
        var bug = all[1];

        Assert.AreEqual($"Report #{player.Id}: Rita Reporter reported Bob Smith (player). !mod report {player.Id}", ReportQueries.FormatStaffNotice(player, string.Empty));
        Assert.AreEqual($"Bug report #{bug.Id} from Rita Reporter: Gameplay", ReportQueries.FormatStaffNotice(bug, "Gameplay"));
    }

    [TestMethod]
    public void History_ListsTheReportsAfterTheStaffActions()
    {
        var report = new DbReport { Id = 3, Kind = ReportKind.Player, ReporterName = "Rita Reporter", Created = _clock.GetUtcNow() };

        var lines = ModChatCommand.FormatHistory("Bob Smith", 0, [], 1, [report]);

        Assert.AreEqual(3, lines.Count);
        StringAssert.Contains(lines[1], "1 report");
        StringAssert.Contains(lines[2], "#3");
        StringAssert.Contains(lines[2], "Rita Reporter");

        // Without report data the history is the staff actions alone, as before.
        Assert.AreEqual(1, ModChatCommand.FormatHistory("Bob Smith", 0, []).Count);
    }

    // ---- Limits across accounts, the age of the account, the open ceilings and retention ----

    private void AddAccount(ulong id, DateTimeOffset? created = null)
    {
        using var dbContext = CreateDbContext();

        dbContext.Users.Add(new DbUser { Id = id, Username = "user" + id, Password = "x", Created = created ?? DateTimeOffset.UnixEpoch });
        dbContext.SaveChanges();
    }

    private static ReportContext Account(ulong user) => new(user, user * 10, "Reporter " + user, "Wugachug", "0 0 0");

    private const string BugXml = "<BugSubmission Category=\"Gameplay\"/>";

    [TestMethod]
    public void AnAccountTooNewToReportIsLimited_AndOldEnoughIsNot()
    {
        AddAccount(50, _clock.GetUtcNow() - TimeSpan.FromMinutes(2));

        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitPlayerReport(Account(50), "Bob Smith", Lines, null).Outcome);
        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitBugReport(Account(50), BugXml).Outcome);
        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitHouseReport(Account(50), GuidHelper.GetHouseGuid(5), GuidHelper.GetPlayerGuid(TargetCharacter), "Bob's Place", "Bob Smith").Outcome);
        Assert.AreEqual(0, Reports().Count);
        Assert.AreEqual(0, _notifier.Sent.Count);

        _clock.Advance(ReportLimits.MinAccountAge);

        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Account(50), "Bob Smith", Lines, null).Outcome);
    }

    [TestMethod]
    public void AnAccountWithNoRowIsLimited()
    {
        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitPlayerReport(Account(777), "Bob Smith", Lines, null).Outcome);
        Assert.AreEqual(0, Reports().Count);
    }

    [TestMethod]
    public void ManyAccountsCannotPileOpenReportsOnOneTarget()
    {
        var last = 100UL + (ulong)ReportLimits.MaxOpenReportsPerTarget + 2;

        for (var user = 100UL; user <= last; user++)
            AddAccount(user);

        var outcomes = new List<ReportOutcome>();

        for (var user = 100UL; user <= last; user++)
            outcomes.Add(_service.SubmitPlayerReport(Account(user), "Bob Smith", Lines, null).Outcome);

        Assert.AreEqual(ReportLimits.MaxOpenReportsPerTarget, outcomes.Count(outcome => outcome == ReportOutcome.Accepted));
        Assert.AreEqual(3, outcomes.Count(outcome => outcome == ReportOutcome.Limited));

        // House reports about their owner count against the same player.
        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitHouseReport(Account(last), GuidHelper.GetHouseGuid(5), GuidHelper.GetPlayerGuid(TargetCharacter), "x", "y").Outcome);

        // Another target is not affected, and closing one makes room.
        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Account(100), "Someone Else", Lines, null).Outcome);

        using (var dbContext = CreateDbContext())
            Assert.IsTrue(ReportQueries.TryClose(dbContext, Reports().First().Id, 7, null, _clock.GetUtcNow()));

        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Account(last), "Bob Smith", Lines, null).Outcome);
    }

    [TestMethod]
    public void ManyAccountsCannotPileReportsOnANameNobodyHas()
    {
        var last = 100UL + (ulong)ReportLimits.MaxOpenReportsPerTarget;

        for (var user = 100UL; user <= last; user++)
            AddAccount(user);

        var accepted = 0;

        for (var user = 100UL; user <= last; user++)
        {
            if (_service.SubmitPlayerReport(Account(user), "Nobody Here", Lines, null).Outcome == ReportOutcome.Accepted)
                accepted++;
        }

        Assert.AreEqual(ReportLimits.MaxOpenReportsPerTarget, accepted);
    }

    [TestMethod]
    public void ManyAccountsTogetherAreHeldToTheGlobalHourlyLimit_AndItResets()
    {
        var accounts = ReportLimits.GlobalReportsPerHour + 5;
        var last = 200UL + (ulong)accounts - 1;

        for (var user = 200UL; user <= last; user++)
            AddAccount(user);

        var accepted = 0;

        // Each account reports a different target so neither the duplicate rule nor the per-target cap is what stops it.
        for (var user = 200UL; user <= last; user++)
        {
            if (_service.SubmitPlayerReport(Account(user), "Target " + user, Lines, null).Outcome == ReportOutcome.Accepted)
                accepted++;
        }

        Assert.AreEqual(ReportLimits.GlobalReportsPerHour, accepted);

        // Bugs have their own allowance, so a flood of reports does not stop them.
        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitBugReport(Account(200), BugXml).Outcome);

        _clock.Advance(TimeSpan.FromMinutes(61));

        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Account(last), "Target again", Lines, null).Outcome);
    }

    [TestMethod]
    public void TheOpenCeilingStopsNewReportsUntilStaffCloseSome()
    {
        using (var dbContext = CreateDbContext())
        {
            for (var i = 0; i < ReportLimits.MaxOpenReports; i++)
                dbContext.Reports.Add(new DbReport { Kind = ReportKind.Bug, Created = _clock.GetUtcNow(), ReporterUserId = 9, ReporterName = "x", Status = ReportStatus.Open });

            dbContext.SaveChanges();
        }

        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null).Outcome);
        Assert.AreEqual(ReportOutcome.Limited, _service.SubmitBugReport(Reporter(), BugXml).Outcome);

        using (var dbContext = CreateDbContext())
            Assert.IsTrue(ReportQueries.TryClose(dbContext, Reports().First().Id, 7, null, _clock.GetUtcNow()));

        Assert.AreEqual(ReportOutcome.Accepted, _service.SubmitPlayerReport(Reporter(), "Bob Smith", Lines, null).Outcome);
    }

    [TestMethod]
    public void PruneClosed_DeletesOldClosedReportsOnly()
    {
        var now = _clock.GetUtcNow();

        using (var dbContext = CreateDbContext())
        {
            dbContext.Reports.AddRange(
                new DbReport { Kind = ReportKind.Bug, Created = now.AddDays(-90), ReporterName = "old closed", Status = ReportStatus.Closed, ClosedAt = now.AddDays(-80) },
                new DbReport { Kind = ReportKind.Bug, Created = now.AddDays(-90), ReporterName = "old open", Status = ReportStatus.Open },
                new DbReport { Kind = ReportKind.Bug, Created = now.AddDays(-5), ReporterName = "new closed", Status = ReportStatus.Closed, ClosedAt = now.AddDays(-1) },
                new DbReport { Kind = ReportKind.Bug, Created = now.AddDays(-40), ReporterName = "closed just past", Status = ReportStatus.Closed, ClosedAt = now - ReportLimits.ClosedRetention - TimeSpan.FromHours(1) },
                new DbReport { Kind = ReportKind.Bug, Created = now.AddDays(-40), ReporterName = "closed just inside", Status = ReportStatus.Closed, ClosedAt = now - ReportLimits.ClosedRetention + TimeSpan.FromHours(1) });
            dbContext.SaveChanges();
        }

        Assert.AreEqual(2, _service.PruneClosed());

        CollectionAssert.AreEquivalent(new[] { "old open", "new closed", "closed just inside" }, Reports().Select(report => report.ReporterName).ToList());
        Assert.AreEqual(0, _service.PruneClosed());
    }
}
