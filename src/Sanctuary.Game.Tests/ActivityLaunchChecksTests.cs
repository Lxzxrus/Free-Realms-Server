using System.Runtime.CompilerServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Whether a Flash minigame the player picked opens, from where they are and what they have open
/// (<see cref="ActivityLaunchChecks.FlashMinigame"/>, which FlashMinigameHandler.Open follows): one game at a time, a
/// start screen never started giving way, and none inside a house.
/// </summary>
[TestClass]
public sealed class ActivityLaunchChecksTests
{
    public enum Open { Nothing, StartScreen, StartedGame, Wheel, StartScreenAndWheel }

    private static Player PlayerIn<TZone>(Open open) where TZone : BaseZone
    {
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        player.Zone = (IZone)RuntimeHelpers.GetUninitializedObject(typeof(TZone));

        if (open is Open.StartScreen or Open.StartedGame or Open.StartScreenAndWheel)
        {
            player.FlashMinigame = new FlashMinigameSession(20_001, new FlashMinigameDefinition(414, 376, 8, "game_mailsorting.gfx"))
            {
                Started = open == Open.StartedGame
            };
        }

        if (open is Open.Wheel or Open.StartScreenAndWheel)
            player.WheelSession = new DailyWheelSession { StateId = 30_001 };

        return player;
    }

    [TestMethod]
    public void WithNothingOpen_TheGameOpens()
    {
        Assert.AreEqual(ActivityLaunchResult.Opened, ActivityLaunchChecks.FlashMinigame(PlayerIn<WorldZone>(Open.Nothing), out var givesWay));
        Assert.IsNull(givesWay);
    }

    /// <summary>A start screen the player never started (or closed without the server hearing) gives way to the new game.</summary>
    [TestMethod]
    public void AStartScreenNeverStarted_GivesWay_AndTheGameOpens()
    {
        var player = PlayerIn<WorldZone>(Open.StartScreen);

        Assert.AreEqual(ActivityLaunchResult.Opened, ActivityLaunchChecks.FlashMinigame(player, out var givesWay));
        Assert.AreSame(player.FlashMinigame, givesWay);
    }

    /// <summary>A game the player has started is never ended by another pick: a second click on a table mid-game opens nothing.</summary>
    [TestMethod]
    public void AGameThePlayerStarted_KeepsTheNewOneShut_AndStaysOpen()
    {
        Assert.AreEqual(ActivityLaunchResult.AnotherGameOpen, ActivityLaunchChecks.FlashMinigame(PlayerIn<WorldZone>(Open.StartedGame), out var givesWay));
        Assert.IsNull(givesWay);
    }

    [TestMethod]
    public void TheWheel_KeepsTheNewOneShut()
    {
        Assert.AreEqual(ActivityLaunchResult.AnotherGameOpen, ActivityLaunchChecks.FlashMinigame(PlayerIn<WorldZone>(Open.Wheel), out var givesWay));
        Assert.IsNull(givesWay);
    }

    /// <summary>With the wheel open too, a start screen never started still gives way, and the wheel keeps the new one shut.</summary>
    [TestMethod]
    public void AStartScreenNeverStarted_GivesWay_EvenWhenTheWheelKeepsTheNewOneShut()
    {
        var player = PlayerIn<WorldZone>(Open.StartScreenAndWheel);

        Assert.AreEqual(ActivityLaunchResult.AnotherGameOpen, ActivityLaunchChecks.FlashMinigame(player, out var givesWay));
        Assert.AreSame(player.FlashMinigame, givesWay);
    }

    /// <summary>Inside a house nothing opens, and nothing the player has open is touched.</summary>
    [TestMethod]
    [DataRow(Open.Nothing)]
    [DataRow(Open.StartScreen)]
    [DataRow(Open.StartedGame)]
    [DataRow(Open.Wheel)]
    [DataRow(Open.StartScreenAndWheel)]
    public void InsideAHouse_NoGameOpens(Open open)
    {
        Assert.AreEqual(ActivityLaunchResult.InsideAHouse, ActivityLaunchChecks.FlashMinigame(PlayerIn<HousingZone>(open), out var givesWay));
        Assert.IsNull(givesWay);
    }
}
