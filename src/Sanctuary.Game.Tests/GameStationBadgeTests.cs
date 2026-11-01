using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Game.Resources;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A world object that opens a game wears its kind's badge and hover cursor, the ones the retail capture shows on its own
/// objects of that kind; one with nothing to open wears no minigame badge or cursor until its game is wired
/// (<see cref="GameStations.ShowWhatItOpens"/>). Against the shipped Npcs.json, Games menu entries and served games.
/// Whether the client draws a badge over an object and on the minimap is checked in game.
/// </summary>
[TestClass]
public sealed class GameStationBadgeTests
{
    private const int Local = PointOfInterestNotificationType.Local;

    private const int CookingIcon = 7546;
    private const int HarvestingIcon = 7555;
    private const int ForgingIcon = 7552;
    private const int MailIcon = 7558;
    private const int FishingIcon = 21061;

    private static ClientActivityDefinitionCollection _activities = null!;
    private static MimicDefinitions _mimic = null!;
    private static ZoneDefinitionCollection _zones = null!;
    private static DailyWheel _dailyWheel = null!;
    private static NpcDefinitionCollection _npcs = null!;

    [ClassInitialize]
    public static void LoadResources(TestContext _)
    {
        var resources = Path.GetDirectoryName(FindSourceFile("Resources", "ClientActivityDefinitions.json"))!;

        _activities = new ClientActivityDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_activities.Load(Path.Combine(resources, "ClientActivityDefinitions.json")));

        Assert.IsTrue(MimicDefinitions.TryLoad(Path.Combine(resources, "Mimic.json"), NullLogger.Instance, out _mimic));

