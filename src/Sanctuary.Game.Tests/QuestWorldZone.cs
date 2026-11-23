using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Game.Trading;
using Sanctuary.Game.Zones;
using Sanctuary.Scripting;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Fabled Realms as the Gateway builds it (<see cref="WorldZone"/>), around a <see cref="QuestWorld"/>: its quest manager
/// and its recording launcher, with the shipped NPCs, zones and served games added to its resources. <see cref="Spawn"/>
/// makes an NPC as FabledRealms.lua's <c>spawnFixedNpc</c> does, through <see cref="BaseZone.TrySpawnNpc"/>, so what the
/// zone does to an NPC it spawns, and to a click on it, is the server's own. Nothing else of the server is there: no
/// scripts, other zones or trading.
/// </summary>
public sealed class QuestWorldZone : IDisposable
{
    public QuestWorldZone(QuestWorld world)
    {
        var resources = world.Resources;

        Assert.IsTrue(resources.Npcs.Load(FindSourceFile("Resources", "Npcs.json")));
        Assert.IsTrue(resources.Zones.Load(Path.GetDirectoryName(FindSourceFile("Resources", "Zones", "FabledRealms.json"))!));

        // ResourceManager.Load reads these with everything else the servers read; a spawn and a click ask only whether a
        // game is served (PlayableActivities).
        Assert.IsTrue(MimicDefinitions.TryLoad(FindSourceFile("Resources", "Mimic.json"), NullLogger.Instance, out var mimic));
        Assert.IsTrue(DailyWheel.TryLoad(FindSourceFile("Resources", "DailyWheel.json"), NullLogger.Instance, out var dailyWheel));

        typeof(ResourceManager).GetProperty(nameof(ResourceManager.Mimic))!.SetValue(resources, mimic);
        typeof(ResourceManager).GetProperty(nameof(ResourceManager.DailyWheel))!.SetValue(resources, dailyWheel);

        var services = new Services
        {
            [typeof(IResourceManager)] = resources,
            [typeof(IOptions<HousingOptions>)] = Options.Create(new HousingOptions()),
            [typeof(ILoggerFactory)] = NullLoggerFactory.Instance,
            [typeof(IScriptManager)] = DispatchProxy.Create<IScriptManager, NoScripts>(),
            [typeof(IZoneManager)] = DispatchProxy.Create<IZoneManager, NotHere>(),
            [typeof(ITradeManager)] = DispatchProxy.Create<ITradeManager, NotHere>(),
            [typeof(IQuestManager)] = world.Quests,
            [typeof(IActivityLauncher)] = world.Launcher
        };

        Zone = new WorldZone(resources.Zones.Values.OfType<WorldZoneDefinition>().Single(), services) { Id = 1 };
    }

    public WorldZone Zone { get; }

    /// <summary>The NPC as <c>zone:spawnFixedNpc(npcId, 0, 0, 0, 0)</c> spawns it, under its fixed GUID.</summary>
    public Npc Spawn(int npcId)
    {
        Assert.IsTrue(Zone.TrySpawnNpc(npcId, GuidHelper.GetNpcGuid((ulong)npcId, GuidHelper.OpenWorldInstanceId),
            0f, 0f, 0f, 0f, 0f, 0f, out var npc), $"NPC {npcId} wasn't spawned.");

        return (Npc)npc;
    }

    public void Dispose() => Zone.Dispose();

    private sealed class Services : Dictionary<Type, object>, IServiceProvider
    {
        public object? GetService(Type serviceType) => TryGetValue(serviceType, out var service) ? service : null;
    }

    /// <summary>No NPC spawned here has a script, so there is no script context to delete.</summary>
    public class NoScripts : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IScriptManager.DeleteContext) ? false : throw new NotSupportedException(targetMethod?.Name);
    }

    /// <summary>A service the zone is built with but nothing here uses.</summary>
    public class NotHere : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }

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
