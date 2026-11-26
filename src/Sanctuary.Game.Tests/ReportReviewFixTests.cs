using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database.Entities;
using Sanctuary.Game.ChatCommands;
using Sanctuary.Game.Reports;

namespace Sanctuary.Game.Tests;

/// <summary>Who is told of a new report, the audit-line text, and the limited log of refused report packets.</summary>
[TestClass]
public sealed class ReportReviewFixTests
{
    private sealed record Staff(string Name, ulong UserId, ChatCommandRole Role);

    private static readonly Staff Player = new("player", 1, ChatCommandRole.Player);
    private static readonly Staff ModA = new("modA", 10, ChatCommandRole.Mod);
    private static readonly Staff ModB = new("modB", 11, ChatCommandRole.Mod);
    private static readonly Staff AdminA = new("adminA", 20, ChatCommandRole.Admin);
    private static readonly Staff AdminB = new("adminB", 21, ChatCommandRole.Admin);

    private static readonly Staff[] Online = [Player, ModA, ModB, AdminA, AdminB];

    private static List<string> Told(ulong? targetUserId, ChatCommandRole? targetRole, ReportKind kind = ReportKind.Player)
    {
        var report = new DbReport { Id = 1, Kind = kind, ReporterName = "r", TargetUserId = targetUserId };

        return ReportDesk.SelectRecipients(Online, staff => staff.UserId, staff => staff.Role, report, targetRole).Select(staff => staff.Name).ToList();
    }

    [TestMethod]
    public void AReportAboutAPlayer_IsToldToAllStaff_AndNeverToPlayers()
    {
        CollectionAssert.AreEqual(new[] { "modA", "modB", "adminA", "adminB" }, Told(500, ChatCommandRole.Player));
    }

    [TestMethod]
    public void AReportAboutNobodyKnown_OrABug_IsToldToAllStaff()
    {
        CollectionAssert.AreEqual(new[] { "modA", "modB", "adminA", "adminB" }, Told(null, null));
        CollectionAssert.AreEqual(new[] { "modA", "modB", "adminA", "adminB" }, Told(null, null, ReportKind.Bug));
        CollectionAssert.AreEqual(new[] { "modA", "modB", "adminA", "adminB" }, Told(500, null), "a deleted account is open to all staff");
    }

    [TestMethod]
    public void AReportAboutAModerator_IsNotToldToThatModeratorOrToOtherModerators()
    {
        // ModA is the target: not told. ModB does not outrank them: not told. Admins are.
        CollectionAssert.AreEqual(new[] { "adminA", "adminB" }, Told(ModA.UserId, ChatCommandRole.Mod));
    }

    [TestMethod]
    public void AReportAboutAnAdmin_IsToldToOtherAdminsOnly()
    {
        CollectionAssert.AreEqual(new[] { "adminB" }, Told(AdminA.UserId, ChatCommandRole.Admin));
    }

    [TestMethod]
    public void AReportAboutAStaffMembersAccount_IsNotToldToThemWhateverRoleIsGiven()
    {
        // Also when the role could not be read and so was taken to be the highest: the account's own owner is never told.
        CollectionAssert.DoesNotContain(Told(ModA.UserId, ChatCommandRole.Admin), "modA");
        CollectionAssert.DoesNotContain(Told(AdminA.UserId, ChatCommandRole.Mod), "adminA");
    }

    [TestMethod]
    public void AuditLineText_CannotCloseItsQuotes()
    {
        var forged = "x\", Actor: \"Nate\" (user 1), Target: \"y\\z";
        var safe = StaffActionLog.ForAuditLine(forged);

        Assert.IsFalse(safe.Contains('"'));
        Assert.IsFalse(safe.Contains('\\'));
        Assert.AreEqual(forged.Length, safe.Length);
        Assert.AreEqual(string.Empty, StaffActionLog.ForAuditLine(null));
        Assert.AreEqual("Bob Smith", StaffActionLog.ForAuditLine("Bob Smith"));
    }

    [TestMethod]
    public void LogRateLimiter_AllowsTheFirstFew_ThenOnePerIntervalWithTheCountHeldBack()
    {
        long now = 1000;
        var limiter = new LogRateLimiter(3, TimeSpan.FromSeconds(60), () => now);

        for (var i = 0; i < 3; i++)
        {
            Assert.IsTrue(limiter.TryAllow(out var held));
            Assert.AreEqual(0, held);
        }

        Assert.IsFalse(limiter.TryAllow(out _));
        Assert.IsFalse(limiter.TryAllow(out _));

        now += 59_000;
        Assert.IsFalse(limiter.TryAllow(out _));

        now += 1_000;
        Assert.IsTrue(limiter.TryAllow(out var suppressed));
        Assert.AreEqual(3, suppressed);

        Assert.IsFalse(limiter.TryAllow(out _));
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lines.Add((logLevel, formatter(state, exception)));
    }

    [TestMethod]
    public void ARefusedReportPacket_IsLoggedAsAWarningWithItsBytes_ButOnlyTheFirstFewInAFlood()
    {
        var logger = new CapturingLogger();
        var limiter = new LogRateLimiter(2, TimeSpan.FromMinutes(1), () => 0);

        for (var i = 0; i < 50; i++)
            ReportRefusalLog.Refused(logger, limiter, "ReportPacketReportPlayer", [0x44, 0x00, 0x01, 0xAB]);

        Assert.AreEqual(2, logger.Lines.Count);
        Assert.AreEqual(LogLevel.Warning, logger.Lines[0].Level);
        StringAssert.Contains(logger.Lines[0].Message, "ReportPacketReportPlayer");
        StringAssert.Contains(logger.Lines[0].Message, "440001AB");
    }

    [TestMethod]
    public void ARefusedReportPacketsBytesAreCapped()
    {
        var logger = new CapturingLogger();

        ReportRefusalLog.Refused(logger, new LogRateLimiter(1, TimeSpan.FromMinutes(1), () => 0), "big", new byte[100_000]);

        Assert.IsTrue(logger.Lines.Single().Message.Length < 2000);
    }
}