        _zones = new ZoneDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_zones.Load(Path.Combine(resources, "Zones")));

        Assert.IsTrue(DailyWheel.TryLoad(Path.Combine(resources, "DailyWheel.json"), NullLogger.Instance, out _dailyWheel));

        _npcs = new NpcDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_npcs.Load(Path.Combine(resources, "Npcs.json")));
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

    private static bool IsPlayable(int activityId) => PlayableActivities.IsPlayable(activityId, _mimic, _zones, _dailyWheel);

    /// <summary>
    /// The NPC as BaseZone.TryCreateNpc spawns it from its Npcs.json row (its captured badges and cursor), then shown as
    /// a station with the games the server serves (or <paramref name="isPlayable"/>).
    /// </summary>
    private static Npc Spawn(int npcId, Func<int, bool>? isPlayable = null)
    {
        var definition = _npcs[npcId];

        var npc = new Npc(null!)
        {
            Guid = GuidHelper.GetNpcGuid((ulong)npcId, GuidHelper.OpenWorldInstanceId),
            DefinitionId = npcId,
            NameId = definition.NameId,
            CursorId = definition.CursorId,
            HasCursor = definition.HasCursor
        };

        foreach (var notification in definition.Notifications)
            npc.Notifications.Add(InteractionMenuHelper.GetNotification(npc.Guid, notification));

        GameStations.ShowWhatItOpens(npc, _activities, isPlayable ?? IsPlayable);

        return npc;
    }

    private static IEnumerable<GameStation> StationsThatOpen(bool aGame) =>
        GameStations.All.Where(station => station.ActivityIds.Any(IsPlayable) == aGame);

    private static int IconOf(GameStation station) => _activities[station.ActivityIds[0]].ImageSetId;

    /// <summary>Each kind's badge and cursor are the capture's own, read from the shipped Npcs.json rows it recorded.</summary>
    [TestMethod]
    [DataRow(CookingIcon, 33482)]
    [DataRow(CookingIcon, 33636)]
    [DataRow(HarvestingIcon, 33213)]
    [DataRow(ForgingIcon, 33481)]
    [DataRow(MailIcon, 33389)]
    [DataRow(FishingIcon, 33620)]
    public void EachKind_IsTheCapturesOwn(int icon, int capturedNpcId)
    {
        var captured = _npcs[capturedNpcId];
        var badge = GameStations.Badges[icon];
        var notification = captured.Notifications.Single();

        Assert.AreEqual((Local, badge.IconId, badge.IconState, badge.ReferenceId),
            (notification.NotificationType, notification.IconId, notification.IconState, notification.ReferenceId));
        Assert.AreEqual((true, badge.CursorId), (captured.HasCursor, captured.CursorId));
        Assert.AreEqual(icon, IconOf(GameStations.All.Single(station => station.NpcIds.Contains(capturedNpcId))));
    }

    [TestMethod]
    public void EveryStationThatOpensAGame_HasAKind()
    {
        Assert.AreEqual(5, GameStations.Badges.Count);

        foreach (var station in StationsThatOpen(aGame: true))
            Assert.IsTrue(GameStations.Badges.ContainsKey(IconOf(station)), $"{station.Name} has no badge.");
    }

    /// <summary>
    /// Each object with a game to open wears exactly one minigame badge, its kind's, named for the object, and its kind's
    /// cursor: those the capture has keep theirs, the others (Sanctuary's table, the mailbox beside Flitter, Smitty's and
    /// the Spectrum anvils...) get them.
    /// </summary>
    [TestMethod]
    public void AnObjectWithAGame_WearsOneBadgeOfItsKind_AndItsCursor()
    {
        var stations = StationsThatOpen(aGame: true).ToList();

        Assert.AreEqual(7, stations.Count);

        foreach (var station in stations)
        {
            var badge = GameStations.Badges[IconOf(station)];

            foreach (var npcId in station.NpcIds)
            {
                var npc = Spawn(npcId);
                var worn = npc.Notifications.Where(notification => notification.NotificationType == Local).ToList();

                Assert.AreEqual(1, worn.Count, $"{station.Name} {npcId} wears {worn.Count} minigame badges.");
                Assert.AreEqual((badge.IconId, badge.IconState, badge.ReferenceId), (worn[0].IconId, worn[0].IconState, worn[0].ReferenceId),
                    $"{station.Name} {npcId}'s badge isn't its kind's.");
                Assert.AreEqual((npc.Guid, _npcs[npcId].NameId, true), (worn[0].Guid, worn[0].NameId, worn[0].Enabled));
                Assert.AreEqual((true, badge.CursorId), (npc.HasCursor, npc.CursorId), $"{station.Name} {npcId}'s cursor isn't its kind's.");
            }
        }

        // Three that had none: the cooking bubble, the envelope, the forging bubble.
        Assert.AreEqual(58, Spawn(2417).Notifications.Single().IconState);
        Assert.AreEqual(11, Spawn(2375).Notifications.Single().IconState);
        Assert.AreEqual(60, Spawn(3853).Notifications.Single().IconState);
    }

    /// <summary>
    /// An object with nothing to open wears no minigame badge and no cursor: the capture's on the Lakesfield and Perry's
    /// tools (the harvesting bubble) and on the copper veins come off, until their games are wired.
    /// </summary>
    [TestMethod]
    public void AnObjectWithNothingToOpen_WearsNoMinigameBadgeOrCursor()
    {
        var stations = StationsThatOpen(aGame: false).ToList();

        Assert.AreEqual(7, stations.Count);
        Assert.IsTrue(_npcs[33484].Notifications.Any(), "The capture badges the Lakesfield tools.");
        Assert.IsTrue(_npcs[33585].Notifications.Any(), "The capture badges the copper veins.");

        foreach (var npcId in stations.SelectMany(station => station.NpcIds))
        {
            var npc = Spawn(npcId);

            Assert.IsFalse(npc.Notifications.Any(notification => notification.NotificationType == Local), $"{npcId} wears a minigame badge.");
            Assert.IsFalse(InteractionMenuHelper.HasOwnCursor(npc), $"{npcId} wears a cursor.");
        }
    }

    /// <summary>
    /// The kind's badge is added unless one with its image is there already, and its cursor only where there is none: an
    /// object's own cursor and another image's badge stay.
    /// </summary>
    [TestMethod]
    public void AnObjectsOwnCursor_AndAnotherImagesBadge_Stay()
    {
        var table = new Npc(null!)
        {
            Guid = GuidHelper.GetNpcGuid(2417, GuidHelper.OpenWorldInstanceId),
            DefinitionId = 2417,
            NameId = 2061,
            HasCursor = true,
            CursorId = 5
        };

        table.Notifications.Add(new NotificationInfo { Guid = table.Guid, NotificationType = Local, IconId = 334, IconState = 20 });

        GameStations.ShowWhatItOpens(table, _activities, IsPlayable);

        CollectionAssert.AreEqual(new[] { 20, 58 }, table.Notifications.Select(notification => notification.IconState).ToArray());
        Assert.AreEqual((true, (byte)5), (table.HasCursor, table.CursorId));
    }

    [TestMethod]
    public void OnceItsGameIsWired_AnObjectWearsItsKind()
    {
        const int lakesfieldHarvesting = 570;

        var tools = Spawn(33484, activityId => activityId == lakesfieldHarvesting || IsPlayable(activityId));
        var badge = GameStations.Badges[HarvestingIcon];

        Assert.AreEqual(badge.IconState, tools.Notifications.Single().IconState);
        Assert.AreEqual((true, badge.CursorId), (tools.HasCursor, tools.CursorId));
    }

    /// <summary>
    /// Anyone else is left as they are, the capture's minigame badges on objects with no game among them (Honey's keg,
    /// a chess table, the practice dummy): those are another piece's.
    /// </summary>
    [TestMethod]
    [DataRow(33132)] // Honey's Honey Keg
    [DataRow(33351)] // Checkers & Chess
    [DataRow(33779)] // Practice Dummy
    [DataRow(2069)]  // Simone
    public void AnyoneElse_IsLeftAsTheyAre(int npcId)
    {
        var definition = _npcs[npcId];
        var npc = Spawn(npcId);

        Assert.AreEqual(definition.Notifications.Count, npc.Notifications.Count);

        for (var i = 0; i < npc.Notifications.Count; i++)
            Assert.AreEqual(definition.Notifications[i].IconState, npc.Notifications[i].IconState);

        Assert.AreEqual((definition.HasCursor, definition.CursorId), (npc.HasCursor, npc.CursorId));
    }
}
