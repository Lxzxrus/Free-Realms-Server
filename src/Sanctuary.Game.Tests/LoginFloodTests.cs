using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Packet;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Nothing a client sends before it has signed in can grow the logs or reach the database unchecked: the session
/// Login accepts, the strings PacketLogin parses, what the packets print, and the log setup itself (NLog's rules and
/// file names, Docker's console log limits).
/// </summary>
[TestClass]
public sealed class LoginFloodTests
{
    // ---- the session Login accepts ----

    [TestMethod]
    public void TheWebApisSessionsAreWellFormed()
    {
        // AuthEndpoints makes every session this way
        for (var i = 0; i < 1000; i++)
            Assert.IsTrue(LoginSession.IsWellFormed(Guid.NewGuid().ToString("N")));
    }

    [TestMethod]
    [DataRow(null, DisplayName = "no session")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("0123456789abcdef0123456789abcde", DisplayName = "31 characters")]
    [DataRow("0123456789abcdef0123456789abcdef0", DisplayName = "33 characters")]
    [DataRow("0123456789ABCDEF0123456789ABCDEF", DisplayName = "upper case: the database would match it anyway")]
    [DataRow("0123456789abcdef0123456789abcde ", DisplayName = "a trailing space: so would this")]
    [DataRow("0123456789abcdef0123456789abcdeg", DisplayName = "not hex")]
    [DataRow("01234567-89ab-cdef-0123-456789abcd", DisplayName = "a Guid with dashes")]
    public void AnythingElseIsRefusedBeforeTheDatabase(string? session) =>
        Assert.IsFalse(LoginSession.IsWellFormed(session), session);

    // ---- LoginRequest ----

    [TestMethod]
    public void ARealLoginRequestParses()
    {
        var session = Guid.NewGuid().ToString("N");

        Assert.IsTrue(LoginRequest.TryDeserialize(LoginRequestBytes(session, "fingerprint"), out var packet));
        Assert.AreEqual(session, packet.Session);
    }

    [TestMethod]
    public void ALongSessionDoesNotParse()
    {
        Assert.IsTrue(LoginRequest.TryDeserialize(LoginRequestBytes(new string('a', LoginRequest.MaxSessionLength), ""), out _));
        Assert.IsFalse(LoginRequest.TryDeserialize(LoginRequestBytes(new string('a', LoginRequest.MaxSessionLength + 1), ""), out _));
        Assert.IsFalse(LoginRequest.TryDeserialize(LoginRequestBytes(new string('a', 60_000), ""), out _));
    }

    [TestMethod]
    public void ALoginRequestPrintsNoSession()
    {
        var session = Guid.NewGuid().ToString("N");

        Assert.IsTrue(LoginRequest.TryDeserialize(LoginRequestBytes(session, "fingerprint"), out var packet));

        var text = packet.ToString();

        Assert.IsFalse(text.Contains(session, StringComparison.Ordinal), text);
        Assert.IsFalse(text.Contains("fingerprint", StringComparison.Ordinal), text);
    }

    // ---- PacketLogin (the Gateway) ----

    private const string RealVersion = "1.910.1.530630";

    [TestMethod]
    public void ARealPacketLoginParses()
    {
        var ticket = Guid.NewGuid().ToString("N");

        Assert.IsTrue(PacketLogin.TryDeserialize(PacketLoginBytes(ticket, RealVersion, ""), out var packet));
        Assert.AreEqual(ticket, packet.Ticket);
        Assert.AreEqual(RealVersion, packet.Version);
    }

    [TestMethod]
    public void ALongTicketOrVersionDoesNotParse()
    {
        var ticket = Guid.NewGuid().ToString("N");

        Assert.IsTrue(PacketLogin.TryDeserialize(PacketLoginBytes(new string('a', PacketLogin.MaxTicketLength), RealVersion, ""), out _));
        Assert.IsFalse(PacketLogin.TryDeserialize(PacketLoginBytes(new string('a', PacketLogin.MaxTicketLength + 1), RealVersion, ""), out _));
        Assert.IsFalse(PacketLogin.TryDeserialize(PacketLoginBytes(ticket.PadRight(60_000), RealVersion, ""), out _), "a real ticket padded with spaces (Guid.TryParse trims them)");

        Assert.IsTrue(PacketLogin.TryDeserialize(PacketLoginBytes(ticket, new string('1', PacketLogin.MaxVersionLength), ""), out _));
        Assert.IsFalse(PacketLogin.TryDeserialize(PacketLoginBytes(ticket, new string('1', PacketLogin.MaxVersionLength + 1), ""), out _));
    }

