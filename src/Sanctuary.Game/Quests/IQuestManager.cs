using System.Collections.Generic;
using System.Numerics;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Interactions;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Quests;

public interface IQuestManager
{
    bool IsQuestNpc(int npcId);

    List<NpcInteractionOption> GetInteractionOptions(Player player, Npc npc);

    void OnEvent(Player player, QuestEvent questEvent);

    /// <summary>
    /// Evergrove: completes each quest's open talk step at the NPC, without a line, for an NPC the player goes through
    /// rather than talks to, such as a Housing door, just before the move. A step in a quest's last step is left open and
    /// logged: completing it could hand the quest in, and the hand-in can't follow the player through.
    /// </summary>
    void AdvanceTalkStepsAt(Player player, Npc npc);

    bool CanGatherQuestNode(Player player, CollectionNode node);

    void AcceptQuest(Player player, int questId);

    bool CompleteQuest(Player player, int questId);

    bool OfferNextQuest(Player player, int questId);

    void AbandonQuest(Player player, int questId);

    bool ResetQuest(Player player, int questId);

    void SetActiveQuest(Player player, int questId);

    ClientQuestData GetClientQuestData(Player player);

    void SendActiveObjectives(Player player);

    void RestoreJournal(Player player);

    NotificationInfo? GetQuestBadge(Player player, Npc npc);

    bool IsHiddenByQuest(Player player, Npc npc);

    int GetQuestEffectId(Player player, Npc npc);

    void RefreshQuestNotification(Player player, ulong npcGuid);

    void RefreshObjectiveTarget(Player player);

    /// <summary>
    /// Evergrove: whether one of the player's quests is on a Collect goal for this kind of pickup. A quest pickup
    /// with nothing to give (homework, tools, tin ore) can be taken only then.
    /// </summary>
    bool NeedsCollectionNode(Player player, string nodeType);

    /// <summary>Evergrove: where the tracked goal's objective stands, for Take Me There.</summary>
    bool TryGetActiveObjectiveTarget(Player player, out Vector3 targetPosition);
}
