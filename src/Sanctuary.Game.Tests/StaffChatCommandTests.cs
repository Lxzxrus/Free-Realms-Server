using System;
using System.Linq;
using System.Runtime.CompilerServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.ChatCommands;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Commands that mint progress or act on other players are staff-only. The manager's role check is the only gate
/// (<c>!mod</c> relies on it too), so these pin each command's role and the check itself.
/// </summary>
/// <remarks>
/// A Player can't be built in a test, and a Debug build makes everyone Admin
/// (<c>ChatHelper.GetRoleFromFlags</c>), so this works on roles rather than players.
/// </remarks>
[TestClass]
public sealed class StaffChatCommandTests
{
    private static IChatCommand Command(string keyWord)
    {
        var types = typeof(ChatCommandManager).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface && typeof(IChatCommand).IsAssignableFrom(type));

        // KeyWord and RequiredRole are constants, so the command's services aren't needed to read them.
        foreach (var type in types)
        {
            var command = (IChatCommand)RuntimeHelpers.GetUninitializedObject(type);

            if (string.Equals(command.KeyWord, keyWord, StringComparison.OrdinalIgnoreCase))
                return command;
        }

        throw new AssertFailedException($"No chat command is called !{keyWord}.");
    }

    /// <summary>
    /// Upstream PR #120 opened both to every player; here they stay Admin, with the other commands that hand out
    /// rewards (docs/security/threat-model.md).
    /// </summary>
    [TestMethod]
    [DataRow("exp")]
    [DataRow("reward")]
    public void GrantCommands_AreAdminOnly(string keyWord)
    {
        var command = Command(keyWord);

        Assert.AreEqual(ChatCommandRole.Admin, command.RequiredRole);
        Assert.IsFalse(ChatCommandManager.CanUse(ChatCommandRole.Player, command), "A player is refused.");
        Assert.IsFalse(ChatCommandManager.CanUse(ChatCommandRole.Mod, command), "A mod is refused.");
        Assert.IsTrue(ChatCommandManager.CanUse(ChatCommandRole.Admin, command));
    }

    [TestMethod]
    [DataRow("mod")]
    [DataRow("teleport")]
    [DataRow("admin")]
    [DataRow("collection")]
    [DataRow("navnpcto")]
    [DataRow("npc")]
    [DataRow("script")]
    [DataRow("exp")]
    [DataRow("reward")]
    [DataRow("job")]
    public void StaffCommands_RefusePlayers(string keyWord)
    {
        var command = Command(keyWord);

        Assert.IsTrue(command.RequiredRole >= ChatCommandRole.Mod, $"!{keyWord} is open to players.");
        Assert.IsFalse(ChatCommandManager.CanUse(ChatCommandRole.Player, command));
    }

    /// <summary><c>!job</c> uses <c>!mod</c>'s check: mods and admins, never players.</summary>
    [TestMethod]
    [DataRow("mod")]
    [DataRow("job")]
    public void ModCommands_AreForModsAndAdmins(string keyWord)
    {
        var command = Command(keyWord);

        Assert.IsFalse(ChatCommandManager.CanUse(ChatCommandRole.Player, command));
        Assert.IsTrue(ChatCommandManager.CanUse(ChatCommandRole.Mod, command));
        Assert.IsTrue(ChatCommandManager.CanUse(ChatCommandRole.Admin, command));
    }
}