    [TestMethod]
    public void APacketLoginPrintsNoTicket()
    {
        var ticket = Guid.NewGuid().ToString("N");

        Assert.IsTrue(PacketLogin.TryDeserialize(PacketLoginBytes(ticket, RealVersion, ""), out var packet));

        Assert.IsFalse(packet.ToString().Contains(ticket, StringComparison.Ordinal), packet.ToString());
    }

    // ---- the log setup ----

    [TestMethod]
    public void TheWebApiDropsAspNetCoresInformationLines()
    {
        var rules = Rules("Sanctuary.WebAPI");

        var aspNet = rules.FindIndex(rule =>
            Attribute(rule, "name") == "Microsoft.AspNetCore.*"
            && string.Equals(Attribute(rule, "maxlevel"), "Info", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Attribute(rule, "final"), "true", StringComparison.OrdinalIgnoreCase)
            && Attribute(rule, "writeTo") is null);

        var firstCatchAll = rules.FindIndex(rule => Attribute(rule, "name") == "*");

        Assert.IsTrue(aspNet >= 0, "no rule that drops Microsoft.AspNetCore.* at Info and below");
        Assert.IsTrue(aspNet < firstCatchAll, "the rule has to come before the catch-all rules to stop anything");
    }

    [TestMethod]
    public void EachServerWritesFilesOfItsOwn()
    {
        string[] servers = ["Sanctuary.Login", "Sanctuary.Gateway", "Sanctuary.WebAPI"];

        var fileNames = servers
            .SelectMany(server => Config(server).Descendants().Where(element => element.Name.LocalName == "target"))
            .Select(target => Attribute(target, "fileName"))
            .OfType<string>()
            .ToList();

        Assert.IsTrue(fileNames.Count >= 6, string.Join(", ", fileNames));

        // in Docker every server's process is "dotnet", so ${processname} gives all three the same files
        Assert.IsFalse(fileNames.Any(name => name.Contains("${processname}", StringComparison.OrdinalIgnoreCase)), string.Join(", ", fileNames));
        Assert.AreEqual(fileNames.Count, fileNames.Distinct(StringComparer.OrdinalIgnoreCase).Count(), string.Join(", ", fileNames));
    }

    [TestMethod]
    public void DockerKeepsLimitedConsoleLogsForEachServer()
    {
        var lines = File.ReadAllLines(FindSourceFile("Docker", "docker-compose.yml"));

        var anchor = Array.FindIndex(lines, line => line.StartsWith("x-logging: &logging", StringComparison.Ordinal));
        Assert.IsTrue(anchor >= 0, "no x-logging block");

        var block = lines.Skip(anchor + 1).TakeWhile(line => line.StartsWith(' ')).ToList();
        Assert.IsTrue(block.Any(line => line.Trim() == "driver: json-file"), string.Join("\n", block));
        Assert.IsTrue(block.Any(line => line.Trim().StartsWith("max-size:", StringComparison.Ordinal)), string.Join("\n", block));
        Assert.IsTrue(block.Any(line => line.Trim().StartsWith("max-file:", StringComparison.Ordinal)), string.Join("\n", block));

        foreach (var service in new[] { "sanctuary.webapi", "sanctuary.gateway", "sanctuary.login" })
        {
            var start = Array.FindIndex(lines, line => line == $"  {service}:");
            Assert.IsTrue(start >= 0, service);

            var serviceLines = lines.Skip(start + 1).TakeWhile(line => line.Length == 0 || line.StartsWith("    ", StringComparison.Ordinal) || line.TrimStart().StartsWith('#'));

            Assert.IsTrue(serviceLines.Any(line => line == "    logging: *logging"), $"{service} has no logging: *logging");
        }
    }

    // ---- helpers ----

    private static byte[] LoginRequestBytes(string session, string fingerprint)
    {
        using var writer = new PacketWriter();

        writer.Write(LoginRequest.OpCode);
        writer.Write(session);
        writer.Write(fingerprint);

        return writer.Buffer;
    }

    private static byte[] PacketLoginBytes(string ticket, string version, string unknown)
    {
        using var writer = new PacketWriter();

        writer.Write(PacketLogin.OpCode);
        writer.Write(ticket);
        writer.Write(0x0100_0000_0000_0001UL);
        writer.Write(version);
        writer.Write(unknown);

        return writer.Buffer;
    }

    private static XDocument Config(string server) => XDocument.Load(FindSourceFile(server, "NLog.config"));

    private static System.Collections.Generic.List<XElement> Rules(string server) =>
        Config(server).Descendants().Where(element => element.Name.LocalName == "logger").ToList();

    // NLog's attribute names don't care about case (maxLevel, maxlevel)
    private static string? Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static string FindSourceFile(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);

            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"src/{string.Join('/', relativePath)} not found above the test output.");
    }
}
