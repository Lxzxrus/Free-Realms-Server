using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Every move to another zone that a player's packet or chat command asks for goes through
/// <see cref="Helpers.ZoneTeleports.Request"/>, so it can't be repeated faster than the limit there. The packet handlers
/// can't be run from a test (they are in the Gateway, with its connection and database), so this reads their source
/// instead: in each file, the calls that move a player to another zone may not outnumber the paced requests.
/// It is a count, so it can't prove each call sits inside its request; it catches a move added with none.
/// </summary>
[TestClass]
public sealed class ZoneMovePacingTests
{
    private static readonly Regex ZoneMove = new(@"\.(TeleportToZone|ReturnToStartingZone|EnterHouse|LeaveHouse)\(", RegexOptions.Compiled);

    private const string PacedRequest = "ZoneTeleports.Request(";

    /// <summary>Files whose moves are the server's own, with the reason. A player can't ask for these at will.</summary>
    private static readonly Dictionary<string, string> ServersOwnMoves = new()
    {
        ["Sanctuary.Gateway/GatewayServer.cs"] = "sends a player home when their fishing session fails",
    };

    private static string FindSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Sanctuary.Gateway", "Handlers"))
                && Directory.Exists(Path.Combine(directory.FullName, "Sanctuary.Game", "ChatCommands")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("src not found above the test output.");
    }

    private static IEnumerable<string> PlayerDrivenFiles(string source)
    {
        var separator = Path.DirectorySeparatorChar;

        var gateway = Directory.EnumerateFiles(Path.Combine(source, "Sanctuary.Gateway"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}bin{separator}") && !file.Contains($"{separator}obj{separator}"));

        var chatCommands = Directory.EnumerateFiles(Path.Combine(source, "Sanctuary.Game", "ChatCommands"), "*.cs");

        return gateway.Concat(chatCommands);
    }

    private static int Count(string text, string value)
    {
        var count = 0;

        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;

        return count;
    }

    [TestMethod]
    public void EveryPlayerRequestedZoneMoveIsPaced()
    {
        var source = FindSource();
        var unpaced = new List<string>();
        var filesWithMoves = 0;

        foreach (var file in PlayerDrivenFiles(source))
        {
            var text = File.ReadAllText(file);
            var moves = ZoneMove.Matches(text).Count;

            if (moves == 0)
                continue;

            filesWithMoves++;

            var name = Path.GetRelativePath(source, file).Replace('\\', '/');

            if (ServersOwnMoves.ContainsKey(name))
                continue;

            var requests = Count(text, PacedRequest);

            if (requests < moves)
                unpaced.Add($"{name}: {moves} zone moves, {requests} paced requests");
        }

        Assert.IsTrue(filesWithMoves >= 5, "The scan found too few files with zone moves: has a method been renamed?");
        Assert.AreEqual(0, unpaced.Count, "Zone moves without ZoneTeleports.Request: " + string.Join("; ", unpaced));
    }

    [TestMethod]
    public void TheServersOwnMovesStillExist()
    {
        var source = FindSource();

        // An entry whose file no longer moves anyone should be removed, so the list stays honest.
        foreach (var name in ServersOwnMoves.Keys)
        {
            var file = Path.Combine(source, name);

            Assert.IsTrue(File.Exists(file), name + " is listed but missing.");
            Assert.IsTrue(ZoneMove.IsMatch(File.ReadAllText(file)), name + " is listed but moves no player.");
        }
    }
}
