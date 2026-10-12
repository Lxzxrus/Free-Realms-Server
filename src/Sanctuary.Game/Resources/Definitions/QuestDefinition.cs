using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.Game.Resources.Definitions;

public sealed class QuestDefinition
{
    public int QuestId { get; set; }

    public int TitleId { get; set; }
    public int DescriptionId { get; set; }
    public int GiverDialogueId { get; set; }

    public int ObjectiveDescriptionId { get; set; }

    public int IconId { get; set; }

    public List<QuestGoal> Goals { get; set; } = [];

    public ulong GiverGuid { get; set; }

    public ulong TargetGuid { get; set; }

    public int RewardCoins { get; set; }

    public int RewardExperience { get; set; }

    public List<int> RewardItems { get; set; } = [];

    public int RewardCollectionId { get; set; }

    public int PrerequisiteQuestId { get; set; }
    public int NextQuestId { get; set; }

    public List<int> ExcludesQuestIds { get; set; } = [];

    public int NotificationAvailable { get; set; } = 2;
    public int NotificationActive { get; set; } = 6;

    public int TurnInDialogueId => Goals.Count > 0 ? Goals[^1].DialogueId : 0;

    /// <summary>The NPC a talk goal is with: its own target, or the quest's target when it names none.</summary>
    public ulong GoalTargetGuid(int goalIndex)
    {
        if (goalIndex >= 0 && goalIndex < Goals.Count && Goals[goalIndex].TargetGuid != 0)
            return Goals[goalIndex].TargetGuid;

        return TargetGuid;
    }

    /// <summary>
    /// Whether a player on goal <paramref name="goalIndex"/> still has to talk to this NPC, so it shows the quest's
    /// "?". Only the current step's NPC does: Diggy kept his after his step was done, while Brenna and Seaspray
    /// showed none (Ariel, 2026-10-12).
    /// </summary>
    public bool WaitsOn(int goalIndex, ulong npcGuid, IReadOnlySet<ulong> talkedNpcs)
    {
        if (goalIndex < 0 || goalIndex >= Goals.Count)
            return false;

        var goal = Goals[goalIndex];

        if (goal.Type != QuestGoalType.TalkToNpc)
            return false;

        if (goal.IsCountedTalk)
            return goal.AllTalkTargetGuids().Contains(npcGuid) && !talkedNpcs.Contains(npcGuid);

        return GoalTargetGuid(goalIndex) == npcGuid;
    }

    /// <summary>Every NPC whose marker can depend on this quest: the giver, the target and each step's NPCs.</summary>
    public IEnumerable<ulong> NpcGuids()
    {
        var seen = new HashSet<ulong>();

        if (GiverGuid != 0 && seen.Add(GiverGuid))
            yield return GiverGuid;

        if (TargetGuid != 0 && seen.Add(TargetGuid))
            yield return TargetGuid;

        foreach (var goal in Goals)
            foreach (var guid in goal.AllTalkTargetGuids())
                if (seen.Add(guid))
                    yield return guid;
    }

    public bool IsOfferableFor(IReadOnlyDictionary<int, bool> playerQuests)
    {
        if (playerQuests.ContainsKey(QuestId))
            return false;

        // Exclusions first: two quests that exclude each other can share a prerequisite, and checking the
        // prerequisite first returned before the exclusion was ever looked at, so both were offered. (Found by
        // ImAlko reviewing Sulphural's upstream PR #130.)
        foreach (var excludedId in ExcludesQuestIds)
            if (playerQuests.ContainsKey(excludedId))
                return false;

        if (PrerequisiteQuestId != 0)
            return playerQuests.TryGetValue(PrerequisiteQuestId, out var prerequisiteDone) && prerequisiteDone;

        return true;
    }
}
