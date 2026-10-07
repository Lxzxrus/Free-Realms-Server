using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Configuration;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class StartupChecksTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] values)
    {
        var data = new Dictionary<string, string?>();

        foreach (var (key, value) in values)
            data[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    [TestMethod]
    public void ReleaseBuildStartsWithoutTheSetting()
    {
        Assert.IsNull(StartupChecks.CheckBuild(isDebugBuild: false, Configuration()));
    }

    [TestMethod]
    public void DebugBuildRefusesWithoutTheSetting()
    {
        Assert.IsNotNull(StartupChecks.CheckBuild(isDebugBuild: true, Configuration()));
    }

    [TestMethod]
    [DataRow("false")]
    [DataRow("")]
    [DataRow("yes")]
    [DataRow("1")]
    public void DebugBuildRefusesUnlessTheSettingIsTrue(string value)
    {
        var configuration = Configuration((StartupChecks.AllowDebugBuildKey, value));

        Assert.IsNotNull(StartupChecks.CheckBuild(isDebugBuild: true, configuration));
    }

    [TestMethod]
    [DataRow("true")]
    [DataRow("True")]
    public void DebugBuildStartsWhenAllowed(string value)
    {
        var configuration = Configuration((StartupChecks.AllowDebugBuildKey, value));

        Assert.IsNull(StartupChecks.CheckBuild(isDebugBuild: true, configuration));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void ChallengeRefusesWhenMissing(string? challenge)
    {
        Assert.IsNotNull(StartupChecks.CheckLoginGatewayChallenge(challenge));
    }

    [TestMethod]
    [DataRow("OSFR-EDITz@2024")]
    [DataRow(" OSFR-EDITz@2024 ")]
    [DataRow("osfr-editz@2024")]
    public void ChallengeRefusesOsfrsPublicDefault(string challenge)
    {
        Assert.IsNotNull(StartupChecks.CheckLoginGatewayChallenge(challenge));
    }

    [TestMethod]
    public void ChallengeAcceptsAnyOtherValue()
    {
        Assert.IsNull(StartupChecks.CheckLoginGatewayChallenge("hHq0lO4M5d3m4dn1g3Ck6PZt1D3Gkq2hJxRkQe0h3cE="));
    }

    [TestMethod]
    [DataRow("127.0.0.1")]
    [DataRow("0.0.0.0")]
    [DataRow("10.0.0.5")]
    public void BindAddressAcceptsIPv4(string address)
    {
        Assert.IsNull(StartupChecks.CheckLoginGatewayBindAddress(address));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("localhost")]
    [DataRow("::1")]
    [DataRow("127.0.0.1:20041")]
    public void BindAddressRefusesAnythingElse(string? address)
    {
        Assert.IsNotNull(StartupChecks.CheckLoginGatewayBindAddress(address));
    }

    [TestMethod]
    public void BindAddressDefaultsToThisMachineOnly()
    {
        var options = new LoginServerOptions
        {
            Port = 20042,
            UseCompression = false,
            LoginGatewayPort = 20041,
            LoginGatewayChallenge = "x",
            DefaultProfileId = 1,
        };

        Assert.AreEqual("127.0.0.1", options.LoginGatewayBindAddress);
    }

    /// <summary>
    /// The example files must load as they are and with every commented-out setting switched on, using the
    /// same JSON options as the configuration reader (comments and trailing commas allowed).
    /// </summary>
    [TestMethod]
    [DataRow("Sanctuary.Login/login.local.example.json")]
    [DataRow("Sanctuary.Gateway/gateway.local.example.json")]
    [DataRow("Sanctuary.WebAPI/appsettings.local.example.json")]
    public void ExampleFileParsesAsIsAndWithEverySettingUncommented(string path)
    {
        var text = ReadSource(path);
        var uncommented = Regex.Replace(text, @"^(\s*)// (""\w+"":)", "$1$2", RegexOptions.Multiline);

        Assert.AreNotEqual(text, uncommented);

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        using (JsonDocument.Parse(text, options))
        using (var document = JsonDocument.Parse(uncommented, options))
        {
            // The configuration reader refuses duplicate keys, so no setting may be listed twice.
            AssertNoDuplicateKeys(document.RootElement);
        }
    }

    private static void AssertNoDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            Assert.IsTrue(names.Add(property.Name), $"\"{property.Name}\" is listed twice.");

            AssertNoDuplicateKeys(property.Value);
        }
    }

    [TestMethod]
    public void TrackedLoginConfigHasLaunchEconomy()
    {
        using var document = JsonDocument.Parse(ReadSource("Sanctuary.Login/login.json"));
        var server = document.RootElement.GetProperty("Server");

        Assert.AreEqual(1000, server.GetProperty("StartingCoins").GetInt32());
        Assert.AreEqual(0, server.GetProperty("StartingStationCash").GetInt32());
        Assert.IsFalse(server.GetProperty("UnlockAllTitles").GetBoolean());
        Assert.IsFalse(server.GetProperty("UnlockAllProfiles").GetBoolean());
    }

    [TestMethod]
    [DataRow("Sanctuary.WebAPI/appsettings.json")]
    [DataRow("Sanctuary.WebAPI/appsettings.Development.json")]
    public void TrackedWebApiConfigDoesNotMakeEveryoneAMember(string path)
    {
        using var document = JsonDocument.Parse(ReadSource(path));

        if (document.RootElement.GetProperty("WebAPI").TryGetProperty("MemberByDefault", out var memberByDefault))
            Assert.IsFalse(memberByDefault.GetBoolean());
    }

    [TestMethod]
    [DataRow("Sanctuary.Login/login.json")]
    [DataRow("Sanctuary.Gateway/gateway.json")]
    public void TrackedConfigHasNoChallenge(string path)
    {
        var text = ReadSource(path);

        Assert.DoesNotContain("LoginGatewayChallenge", text);
    }

    /// <summary>
    /// Reads a file under <c>src/</c>, found by walking up from the test output.
    /// </summary>
    private static string ReadSource(string path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, path);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException($"src/{path} not found above the test output.");
    }

    [TestMethod]
    public void PassesLogsEachFailureAndRefuses()
    {
        var logger = new ListLogger();

        Assert.IsFalse(StartupChecks.Passes(logger, isDebugBuild: false, "first", null, "second"));

        CollectionAssert.AreEqual(new[] { LogLevel.Critical, LogLevel.Critical }, logger.Levels);
    }

    [TestMethod]
    public void PassesWarnsWhenAnAllowedDebugBuildStarts()
    {
        var logger = new ListLogger();

        Assert.IsTrue(StartupChecks.Passes(logger, isDebugBuild: true, (string?)null));

        CollectionAssert.AreEqual(new[] { LogLevel.Warning }, logger.Levels);
    }

    [TestMethod]
    public void PassesIsQuietForAReleaseBuild()
    {
        var logger = new ListLogger();

        Assert.IsTrue(StartupChecks.Passes(logger, isDebugBuild: false, (string?)null));

        Assert.IsEmpty(logger.Levels);
    }

    private sealed class ListLogger : ILogger
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
}
