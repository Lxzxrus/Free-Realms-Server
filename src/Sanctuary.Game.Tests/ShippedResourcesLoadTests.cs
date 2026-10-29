using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;

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
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Errors.Add(formatter(state, exception));
            else if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
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

        // The zone geometry is fetched at deploy time (tools/deploy/fetch-zone-geometry.sh) and isn't in the
        // repository; a developer may have fetched it, so the copy drops it to load what a fresh clone has.
        foreach (var geometry in new[] { "NavMeshes", "Terrain" })
        {
            var directory = Path.Combine(root, ResourceManager.BaseDirectory, geometry);

            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        var previous = Environment.CurrentDirectory;
        var logger = new ErrorLogger();
        ResourceManager? resourceManager = null;

        try
        {
            Environment.CurrentDirectory = root;
            resourceManager = new ResourceManager(logger);

            Assert.IsTrue(resourceManager.Load(), "The servers would refuse to start: " + string.Join(" | ", logger.Errors));
            Assert.AreEqual(20, resourceManager.RankLevels.MaxLevel, "Every job levels from 1 to 20.");

            // Pets bought for coins (Nate, 2026-10-29: the coin store keeps the pets upstream OSFR took out of it) are the
            // pets the Station Cash store hands out: each resolves to the model the pets system spawns.
            foreach (var restored in new[] { 2152, 2171, 2226 }) // Spaz, Dakota, Screech
                Assert.IsTrue(resourceManager.CoinStoreItems.ContainsKey(restored), $"Coin store pet {restored} is missing.");

            var coinStorePets = resourceManager.CoinStoreItems.Keys
                .Select(id => resourceManager.ClientItemDefinitions.TryGetValue(id, out var item) ? item : null)
                .OfType<Sanctuary.Packet.Common.ClientItemDefinition>()
                .Where(item => item.Type == ItemDefinitionHelper.PetItemType)
                .ToList();

            Assert.IsTrue(coinStorePets.Count >= 500, $"Only {coinStorePets.Count} pets in the coin store.");

            foreach (var pet in coinStorePets)
                Assert.IsTrue(PetHelper.TryGetModel(resourceManager, pet, out _), $"Coin store pet {pet.Id} has no pet model.");

            // The Gateway loads the zone geometry next. Without it the server still starts, with one warning.
            logger.Warnings.Clear();
            Assert.IsTrue(resourceManager.LoadZoneGeometry(), "The Gateway would refuse to start: " + string.Join(" | ", logger.Errors));
            Assert.AreEqual(0, resourceManager.NavMeshes.Count);
            Assert.AreEqual(0, resourceManager.Terrains.Count);
            Assert.AreEqual(1, logger.Warnings.Count, "Expected one warning: " + string.Join(" | ", logger.Warnings));
            StringAssert.Contains(logger.Warnings[0], "fetch-zone-geometry.sh");
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
