using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions.Zones;

namespace Sanctuary.Game.Minigames;

/// <summary>Who decides a game's wins, which a quest step won by playing it counts (<see cref="Quests.ActivityWonEvent"/>).</summary>
public enum ActivityWins
{
    /// <summary>Nobody: no win is reported, as for fishing, the wheel or an activity the server doesn't serve.</summary>
    NotReported,

    /// <summary>The server: a match-three game is played on the server's own board, which decides the win.</summary>
    DecidedByServer,

    /// <summary>
    /// The client: Cooking's Flash plays every round by itself and reports the result, and the server accepts a win
    /// when it is plausible (<see cref="Mimic.MimicGame"/>: the recipe sent first, every round reported, at least
    /// <see cref="Mimic.MimicGameDefinition.MinSecondsPerPhase"/> a round). A modified client can report one without
    /// playing, so a step won there may lead to coins and stars, never to anything scarce.
    /// </summary>
    ReportedByClient
}

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
    /// Who decides the activity's wins, which the server tells quests of (<see cref="Quests.ActivityWonEvent"/>): the
    /// server for a match-three game, the client for a Cooking recipe. Fishing and the wheel have no win to report, so no
    /// quest step can be won there.
    /// </summary>
    public static ActivityWins WinsOf(int activityId, MimicDefinitions mimic) =>
        FlashMinigames.Definitions.ContainsKey(activityId) ? ActivityWins.DecidedByServer
        : mimic.TryFindActivity(activityId, out _, out _) ? ActivityWins.ReportedByClient
        : ActivityWins.NotReported;
}
