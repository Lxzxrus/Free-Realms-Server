using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
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
        var text = File.ReadAllText(Path.Combine(SourceDirectory(), path));
        var uncommented = Regex.Replace(text, @"^(\s*)// (""\w+"":)", "$1$2", RegexOptions.Multiline);

        Assert.AreNotEqual(text, uncommented);

        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        using (JsonDocument.Parse(text, options))
        using (JsonDocument.Parse(uncommented, options))
        {
        }
    }

    [TestMethod]
    [DataRow("Sanctuary.Login/login.json")]
    [DataRow("Sanctuary.Gateway/gateway.json")]
    public void TrackedConfigHasNoChallenge(string path)
    {
        var text = File.ReadAllText(Path.Combine(SourceDirectory(), path));

        Assert.DoesNotContain("LoginGatewayChallenge", text);
    }

    private static string SourceDirectory([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, ".."));

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
