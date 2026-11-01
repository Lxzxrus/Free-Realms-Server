using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Minigames;

/// <summary>What became of opening a Games menu entry for a player (<see cref="IActivityLauncher.Launch"/>).</summary>
public enum ActivityLaunchResult
{
    /// <summary>
    /// Opened as the Games menu opens it: a Flash minigame's start screen, or the activity handed to the wheel's or the
    /// fishing hole's own handler, which make their own checks (one spin a day...).
    /// </summary>
    Opened,

    /// <summary>The player is no longer connected, so nothing was sent.</summary>
    NotConnected,

    /// <summary>
    /// Not a game the Games menu opens on this server (the wheel, a Flash minigame it serves, a fishing hole), or not a
    /// Games menu entry at all: nothing was sent.
    /// </summary>
    NotAGamesMenuGame,

    /// <summary>The player has another Flash minigame or the wheel open: one game at a time, so nothing was sent.</summary>
    AnotherGameOpen
}

/// <summary>
/// Opens a Games menu entry for a player the way choosing it in the Games menu does, with the same checks (one game at a
/// time, among them). The Gateway serves the games, so it implements this (<c>Sanctuary.Gateway.ActivityLauncher</c>);
/// the quest manager uses it for the NPC who offers a play step's game
/// (<see cref="Resources.Definitions.QuestGoals.PlayActivityGoal.NpcId"/>), and a world object for its games
/// (<see cref="GameStationMenu"/>).
/// </summary>
public interface IActivityLauncher
{
    /// <summary>Opens <paramref name="activityId"/>, a ClientActivityDefinitions.json id, as the Games menu would.</summary>
    ActivityLaunchResult Launch(Player player, int activityId);
}

/// <summary>
/// What a player who picked a game from an NPC's or a world object's menu is told when it didn't open, so the quest's NPC
/// and the objects say the same thing.
/// </summary>
public static class ActivityLaunches
{
    /// <summary>
    /// The chat line for <paramref name="result"/>, or null when there is nothing to tell: the game opened, or what stopped
    /// it (a player who has left, a game the Games menu doesn't open) isn't the player's to fix.
    /// </summary>
    public static string? Explain(ActivityLaunchResult result) => result switch
    {
        ActivityLaunchResult.AnotherGameOpen => "Finish or close the game you have open first.",
        _ => null
    };
}
