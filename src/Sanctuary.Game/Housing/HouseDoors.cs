using System.Collections.Generic;
using System.Linq;

using Sanctuary.Database.Entities;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;

namespace Sanctuary.Game.Housing;

/// <summary>
/// Evergrove: the Housing doors, NPCs that take whoever clicks them into their account's own house
/// (<see cref="HouseDoorService"/>, <c>docs/design/evergrove-housing.md</c>, section 11). Doors are matched on
/// <c>Npc.DefinitionId</c>, their <c>Npcs.json</c> id, as job trainers are. Another door needs only its row there, its
/// <c>spawnFixedNpc</c> in a zone script and its id here.
/// </summary>
public static class HouseDoors
{
    /// <summary>The door behind Samantha in Cobblestone Village, where her Housing quest (9100) sends new players.</summary>
    public const int CobblestoneVillage = 90101;

    public static readonly IReadOnlySet<int> Ids = new HashSet<int> { CobblestoneVillage };

    /// <summary>
    /// How near the player has to be for the door to take them in: from further, the client walks them up to it and
    /// clicks again. The same reach as a quest step's NPC (<see cref="QuestGoal.InteractRange"/>), so the doorstep is
    /// where they come back to: the world saves where they stood as they left it.
    /// </summary>
    public const int WalkInRange = 12;

    /// <summary>The most houses the door's menu offers. My Houses lists them all.</summary>
    public const int MenuLimit = 6;

    /// <summary>
    /// How soon, in milliseconds, a door answers the same player's next click. Each answer reads the account's houses
    /// from the database on the main loop every player waits on, and a modified client can click as fast as it sends, so
    /// a click sooner does nothing. A person's double click is one click. A click from too far, which only walks the
    /// player up, isn't counted, so the one that ends the walk at the doorstep is answered. A pick from the door's menu
    /// needs a new click: the menu is gone once picked from.
    /// </summary>
    public const int MinUseIntervalMs = 500;

    public static bool IsDoor(int npcId) => Ids.Contains(npcId);

    /// <summary>Whether a step of the quest is at a Housing door, so it can only be done by a player with a house to go into.</summary>
    public static bool SendsThroughADoor(QuestDefinition quest) =>
        quest.Goals.OfType<TalkToNpcGoal>().Any(goal => goal.AllTalkTargetNpcIds().Any(IsDoor));

    /// <summary>
    /// The houses in the order the door offers them: the account's ranked plot first, then the most recently visited,
    /// at most <see cref="MenuLimit"/>. A visit is anyone's entry (<see cref="DbHouse.LastVisited"/>), and a house nobody
    /// has entered counts from when it was made, so a lot just bought comes before one entered a minute ago.
    /// </summary>
    public static List<DbHouse> Order(IEnumerable<DbHouse> houses) =>
        houses
            .OrderByDescending(house => house.Plot == HousePlot.Ranked)
            .ThenByDescending(house => house.LastVisited)
            .ThenBy(house => house.Id)
            .Take(MenuLimit)
            .ToList();

    /// <summary>
    /// What's wrong with how a quest uses a door, for the quest loader's warnings. A click on a door only takes the player
    /// into their house, completing a talk step there just before the move (<see cref="Quests.IQuestManager.AdvanceTalkStepsAt"/>),
    /// so in a quest a door can be nothing else. It doesn't give or take a quest, and isn't the NPC of a quest's last step:
    /// the hand-in can't follow the player into the house. It isn't one of a counted talk's NPCs or a game's, as its click
    /// never opens the quest's menu. And its step says nothing, as nobody stands there to say it.
    /// </summary>
    public static IEnumerable<string> QuestProblems(QuestDefinition quest)
    {
        if (IsDoor(quest.GiverNpcId))
            yield return $"is given by Housing door {quest.GiverNpcId}";

        if (IsDoor(quest.TargetNpcId))
            yield return $"is handed in to Housing door {quest.TargetNpcId}";

        var lastStepStart = quest.StepStartOf(quest.Goals.Count - 1);

        for (var goalIndex = 0; goalIndex < quest.Goals.Count; goalIndex++)
        {
            var goal = quest.Goals[goalIndex];

            if (goal is PlayActivityGoal playGoal && IsDoor(playGoal.NpcId))
                yield return $"has goal {goal.NameId}'s game offered by Housing door {playGoal.NpcId}";

            if (goal is not TalkToNpcGoal talkGoal)
                continue;

            foreach (var door in talkGoal.AllTalkTargetNpcIds().Where(IsDoor))
            {
                if (goalIndex >= lastStepStart)
                    yield return $"has goal {goal.NameId} at Housing door {door} in its last step";

                if (talkGoal.IsCounted)
                    yield return $"counts Housing door {door} among goal {goal.NameId}'s NPCs";

                if (talkGoal.CompleteAfterDialogue || talkGoal.ConversationFor(door).Count > 0)
                    yield return $"has a line for goal {goal.NameId} at Housing door {door}";
            }
        }
    }
}
