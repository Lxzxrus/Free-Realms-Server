using System;
using System.IO;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A connection has low limits, and a time by which it must log in, until <c>UdpConnection.RaiseStartingLimits</c> is
/// called. What keeps a stranger from filling a queue or holding a place is that only a successful login calls it. The
/// servers' handlers aren't unit-tested, so this reads their source: one call in the Gateway's login handler after the
/// player has been made, one in the Login server's after the account has signed in, and no other.
/// </summary>
[TestClass]
public class StartingLimitsRaiseTests
{
    private const string Raise = "RaiseStartingLimits(";

    private const string GatewayHandler = "Sanctuary.Gateway/Handlers/PacketLoginHandler.cs";
    private const string LoginHandler = "Sanctuary.Login/Handlers/LoginRequestHandler.cs";

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

    private static string Code(string source, string relative) =>
        Code(Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar)));

    [TestMethod]
    public void OnlyTheTwoLoginHandlersRaiseTheStartingLimits()
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
            .OrderBy(found => found.File, StringComparer.Ordinal)
            .ToList();

        Assert.AreEqual($"{GatewayHandler} x1, {LoginHandler} x1", string.Join(", ", callers.Select(found => $"{found.File} x{found.Calls}")));
    }

    [TestMethod]
    public void TheGatewayRaisesOnlyAfterThePlayerHasBeenMade()
    {
        var handler = Code(FindSource(), GatewayHandler);

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

    [TestMethod]
    public void TheLoginServerRaisesOnlyAfterTheAccountHasSignedIn()
    {
        var handler = Code(FindSource(), LoginHandler);

        var sessionLooked = handler.IndexOf("dbContext.Users.SingleOrDefault(x => x.Session == packet.Session)", StringComparison.Ordinal);
        var lockChecked = handler.IndexOf("_options.IsLocked", StringComparison.Ordinal);
        var signedIn = handler.IndexOf("connection.UserId = user.Id;", StringComparison.Ordinal);
        var raised = handler.IndexOf(Raise, StringComparison.Ordinal);
        var replied = handler.IndexOf("loginReply.LoggedIn = true;", StringComparison.Ordinal);

        Assert.IsTrue(sessionLooked > 0, "the handler still looks the session up the way this test knows");
        Assert.IsTrue(lockChecked > sessionLooked, "and still checks the server lock after that");
        Assert.IsTrue(signedIn > lockChecked, "and only then marks the connection as signed in");
        Assert.IsTrue(replied > 0, "the handler still marks success the way this test knows");
        Assert.IsTrue(raised > signedIn, "raised after the session and the lock have been checked, never before");
        Assert.IsTrue(raised < replied, "and before the client is told it is in");
    }
}
