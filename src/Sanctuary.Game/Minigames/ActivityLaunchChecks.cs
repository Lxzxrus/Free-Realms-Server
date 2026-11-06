using Sanctuary.Game.Entities;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Minigames;

/// <summary>
/// Evergrove: whether a game the player picked can open, from where they are and what they have open, decided here so it
/// is tested; the Gateway's openers (FlashMinigameHandler.Open and the Games menu's for a fishing hole, behind the Games
/// menu, a quest's NPC and the world objects) send what it decides. One game at a time, and none inside a house.
/// </summary>
public static class ActivityLaunchChecks
{
    /// <summary>
    /// Whether a Flash minigame (match three, Cooking) opens for <paramref name="player"/>:
    /// <see cref="ActivityLaunchResult.InsideAHouse"/> inside a house, whatever they have open: the client shows no
    /// minigame start screen there, so nothing would show and the player wouldn't know why.
    /// <see cref="ActivityLaunchResult.AnotherGameOpen"/> while they have a Flash minigame they started, or the wheel,
    /// open. Otherwise <see cref="ActivityLaunchResult.Opened"/>: it opens.
    /// </summary>
    /// <param name="givesWay">
    /// The player's Flash minigame that was never started, if they have one outside a house: the opener closes it, whether
    /// the new game opens or the wheel refuses it. A start screen closed before Go may never tell the server, so it gives
    /// way to the next game the player opens. It has paid nothing and can't: once it is gone, its Go is refused
    /// (FlashMinigameHandler.TryStart).
    /// </param>
    public static ActivityLaunchResult FlashMinigame(Player player, out FlashMinigameSession? givesWay)
    {
        givesWay = null;

        if (player.Zone is HousingZone)
            return ActivityLaunchResult.InsideAHouse;

        if (player.FlashMinigame is { Started: false } unstarted)
            givesWay = unstarted;

        return player.FlashMinigame is { Started: true } || player.WheelSession is not null
            ? ActivityLaunchResult.AnotherGameOpen
            : ActivityLaunchResult.Opened;
    }

    /// <summary>
    /// Whether a fishing hole's start screen opens for <paramref name="player"/>: <see cref="ActivityLaunchResult.InsideAHouse"/>
    /// inside a house, as for a Flash minigame; otherwise <see cref="ActivityLaunchResult.Opened"/>. A hole opens no
    /// session that could block another game, so nothing else is checked. Unverified: a hole's start screen is sent
    /// differently from a Flash minigame's (FishingHandler.Join), and nobody has checked whether it shows in a house. It is
    /// refused there until someone who played the original says whether fishing worked from inside a house
    /// (docs/design/evergrove-housing.md, section 10); if it did, this check goes.
    /// </summary>
    public static ActivityLaunchResult FishingHole(Player player) =>
        player.Zone is HousingZone ? ActivityLaunchResult.InsideAHouse : ActivityLaunchResult.Opened;
}
