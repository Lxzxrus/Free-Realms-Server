using System;
using System.Linq;
using System.Text;

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Reports;
using Sanctuary.Packet.Common.Chat;

namespace Sanctuary.Game.Tests;

/// <summary>The bug XML reader, the server's chat record and the report limits.</summary>
[TestClass]
public sealed class ReportSupportTests
{
    // ---- The bug XML ----

    private const string RealisticXml =
        "<?xml version=\"1.0\"?><BugSubmission Category=\"Gameplay\" SubCategory=\"Quests\" Severity=\"3\" BugDescription=\"The quest npc is gone &amp; I am stuck\">" +
        "<PlayerData PlayerId=\"33\" Gender=\"1\" LocationX=\"1.0\" ClientVersion=\"1.910.1.530630\"/></BugSubmission>";

    [TestMethod]
    public void BugXml_ReadsTheFourFields()
    {
        Assert.IsTrue(BugReportXml.TryParse(RealisticXml, out var fields));
        Assert.AreEqual("Gameplay", fields.Category);
        Assert.AreEqual("Quests", fields.SubCategory);
        Assert.AreEqual("3", fields.Severity);
        Assert.AreEqual("The quest npc is gone & I am stuck", fields.Description);
    }

    [TestMethod]
    public void BugXml_MissingAttributesAreEmpty()
    {
        Assert.IsTrue(BugReportXml.TryParse("<BugSubmission Category=\"Other\"/>", out var fields));
        Assert.AreEqual("Other", fields.Category);
        Assert.AreEqual(string.Empty, fields.SubCategory);
        Assert.AreEqual(string.Empty, fields.Severity);
        Assert.AreEqual(string.Empty, fields.Description);
    }

    [TestMethod]
    public void BugXml_AttributeNamesAreMatchedExactly()
    {
        // A longer name that ends the same must not stand in for Category (the regex this replaces matched loosely).
        Assert.IsTrue(BugReportXml.TryParse("<BugSubmission SubCategory=\"Sub\" XCategory=\"X\"/>", out var fields));
        Assert.AreEqual(string.Empty, fields.Category);
        Assert.AreEqual("Sub", fields.SubCategory);
    }

