using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Database.Sqlite;
using Sanctuary.Game.ChatCommands;

namespace Sanctuary.Game.Tests;

/// <summary>
/// <c>!mod</c>'s arguments, the reason and warning text, the rank rule and the history listing. A Player can't be built
/// in a test (see <see cref="StaffChatCommandTests"/>), so these test the parts the subcommands are made of.
/// </summary>
[TestClass]
public sealed class ModCommandTests
{
    private static string[] Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static ModTarget Parse(string text, bool allowMinutes = true, int maxText = ModArguments.MaxReasonLength)
    {
        Assert.IsTrue(ModArguments.TryParse(Words(text), allowMinutes, maxText, out var target, out var error), $"\"{text}\" was refused: {error}");
        return target;
    }

    [TestMethod]
    [DataRow("Bob", "Bob", null, null)]
    [DataRow("Bob 30", "Bob", 30, null)]
    [DataRow("Bob Smith", "Bob Smith", null, null)]
    [DataRow("Bob Smith 30", "Bob Smith", 30, null)]
    [DataRow("Bob | spam", "Bob", null, "spam")]
    [DataRow("Bob 30 | spam", "Bob", 30, "spam")]
    [DataRow("Bob Smith | spam", "Bob Smith", null, "spam")]
    [DataRow("Bob Smith 30 | being rude to others", "Bob Smith", 30, "being rude to others")]
    [DataRow("Bob Smith 30 |being rude", "Bob Smith", 30, "being rude")]
    [DataRow("Bob Smith 30| being rude", "Bob Smith", 30, "being rude")]
    [DataRow("Bob Smith|spam", "Bob Smith", null, "spam")]
    [DataRow("Bob | a | b", "Bob", null, "a | b")]
    [DataRow("Bob |", "Bob", null, null)]
    [DataRow("Bob |    ", "Bob", null, null)]
    public void Parse_SplitsNameMinutesAndReason(string text, string name, int? minutes, string? reason)
    {
        var target = Parse(text);

        Assert.AreEqual(name, target.Name);
        Assert.AreEqual(minutes, target.Minutes);
        Assert.AreEqual(reason, target.Reason);
        Assert.AreEqual(text.Contains('|'), target.HasSeparator);
    }

    [TestMethod]
    public void Parse_WithoutMinutes_KeepsATrailingNumberInTheName()
    {
        var target = Parse("Bob Smith 30 | spam", allowMinutes: false);

        Assert.AreEqual("Bob Smith 30", target.Name);
        Assert.IsNull(target.Minutes);
        Assert.AreEqual("spam", target.Reason);
    }

    [TestMethod]
    public void Parse_ANumberAloneIsTheName_NotMinutes()
    {
        var target = Parse("30");

        Assert.AreEqual("30", target.Name);
        Assert.IsNull(target.Minutes);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("| spam")]
    [DataRow("   |")]
    public void Parse_NeedsAPlayer(string text)
    {
        Assert.IsFalse(ModArguments.TryParse(Words(text), true, 200, out _, out var error));
        Assert.IsNull(error, "No player is a usage error, not a duration error.");
    }

