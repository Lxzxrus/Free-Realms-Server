using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

/// <summary>
/// A step won by playing a game: the player wins the Games menu entry <see cref="ActivityId"/> <c>RequiredCount</c>
/// times. Only <see cref="ActivityWonEvent"/> credits it, which the server raises for a win its own game decided, so
/// no message from the client can pass the step.
/// </summary>
public sealed class PlayActivityGoal : QuestGoal
{
    // One win, unless the quest asks for more.
    public PlayActivityGoal() => RequiredCount = 1;

    // A ClientActivityDefinitions.json id, e.g. 450 for Cooking: Simone's Spiralmint Steak. Only games whose wins the
    // server settles report them: the Cooking recipes in Mimic.json and the match-three games (FlashMinigames).
    public int ActivityId { get; set; }

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
