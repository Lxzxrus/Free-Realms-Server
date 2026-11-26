using System;
using System.IO;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A connection's queue limits are low until <c>UdpConnection.RaiseQueuedPacketLimits</c> is called, and what keeps a
/// stranger from filling a queue is that only a successful login calls it. The Gateway's handlers aren't unit-tested, so
/// this reads their source: one call, in the login handler, after the player has been made.
/// </summary>
[TestClass]
public class QueueLimitRaiseTests
{
    private const string Raise = "RaiseQueuedPacketLimits(";

    private static string FindSource()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "Sanctuary.Gateway", "Handlers"))
                && Directory.Exists(Path.Combine(directory.FullName, "Sanctuary.Login", "Handlers")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("src not found above the test output.");
    }

    private static int Count(string text, string value)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;

        return count;
    }

    /// <summary>The source with its comment lines taken out, so a call that is commented out doesn't count.</summary>
    private static string Code(string file) =>
        string.Join('\n', File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [TestMethod]
    public void OnlyTheGatewaysLoginHandlerRaisesTheQueueLimits()
    {
        var source = FindSource();
        var separator = Path.DirectorySeparatorChar;

        // every project but the library that declares the method, and the tests that call it on their own connections
        var callers = Directory.EnumerateDirectories(source, "Sanctuary.*")
            .Where(project => !project.EndsWith(".Tests", StringComparison.Ordinal) && Path.GetFileName(project) != "Sanctuary.UdpLibrary")
            .SelectMany(project => Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{separator}bin{separator}") && !file.Contains($"{separator}obj{separator}"))
            .Select(file => (File: Path.GetRelativePath(source, file).Replace(separator, '/'), Calls: Count(Code(file), Raise)))
            .Where(found => found.Calls > 0)
            .ToList();

        Assert.HasCount(1, callers, "callers: " + string.Join(", ", callers.Select(found => found.File)));
        Assert.AreEqual("Sanctuary.Gateway/Handlers/PacketLoginHandler.cs", callers[0].File);
        Assert.AreEqual(1, callers[0].Calls);
    }

    [TestMethod]
    public void TheLimitsAreRaisedOnlyAfterThePlayerHasBeenMade()
    {
        var handler = Code(Path.Combine(FindSource(), "Sanctuary.Gateway", "Handlers", "PacketLoginHandler.cs"));

        var ticketChecked = handler.IndexOf("connection.InitializeCipher(packetLogin.Ticket)", StringComparison.Ordinal);
        var playerMade = handler.IndexOf("connection.CreatePlayerFromDatabase(character)", StringComparison.Ordinal);
        var raised = handler.IndexOf(Raise, StringComparison.Ordinal);
        var replied = handler.IndexOf("packetLoginReply.Success = true;", StringComparison.Ordinal);

        Assert.IsTrue(ticketChecked > 0, "the handler still keys the cipher with the checked ticket the way this test knows");
        Assert.IsTrue(playerMade > ticketChecked, "the handler still makes the player after that, the way this test knows");
        Assert.IsTrue(replied > 0, "the handler still marks success the way this test knows");
        Assert.IsTrue(raised > playerMade, "raised after the ticket has been checked and the player made, never before");
        Assert.IsTrue(raised < replied, "and before the client is told it is in, so its next packets find the full limits");
    }
}
