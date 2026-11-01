using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Minigames;

/// <summary>
/// Opens a Games menu entry for a player the way choosing it in the Games menu does, with the same checks (one game at a
/// time, among them). The Gateway serves the games, so it implements this (<c>Sanctuary.Gateway.ActivityLauncher</c>);
/// the quest manager uses it for the NPC who offers a play step's game
/// (<see cref="Resources.Definitions.QuestGoals.PlayActivityGoal.NpcId"/>).
/// </summary>
public interface IActivityLauncher
{
    /// <summary>
    /// Opens <paramref name="activityId"/>, a ClientActivityDefinitions.json id, as the Games menu would. False if it isn't
    /// an activity the Games menu opens, or the player is no longer connected.
    /// </summary>
    bool TryLaunch(Player player, int activityId);
}
