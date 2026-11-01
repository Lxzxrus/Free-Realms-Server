using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions.Zones;

namespace Sanctuary.Game.Minigames;

/// <summary>
/// The activities the server can open. The client's Games menu lists every activity the server sends, and
/// ClientActivityDefinitions.json holds all 417 of the client's, so most menu entries did nothing when clicked. A new
/// player shouldn't meet a menu entry that does nothing, so the menu gets only these.
/// </summary>
public static class PlayableActivities
{
    public static bool IsPlayable(IResourceManager resourceManager, int activityId) =>
        IsPlayable(activityId, resourceManager.Mimic, resourceManager.Zones, resourceManager.DailyWheel);

    /// <summary>A match-three game, a Cooking recipe, a fishing hole (a fishing zone with its id), or the wheel while it's on.</summary>
    public static bool IsPlayable(int activityId, MimicDefinitions mimic, ZoneDefinitionCollection zones, DailyWheel dailyWheel) =>
        FlashMinigames.Definitions.ContainsKey(activityId)
        || mimic.TryFindActivity(activityId, out _, out _)
        || zones.TryGetValue(activityId, out var zone) && zone is FishingZoneDefinition
        || activityId == DailyWheel.ActivityId && dailyWheel.Enabled;

    /// <summary>
    /// Whether the server tells quests when the player wins the activity (<see cref="Quests.ActivityWonEvent"/>), so a
    /// quest step can be won there: a match-three game or a Cooking recipe, whose wins the server's own game decides.
    /// Fishing and the wheel have no win to report.
    /// </summary>
    public static bool ReportsWins(int activityId, MimicDefinitions mimic) =>
        FlashMinigames.Definitions.ContainsKey(activityId) || mimic.TryFindActivity(activityId, out _, out _);
}
