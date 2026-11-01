using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

/// <summary>
/// A step won by playing a game: the player wins the Games menu entry <see cref="ActivityId"/> <c>RequiredCount</c>
/// times. Only <see cref="ActivityWonEvent"/> credits it, which the server raises for a win its own game decided, so
/// no message from the client can pass the step. While the step is open, the NPC it names (<see cref="NpcId"/>) offers
/// the game, and is the step's target.
/// </summary>
public sealed class PlayActivityGoal : QuestGoal
{
    // One win, unless the quest asks for more.
    public PlayActivityGoal() => RequiredCount = 1;

    // A ClientActivityDefinitions.json id, e.g. 450 for Cooking: Simone's Spiralmint Steak. Only games whose wins the
    // server settles report them: the Cooking recipes in Mimic.json and the match-three games (FlashMinigames).
    public int ActivityId { get; set; }

    // The NPC (an Npcs.json id) whose menu offers the game while the step is open, opening it as the Games menu does, and
    // so the step's target: its marker, Take Me There and "?" point at them, as a talk step's do. 0 = none: the game is
    // then started from the Games menu alone, and the step has no target. A stopgap until clicking a world object, such
    // as Simone's cooking table, starts its game.
    public int NpcId { get; set; }

    /// <summary>Whether <paramref name="npcId"/> is the NPC who offers the step's game.</summary>
    public bool IsOfferedBy(int npcId) => NpcId != 0 && NpcId == npcId;

    public override bool Matches(QuestDefinition quest, QuestEvent questEvent)
        => ActivityId != 0
            && RequiredCount > 0
            && questEvent is ActivityWonEvent { ActivityId: var activityId }
            && activityId == ActivityId;

    public override QuestGoalResult Progress(QuestDefinition quest, QuestEvent questEvent, GoalProgress progress)
    {
        if (!Matches(quest, questEvent))
            return QuestGoalResult.None;

        progress.Increment();

        return progress.Count >= RequiredCount ? QuestGoalResult.Completed : QuestGoalResult.Progressed;
    }
}
