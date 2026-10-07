using System;
using System.Collections.Generic;

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