    [TestMethod]
    public void BugXml_CleansAndCapsEachField()
    {
        var xml = $"<BugSubmission Category=\"{new string('c', 500)}\" Severity=\"a&#10;b‮c\" BugDescription=\"{new string('d', 5000)}\"/>";

        Assert.IsTrue(BugReportXml.TryParse(xml, out var fields));
        Assert.AreEqual(ReportLimits.MaxBugCategoryLength, fields.Category.Length);
        Assert.AreEqual(ReportLimits.MaxBugDescriptionLength, fields.Description.Length);
        Assert.AreEqual("a bc", fields.Severity);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not xml at all")]
    [DataRow("<BugSubmission Category=\"x\"")]
    [DataRow("<Other Category=\"x\"/>")]
    [DataRow("<Wrapper><BugSubmission Category=\"x\"/></Wrapper>")]
    public void BugXml_RefusesWhatIsNotTheClientsDocument(string xml)
    {
        Assert.IsFalse(BugReportXml.TryParse(xml, out _));
    }

    [TestMethod]
    public void BugXml_KeepsTheRootsFieldsWhenTheRestIsBroken()
    {
        // The fields are on the root tag, which is read first. A fault further down (the client's machine snapshot has
        // free text) must not lose what the player wrote.
        Assert.IsTrue(BugReportXml.TryParse("<BugSubmission Category=\"Art\" BugDescription=\"hole\"><PlayerData Gpu=\"A & B\"></BugSubmission>", out var fields));
        Assert.AreEqual("Art", fields.Category);
        Assert.AreEqual("hole", fields.Description);
    }

    [TestMethod]
    public void BugXml_RefusesADtd()
    {
        const string xml = "<!DOCTYPE BugSubmission [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><BugSubmission Category=\"&xxe;\"/>";

        Assert.IsFalse(BugReportXml.TryParse(xml, out _));
    }

    [TestMethod]
    public void BugXml_RefusesAnEntityExpansionBomb()
    {
        var xml = "<!DOCTYPE BugSubmission [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">" +
            "<!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\"><!ENTITY d \"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;\">]>" +
            "<BugSubmission BugDescription=\"&d;&d;&d;&d;&d;&d;&d;&d;\"/>";

        Assert.IsFalse(BugReportXml.TryParse(xml, out _));
    }

    [TestMethod]
    public void BugXml_RefusesADocumentOverTheCapWithoutReadingIt()
    {
        var xml = "<BugSubmission BugDescription=\"" + new string('x', ReportLimits.MaxBugXmlLength) + "\"/>";

        Assert.IsFalse(BugReportXml.TryParse(xml, out _));
    }

    [TestMethod]
    public void BugXml_RawCopyIsCleanedAndCut()
    {
        var copy = BugReportXml.RawCopy("<bad>\n‮" + new string('x', 10_000));

        Assert.AreEqual(ReportLimits.MaxRawBugLength, copy.Length);
        Assert.IsFalse(copy.Contains('\n'));
        Assert.IsFalse(copy.Contains('‮'));
        Assert.AreEqual(string.Empty, BugReportXml.RawCopy(null));
    }

    // ---- The server's chat record ----

    [TestMethod]
    public void ChatEvidence_KeepsTheLastTenOldestFirst()
    {
        var evidence = new ChatEvidence(new ManualTimeProvider());

        for (var i = 1; i <= 15; i++)
            evidence.Add(ChatChannel.WorldSay, "line " + i);

        var lines = evidence.Snapshot();

        Assert.AreEqual(ChatEvidence.MaxLines, lines.Count);
        Assert.AreEqual("line 6", lines[0].Text);
        Assert.AreEqual("line 15", lines[^1].Text);
    }

    [TestMethod]
    public void ChatEvidence_CutsEachLineAndCleansIt()
    {
        var evidence = new ChatEvidence(new ManualTimeProvider());

        evidence.Add(ChatChannel.WorldSay, new string('a', 1000));
        evidence.Add(ChatChannel.WorldSay, "two\nlines‮");
        evidence.Add(ChatChannel.WorldSay, "   ");
        evidence.Add(ChatChannel.WorldSay, null);

        var lines = evidence.Snapshot();

        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual(ChatEvidence.MaxTextLength, lines[0].Text.Length);
        Assert.AreEqual("two lines", lines[1].Text);
    }

    [TestMethod]
    public void ChatEvidence_DropsLinesOlderThanThirtyMinutes()
    {
        var clock = new ManualTimeProvider();
        var evidence = new ChatEvidence(clock);

        evidence.Add(ChatChannel.WorldSay, "old");
        clock.Advance(TimeSpan.FromMinutes(20));
        evidence.Add(ChatChannel.WorldSay, "newer");
        clock.Advance(TimeSpan.FromMinutes(11));

        Assert.AreEqual("newer", evidence.Snapshot().Single().Text);

        clock.Advance(TimeSpan.FromMinutes(20));

        Assert.AreEqual(0, evidence.Snapshot().Count);
    }

    [TestMethod]
    public void ChatEvidence_NeverKeepsAPrivateTell()
    {
        var evidence = new ChatEvidence(new ManualTimeProvider());

        evidence.Add(ChatChannel.WorldSay, "public line");
        evidence.Add(ChatChannel.Tell, "my real name is Jo and I live at 12 Elm Street");

        var lines = evidence.Snapshot();

        Assert.AreEqual(1, lines.Count);
        Assert.AreEqual("public line", lines[0].Text);
        Assert.IsFalse(ChatEvidence.Format(lines, DateTimeOffset.UtcNow, 1000).Contains("Elm Street"));
    }

    [TestMethod]
    public void ChatEvidence_ClearEmptiesIt()
    {
        var evidence = new ChatEvidence(new ManualTimeProvider());

        evidence.Add(ChatChannel.WorldSay, "hello");
        evidence.Clear();

        Assert.AreEqual(0, evidence.Snapshot().Count);
    }

    [TestMethod]
    public void ChatEvidence_FormatShowsEachLineAndStaysWithinTheLength()
    {
        var clock = new ManualTimeProvider();
        var evidence = new ChatEvidence(clock);

        for (var i = 0; i < 10; i++)
            evidence.Add(ChatChannel.WorldShout, new string('z', 256));

        clock.Advance(TimeSpan.FromSeconds(5));

        var text = ChatEvidence.Format(evidence.Snapshot(), clock.GetUtcNow(), 1000);

        Assert.IsTrue(text.Length <= 1000);
        StringAssert.Contains(text, "WorldShout");
        StringAssert.Contains(text, "5s ago");
        StringAssert.Contains(ChatEvidence.Format([], clock.GetUtcNow(), 1000), "no chat");
    }

    // ---- The limits ----

    [TestMethod]
    public void Limiter_AllowsFivePerHourThenRefusesUntilTheHourPasses()
    {
        var clock = new ManualTimeProvider();
        var limiter = new ReportRateLimiter(clock);

        for (var i = 0; i < ReportLimits.ReportsPerHour; i++)
        {
            Assert.IsTrue(limiter.CanAccept(1, ReportBudget.Report));
            limiter.RecordAccepted(1, ReportBudget.Report);
        }

        Assert.IsFalse(limiter.CanAccept(1, ReportBudget.Report));
        Assert.IsTrue(limiter.CanAccept(2, ReportBudget.Report), "another account has its own allowance");

        clock.Advance(TimeSpan.FromMinutes(61));

        Assert.IsTrue(limiter.CanAccept(1, ReportBudget.Report));
    }

    [TestMethod]
    public void Limiter_AllowsTwentyPerDay()
    {
        var clock = new ManualTimeProvider();
        var limiter = new ReportRateLimiter(clock);

        // Five an hour, spread over four hours: twenty in all, then the day's limit stops a fifth hour.
        for (var hour = 0; hour < 4; hour++)
        {
            for (var i = 0; i < 5; i++)
            {
                Assert.IsTrue(limiter.CanAccept(1, ReportBudget.Report));
                limiter.RecordAccepted(1, ReportBudget.Report);
            }

            clock.Advance(TimeSpan.FromMinutes(61));
        }

        Assert.IsFalse(limiter.CanAccept(1, ReportBudget.Report));

        clock.Advance(TimeSpan.FromHours(21));

        Assert.IsTrue(limiter.CanAccept(1, ReportBudget.Report));
    }

    [TestMethod]
    public void Limiter_BugReportsHaveTheirOwnAllowance()
    {
        var limiter = new ReportRateLimiter(new ManualTimeProvider());

        for (var i = 0; i < ReportLimits.ReportsPerHour; i++)
            limiter.RecordAccepted(1, ReportBudget.Report);

        Assert.IsFalse(limiter.CanAccept(1, ReportBudget.Report));
        Assert.IsTrue(limiter.CanAccept(1, ReportBudget.Bug));

        for (var i = 0; i < ReportLimits.BugsPerHour; i++)
            limiter.RecordAccepted(1, ReportBudget.Bug);

        Assert.IsFalse(limiter.CanAccept(1, ReportBudget.Bug));
    }

    [TestMethod]
    public void Limiter_StopsAnAccountThatKeepsTrying()
    {
        var clock = new ManualTimeProvider();
        var limiter = new ReportRateLimiter(clock);

        for (var i = 0; i < ReportLimits.AttemptsPerWindow; i++)
            Assert.IsTrue(limiter.TryBeginAttempt(1));

        Assert.IsFalse(limiter.TryBeginAttempt(1));
        Assert.IsTrue(limiter.TryBeginAttempt(2));

        clock.Advance(ReportLimits.AttemptWindow + TimeSpan.FromSeconds(1));

        Assert.IsTrue(limiter.TryBeginAttempt(1));
    }

    [TestMethod]
    public void Limiter_ForgetsAccountsThatHaveGoneQuiet()
    {
        var clock = new ManualTimeProvider();
        var limiter = new ReportRateLimiter(clock);

        for (ulong user = 0; user < 500; user++)
            limiter.TryBeginAttempt(user);

        Assert.AreEqual(500, limiter.AccountCount);

        clock.Advance(TimeSpan.FromHours(2));
        limiter.TryBeginAttempt(9999);

        Assert.AreEqual(1, limiter.AccountCount);
    }

    [TestMethod]
    public void Limiter_AllAccountsTogetherAreHeldToTheGlobalHourlyAndDailyLimits()
    {
        var clock = new ManualTimeProvider();
        var limiter = new ReportRateLimiter(clock);

        // One report each from many accounts: no account is near its own limit.
        for (ulong user = 1; user <= (ulong)ReportLimits.GlobalReportsPerHour; user++)
        {
            Assert.IsTrue(limiter.CanAccept(user, ReportBudget.Report));
            limiter.RecordAccepted(user, ReportBudget.Report);
        }

        Assert.IsFalse(limiter.CanAccept(9999, ReportBudget.Report), "a new account is held by the global limit");
        Assert.IsTrue(limiter.CanAccept(9999, ReportBudget.Bug), "bugs have their own global allowance");

        // The daily limit: 30 an hour, for as many hours as it takes to reach 200.
        var total = ReportLimits.GlobalReportsPerHour;
        ulong next = 1000;

        while (total < ReportLimits.GlobalReportsPerDay)
        {
            clock.Advance(TimeSpan.FromMinutes(61));

            for (var i = 0; i < ReportLimits.GlobalReportsPerHour && total < ReportLimits.GlobalReportsPerDay; i++, total++)
            {
                Assert.IsTrue(limiter.CanAccept(next, ReportBudget.Report));
                limiter.RecordAccepted(next++, ReportBudget.Report);
            }
        }

        clock.Advance(TimeSpan.FromMinutes(61));

        Assert.IsFalse(limiter.CanAccept(next, ReportBudget.Report), "a day's worth has been taken");

        clock.Advance(TimeSpan.FromHours(24));

        Assert.IsTrue(limiter.CanAccept(next, ReportBudget.Report));
    }

    [TestMethod]
    public void Limiter_AttemptsFromAllAccountsTogetherAreBounded()
    {
        var limiter = new ReportRateLimiter(new ManualTimeProvider());

        var allowed = 0;

        for (ulong user = 1; user <= (ulong)ReportLimits.GlobalAttemptsPerWindow + 50; user++)
        {
            if (limiter.TryBeginAttempt(user))
                allowed++;
        }

        Assert.AreEqual(ReportLimits.GlobalAttemptsPerWindow, allowed);
    }

    [TestMethod]
    public void Queries_NewestFirstSortsByIdOnSqlite_AndByThePlainKeyOnMySql()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        using var sqlite = new Sanctuary.Database.Sqlite.SqliteDatabaseContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);

        var mySql = new Sanctuary.Database.MySql.MySqlDatabaseContext(new DbContextOptionsBuilder()
            .UseMySql("Server=localhost;Database=unused", new MariaDbServerVersion(new Version(10, 11, 0))).Options);

        var sqliteSql = ReportQueries.NewestFirst(sqlite, sqlite.Reports).ToQueryString();
        var mySqlSql = ReportQueries.NewestFirst(mySql, mySql.Reports).ToQueryString();

        StringAssert.Contains(sqliteSql, "CAST", "SQLite can't sort an unsigned column, so it sorts by a cast");
        Assert.IsFalse(mySqlSql.Contains("CAST", StringComparison.OrdinalIgnoreCase), mySqlSql);
        StringAssert.Contains(mySqlSql, "ORDER BY `r`.`Id` DESC");
    }
}
