using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Loads the shipped <c>src/Resources</c> exactly as Login and Gateway do at startup. PR #70 passed every test yet
/// stopped both servers ("Collection 265 references an unknown item definition"), because no test ran this load.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ShippedResourcesLoadTests
{
    private sealed class ErrorLogger : ILogger<ResourceManager>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }

    private static string FindShippedResources()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources");
            if (File.Exists(Path.Combine(candidate, "Collections.json")) && File.Exists(Path.Combine(candidate, "Quests.json")))
                return candidate;
        }

        throw new DirectoryNotFoundException("src/Resources not found above the test output.");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));

        foreach (var directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    [TestMethod]
    public void ShippedResources_LoadLikeTheServersDo()
    {
        // ResourceManager reads "Resources" relative to the working directory. A copy keeps the load from touching
        // the source tree, and keeps this test away from the other tests' files in the test output.
        var root = Path.Combine(Path.GetTempPath(), "evergrove-resources-" + Guid.NewGuid().ToString("N"));
        CopyDirectory(FindShippedResources(), Path.Combine(root, ResourceManager.BaseDirectory));

        var previous = Environment.CurrentDirectory;
        var logger = new ErrorLogger();
        ResourceManager? resourceManager = null;

        try
        {
            Environment.CurrentDirectory = root;
            resourceManager = new ResourceManager(logger);

            Assert.IsTrue(resourceManager.Load(), "The servers would refuse to start: " + string.Join(" | ", logger.Errors));
            Assert.AreEqual(20, resourceManager.RankLevels.MaxLevel, "Every job levels from 1 to 20.");
        }
        finally
        {
            Environment.CurrentDirectory = previous;

            if (resourceManager is not null)
            {
                var watcher = (FileSystemWatcher?)typeof(ResourceManager)
                    .GetField("_fileSystemWatcher", BindingFlags.Instance | BindingFlags.NonPublic)?
                    .GetValue(resourceManager);
                watcher?.Dispose();
            }

            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
                // A zone file can still be open for a moment; the temp folder is cleaned up eventually anyway.
            }
        }
    }
}
