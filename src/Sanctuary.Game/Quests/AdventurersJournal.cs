using System.Collections.Generic;
using System.Linq;

using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Quests;

/// <summary>
/// The stories the Adventurer's Journal lists (the client's "Hero's Journal"). The client works out the journal's
/// current quest by itself: the first unfinished hub of a region, then that hub's lowest-numbered quest that isn't in
/// the finished set the server sends (<see cref="Sanctuary.Packet.AdventurersJournalQuestUpdatePacket"/>). The welcome
/// window's "Hero's Journal Quest" box shows that quest's title and objective, and its Go button opens the journal. The
/// client only has a quest's text while the quest is in the player's quest log, so a story lists its quests in the
/// order they are offered. How the client does it: docs/research/heros-journal.md.
/// </summary>
public static class AdventurersJournal
{
    /// <summary>The Cobblestone Village hub, the only hub in the journal's Cobblestone Village region (region 1).</summary>
    public const int CobblestoneVillageHubId = 3;

    /// <summary>
    /// Evergrove's first story, in the order it is played: Introduce Yourself (Samantha sends you to Ricky), then Call
    /// the Crew! (Ricky sends you to Shakey and back). Each quest is offered where the one before it is handed in.
    /// </summary>
    public static readonly IReadOnlyList<int> CobblestoneVillageStory = [2563, 1801];

    /// <summary>Every story, by the hub it fills.</summary>
    public static readonly IReadOnlyDictionary<int, IReadOnlyList<int>> Stories = new Dictionary<int, IReadOnlyList<int>>
    {
        [CobblestoneVillageHubId] = CobblestoneVillageStory
    };

    /// <summary>Whether the quest is in one of the journal's stories.</summary>
    public static bool IsStoryQuest(int questId) => Stories.Values.Any(story => story.Contains(questId));

    /// <summary>The info packet's hub quests: each story's quests, numbered in story order from 1.</summary>
    public static IEnumerable<AdventurersJournalHubQuestDefinition> HubQuests() =>
        Stories.SelectMany(story => story.Value.Select((questId, index) => new AdventurersJournalHubQuestDefinition
        {
            HubId = story.Key,
            Id = questId,

            // The quest's place in its hub: the client makes the lowest unfinished one the current quest.
            Unknown = index + 1
        }));

    /// <summary>
    /// True while the player has finished a story quest and hasn't taken the next one yet. Told what's finished, the
    /// client would make that next quest current, and as it isn't in the player's quest log, the welcome window's box
    /// would show neither a title nor an objective.
    /// </summary>
    public static bool IsBetweenStoryQuests(IReadOnlyDictionary<int, bool> playerQuests)
    {
        foreach (var story in Stories.Values)
        {
            var started = false;

            foreach (var questId in story)
            {
                if (!playerQuests.TryGetValue(questId, out var completed))
                {
                    if (started)
                        return true;

                    break;
                }

                if (!completed)
                    break;

                started = true;
            }
        }

        return false;
    }
}
