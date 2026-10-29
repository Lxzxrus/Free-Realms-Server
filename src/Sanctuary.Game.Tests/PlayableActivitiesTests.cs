using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Minigames;
using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The Games menu lists only what the server can open, against the shipped resources: before this, it listed all 417
/// of the client's activities and about 15 did anything.
/// </summary>
[TestClass]
public sealed class PlayableActivitiesTests
{
    private static readonly int[] MatchThree = [1113, 410, 414, 420];
    private static readonly int[] Cooking = [450, 460, 451, 456, 471];
    private static readonly int[] FishingHoles = [560, 561, 562, 563, 564, 565];

    private static ClientActivityDefinitionCollection _activities = null!;
    private static MimicDefinitions _mimic = null!;
    private static ZoneDefinitionCollection _zones = null!;
    private static DailyWheel _dailyWheel = null!;

    [ClassInitialize]
    public static void LoadResources(TestContext _)
    {
        var resources = FindResourcesDirectory();

        _activities = new ClientActivityDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_activities.Load(Path.Combine(resources, "ClientActivityDefinitions.json")));

        Assert.IsTrue(MimicDefinitions.TryLoad(Path.Combine(resources, "Mimic.json"), NullLogger.Instance, out _mimic));

        _zones = new ZoneDefinitionCollection(NullLogger.Instance);
        Assert.IsTrue(_zones.Load(Path.Combine(resources, "Zones")));

        Assert.IsTrue(DailyWheel.TryLoad(Path.Combine(resources, "DailyWheel.json"), NullLogger.Instance, out _dailyWheel));
    }

    private static string FindResourcesDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources");

            if (File.Exists(Path.Combine(candidate, "ClientActivityDefinitions.json")) && Directory.Exists(Path.Combine(candidate, "Zones")))
                return candidate;
        }

        throw new DirectoryNotFoundException("src/Resources not found above the test output.");
    }

    private static bool IsPlayable(int activityId, DailyWheel? dailyWheel = null) =>
        PlayableActivities.IsPlayable(activityId, _mimic, _zones, dailyWheel ?? _dailyWheel);

    [TestMethod]
    public void TheMenu_ListsEveryGameThatOpens_AndNothingElse()
    {
        var listed = _activities.Values.Where(activity => IsPlayable(activity.Id)).Select(activity => activity.Id).ToArray();

        var expected = MatchThree.Concat(Cooking).Concat(FishingHoles).ToArray();
        if (_dailyWheel.Enabled)
            expected = [.. expected, DailyWheel.ActivityId];

        CollectionAssert.AreEquivalent(expected, listed);
    }

    [TestMethod]
    public void EveryGameThatOpens_IsAnActivityTheClientKnows()
    {
        foreach (var activityId in MatchThree.Concat(Cooking).Concat(FishingHoles))
            Assert.IsTrue(_activities.ContainsKey(activityId), $"Activity {activityId} isn't in ClientActivityDefinitions.json.");
    }

    [TestMethod]
    public void TheWheel_IsListedOnlyWhileItIsOn()
    {
        Assert.IsFalse(IsPlayable(DailyWheel.ActivityId, new DailyWheel { Enabled = false }));
        Assert.IsTrue(IsPlayable(DailyWheel.ActivityId, new DailyWheel { Enabled = true }));
    }

    [TestMethod]
    public void UnbuiltGames_AreNotListed()
    {
        Assert.IsFalse(IsPlayable(7), "The Trading Card Game lobby is a placeholder.");
        Assert.IsFalse(IsPlayable(514), "Checkers isn't built.");
        Assert.IsFalse(IsPlayable(515), "Chess isn't built.");
    }
}