    [TestMethod]
    [DataRow("Bob 0")]
    [DataRow("Bob -5 | spam")]
    public void Parse_RefusesMinutesThatAreNotPositive(string text)
    {
        Assert.IsFalse(ModArguments.TryParse(Words(text), true, 200, out _, out var error));
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public void Parse_CapsTheReason()
    {
        var target = Parse("Bob 30 | " + new string('x', 500));

        Assert.AreEqual(ModArguments.MaxReasonLength, target.Reason!.Length);
        Assert.AreEqual(DbUser.MaxReasonLength, target.Reason.Length, "A ban reason fits its column.");
    }

    [TestMethod]
    public void Parse_CapsAWarningAtItsOwnLimit()
    {
        var target = Parse("Bob | " + new string('x', 500), allowMinutes: false, maxText: ModArguments.MaxWarnLength);

        Assert.AreEqual(ModArguments.MaxWarnLength, target.Reason!.Length);
    }

    [TestMethod]
    public void CleanText_DropsControlAndInvisibleCharacters_AndTidiesWhitespace()
    {
        // A bell, a tab, a line break, a zero-width space, and a right-to-left override that could flip what follows.
        var cleaned = ModArguments.CleanText("  be\u0007ll\ttab\r\nnew​line ‮flipped   spaces  ", 200);

        Assert.AreEqual("bell tab newline flipped spaces", cleaned);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   \t ")]
    [DataRow("\u0007​‮")]
    public void CleanText_ReturnsNullWhenNothingIsLeft(string? text)
    {
        Assert.IsNull(ModArguments.CleanText(text, 200));
    }

    [TestMethod]
    public void CleanText_NeverSplitsASurrogatePair_OrRunsPastTheCap()
    {
        var text = new string('a', 199) + "\U0001F600" + "tail";

        var cleaned = ModArguments.CleanText(text, 200)!;

        Assert.AreEqual(199, cleaned.Length, "The emoji needs two characters and only one is left.");
        Assert.IsFalse(cleaned.Any(char.IsSurrogate));
    }

    [TestMethod]
    public void CleanText_DoesNotEndOnASpaceWhenItIsCutThere()
    {
        var cleaned = ModArguments.CleanText(new string('a', 9) + " " + new string('b', 20), 10)!;

        Assert.AreEqual(new string('a', 9), cleaned);
    }

    /// <summary>The rule from <c>!mod</c> before this change, kept: the invoker's role must be above the target's.</summary>
    [TestMethod]
    [DataRow(ChatCommandRole.Admin, ChatCommandRole.Mod, true)]
    [DataRow(ChatCommandRole.Admin, ChatCommandRole.Player, true)]
    [DataRow(ChatCommandRole.Mod, ChatCommandRole.Player, true)]
    [DataRow(ChatCommandRole.Admin, ChatCommandRole.Admin, false)]
    [DataRow(ChatCommandRole.Mod, ChatCommandRole.Mod, false)]
    [DataRow(ChatCommandRole.Mod, ChatCommandRole.Admin, false)]
    [DataRow(ChatCommandRole.Player, ChatCommandRole.Player, false)]
    [DataRow(ChatCommandRole.Player, ChatCommandRole.Mod, false)]
    public void RankRule_InvokerMustOutrankTheTarget(ChatCommandRole invoker, ChatCommandRole target, bool allowed)
    {
        Assert.AreEqual(allowed, ModChatCommand.IsAuthorizedAgainstTarget(invoker, target));
    }

    private static DbStaffAction Action(ulong id, string action, string? details, string staff = "Ariel Mod") => new()
    {
        Id = id,
        Created = new DateTimeOffset(2026, 11, 25, 14, 3, 0, TimeSpan.Zero).AddMinutes(id),
        StaffUserId = 1,
        StaffName = staff,
        Command = "mod",
        Action = action,
        TargetUserId = 5,
        Details = details
    };

    [TestMethod]
    public void FormatHistory_IsOneShortLinePerAction_AndBounded()
    {
        var recent = Enumerable.Range(1, ModChatCommand.HistoryLimit)
            .Select(i => Action((ulong)(100 - i), "Ban", new string('x', 900) + "\r\nforged line"))
            .ToList();

        var lines = ModChatCommand.FormatHistory("Bob Smith", 42, recent);

        Assert.AreEqual(ModChatCommand.HistoryLimit + 1, lines.Count, "A heading and one line each.");
        StringAssert.Contains(lines[0], "42");
        StringAssert.Contains(lines[0], "Bob Smith");
        Assert.IsTrue(lines.All(line => line.Length <= 140), "Every line is short.");
        Assert.IsFalse(lines.Any(line => line.Contains('\n') || line.Contains('\r')), "Staff text cannot add lines.");
        StringAssert.Contains(lines[1], "2026-11-25 ");
        StringAssert.Contains(lines[1], "Ban by Ariel Mod");
    }

    [TestMethod]
    public void FormatHistory_WithNoActionsSaysSo()
    {
        var lines = ModChatCommand.FormatHistory("Bob Smith", 0, []);

        Assert.AreEqual(1, lines.Count);
        StringAssert.Contains(lines[0], "0");
    }

    [TestMethod]
    public void LoadHistory_ReturnsTheLastTenOfThatAccountNewestFirst_WithTheCount()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var dbContext = new SqliteDatabaseContext(new DbContextOptionsBuilder().UseSqlite(connection).Options);
        dbContext.Database.Migrate();

        for (var i = 1; i <= 15; i++)
            dbContext.StaffActions.Add(ToNew(Action(0, $"Action{i}", null), targetUserId: 5));

        for (var i = 1; i <= 3; i++)
            dbContext.StaffActions.Add(ToNew(Action(0, "Other", null), targetUserId: 6));

        dbContext.StaffActions.Add(ToNew(Action(0, "NoTarget", null), targetUserId: null));
        dbContext.SaveChanges();

        var (total, recent) = ModChatCommand.LoadHistory(dbContext, 5);

        Assert.AreEqual(15, total);
        Assert.AreEqual(ModChatCommand.HistoryLimit, recent.Count);
        CollectionAssert.AreEqual(
            Enumerable.Range(6, 10).Reverse().Select(i => $"Action{i}").ToList(),
            recent.Select(a => a.Action).ToList());

        Assert.AreEqual(3, ModChatCommand.LoadHistory(dbContext, 6).Total);
        Assert.AreEqual(0, ModChatCommand.LoadHistory(dbContext, 7).Total);
    }

    private static DbStaffAction ToNew(DbStaffAction source, ulong? targetUserId) => new()
    {
        Created = source.Created,
        StaffUserId = source.StaffUserId,
        StaffName = source.StaffName,
        Command = source.Command,
        Action = source.Action,
        TargetUserId = targetUserId
    };
}
