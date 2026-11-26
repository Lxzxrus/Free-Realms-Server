using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Extensions;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Interactions;
using Sanctuary.Game.Jobs;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Resources.Definitions.QuestGoals;
using Sanctuary.Game.Resources.Definitions.Rewards;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Quests;

public sealed class QuestManager : IQuestManager
{
    public const int AllGoalsDoneGoalId = 0;

    public const int GoalDoneCount = -1;

    private readonly IResourceManager _resourceManager;
    private readonly IRewardManager _rewardManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;
    private readonly ILogger<QuestManager> _logger;

    // Evergrove: a quest that teaches a job grants it through the job manager, which refreshes trainer badges through
    // this quest manager, so it is taken lazily: neither has to be built before the other.
    private readonly Lazy<IJobManager> _jobManager;

    // Evergrove: the NPC who offers a play step's game opens it the way the Games menu does, through the Gateway, which
    // serves the games (Sanctuary.Gateway.ActivityLauncher). Taken as it is, not lazily as the job manager is: nothing it
    // needs takes the quest manager, so the Gateway builds it with the quest manager as it starts, and a wiring mistake
    // stops the Gateway there rather than at a player's click.
    private readonly IActivityLauncher _activityLauncher;

    public QuestManager(IResourceManager resourceManager, IRewardManager rewardManager, IDbContextFactory<DatabaseContext> dbContextFactory,
        Lazy<IJobManager> jobManager, IActivityLauncher activityLauncher, ILogger<QuestManager> logger)
    {
        _resourceManager = resourceManager;
        _rewardManager = rewardManager;
        _dbContextFactory = dbContextFactory;
        _jobManager = jobManager;
        _activityLauncher = activityLauncher;
        _logger = logger;
    }

    // Evergrove: job trainers (Sanctuary.Game.Jobs.JobTrainers) carry the "available" badge until they have nothing
    // left to teach, so they go through the same badge refresh as quest NPCs. An NPC who offers a play step's game is in
    // ByTarget, as a talk step's NPC is.
    public bool IsQuestNpc(int npcId)
        => _resourceManager.Quests.ByGiver.ContainsKey(npcId) || _resourceManager.Quests.ByTarget.ContainsKey(npcId)
            || JobTrainers.IsTrainer(npcId);

    private IEnumerable<(QuestDefinition Quest, QuestProgress Progress, int GoalIndex, QuestGoal Goal)> ActiveGoals(Player player)
    {
        foreach (var progress in player.Quests.InProgress)
        {
            if (!_resourceManager.Quests.TryGet(progress.QuestId, out var quest))
                continue;

            foreach (var goalIndex in progress.OpenGoals.Keys.Order())
                yield return (quest, progress, goalIndex, quest.Goals[goalIndex]);
        }
    }

    // Evergrove: a shared quest pickup is taken only by a player whose quest needs it, so nobody empties a spawn for
    // nothing. Every shipped quest pickup is player-specific since upstream OSFR #166 (CanGatherQuestNode); this guards
    // a shared one.
    public bool NeedsCollectionNode(Player player, string nodeType)
        => ActiveGoals(player).Any(active => active.Goal is CollectGoal collectGoal
            && string.Equals(collectGoal.CollectNodeType, nodeType, StringComparison.OrdinalIgnoreCase));

    public List<NpcInteractionOption> GetInteractionOptions(Player player, Npc npc)
    {
        var options = new List<NpcInteractionOption>();
        var quests = _resourceManager.Quests;

        foreach (var progress in player.Quests.InProgress)
        {
            if (!quests.TryGet(progress.QuestId, out var activeQuest))
                continue;

            var quest = activeQuest;

            // Evergrove: while a step won by playing a game is open, the NPC it names offers the game.
            foreach (var goalIndex in progress.OpenGoals.Keys.Order())
            {
                if (quest.Goals[goalIndex] is not PlayActivityGoal playGoal || !playGoal.IsOfferedBy(npc.DefinitionId))
                    continue;

                options.Add(new NpcInteractionOption
                {
                    IconId = ContextIcons.QuestTurnIn,
                    ButtonTextId = GameButtonTextId(playGoal),
                    Invoke = interactingPlayer => OpenStepGame(interactingPlayer, quest, goalIndex)
                });
            }

            if (!AdvancesHere(quest, progress, npc) && !TurnsInHere(quest, progress, npc))
                continue;

            options.Add(new NpcInteractionOption
            {
                IconId = ContextIcons.QuestTurnIn,
                ButtonTextId = quest.TitleId,
                Invoke = interactingPlayer => AdvanceAtNpc(interactingPlayer, quest, npc)
            });
        }

        if (quests.ByGiver.TryGetValue(npc.DefinitionId, out var giverQuestIds))
        {
            foreach (var questId in giverQuestIds)
            {
                if (!quests.TryGet(questId, out var offerableQuest) || !offerableQuest.IsOfferableFor(player.Quests))
                    continue;

                var quest = offerableQuest;

                options.Add(new NpcInteractionOption
                {
                    IconId = ContextIcons.QuestOffer,
                    ButtonTextId = quest.TitleId,
                    Invoke = interactingPlayer => Offer(interactingPlayer, quest)
                });
            }
        }

        return options;
    }

    private static bool AdvancesHere(QuestDefinition quest, QuestProgress progress, Npc npc)
        => TalkGoalHere(quest, progress, npc) >= 0;

    private static int TalkGoalHere(QuestDefinition quest, QuestProgress progress, Npc npc)
    {
        foreach (var goalIndex in progress.OpenGoals.Keys.Order())
        {
            if (quest.Goals[goalIndex].Matches(quest, new NpcTalkedEvent(npc)))
                return goalIndex;
        }

        return -1;
    }

    private static bool TurnsInHere(QuestDefinition quest, QuestProgress progress, Npc npc)
        => progress.GoalIndex >= quest.Goals.Count && quest.GoalTargetNpcId(quest.Goals.Count - 1) == npc.DefinitionId;

    private void AdvanceAtNpc(Player player, QuestDefinition quest, Npc npc)
    {
        if (!player.Quests.TryGetInProgress(quest.QuestId, out var progress))
            return;

        if (TurnsInHere(quest, progress, npc))
        {
            TurnIn(player, quest);
            return;
        }

        var goalIndex = TalkGoalHere(quest, progress, npc);
        if (goalIndex < 0)
            return;

        var goal = quest.Goals[goalIndex];

        if (goal is TalkToNpcGoal { CompleteAfterDialogue: true })
        {
            QuestDialogue.Begin(player, goal.ConversationFor(npc.DefinitionId), npc.Guid, () =>
            {
                if (player.Quests.TryGetInProgress(quest.QuestId, out var current) && current.OpenGoals.ContainsKey(goalIndex))
                    CompleteGoal(player, quest, current, goalIndex, npc, playDialogue: false);
            });
            return;
        }

        var result = goal.Progress(quest, new NpcTalkedEvent(npc), progress.ProgressFor(goalIndex));

        ApplyProgress(player, quest, progress, goalIndex, result, npc);

        if (result != QuestGoalResult.Completed && goal is TalkToNpcGoal { IsCounted: true })
            QuestDialogue.Begin(player, goal.ConversationFor(npc.DefinitionId), npc.Guid);
    }

    // Evergrove: the game's name as the Games menu lists it (its ClientActivityDefinitions.json NameId, which names the
    // recipe), so two games at one NPC never share a button; else the title of its start screen (DisplayNameId, which
    // every Cooking recipe shares); else the step's own row.
    private int GameButtonTextId(PlayActivityGoal goal)
    {
        if (!_resourceManager.ClientActivityDefinitions.TryGetValue(goal.ActivityId, out var activity))
            return goal.NameId;

        if (activity.NameId != 0)
            return activity.NameId;

        return activity.DisplayNameId != 0 ? activity.DisplayNameId : goal.NameId;
    }

    // Evergrove: what the player is told when the NPC's game doesn't open because another is open, so a click never does
    // nothing without a word.
    private const string FinishYourGameFirst = "Finish or close the game you have open first.";

    // Evergrove: opens a play step's game for the player who picked it from the NPC's menu, if the step is still open (the
    // game may have been won from the Games menu since the menu was shown). It opens as the Games menu opens it, with the
    // same checks, such as one game at a time; the win then counts like any other. What stopped it is logged as what it
    // is: the player's other game, a player who has left, or a step naming a game the Games menu doesn't open.
    private void OpenStepGame(Player player, QuestDefinition quest, int goalIndex)
    {
        if (!player.Quests.TryGetInProgress(quest.QuestId, out var progress) || !progress.OpenGoals.ContainsKey(goalIndex)
            || quest.Goals[goalIndex] is not PlayActivityGoal playGoal)
            return;

        switch (_activityLauncher.Launch(player, playGoal.ActivityId))
        {
            case ActivityLaunchResult.AnotherGameOpen:
                ChatHelper.SendSystemMessage(player, FinishYourGameFirst);

                _logger.LogDebug("Quest {questId}'s step {goalId} didn't open activity {activityId} for character {characterId}: " +
                    "another game is open.", quest.QuestId, playGoal.NameId, playGoal.ActivityId, player.CharacterId);
                break;

            case ActivityLaunchResult.NotConnected:
                _logger.LogInformation("Quest {questId}'s step {goalId} didn't open activity {activityId} for character {characterId}: " +
                    "they are no longer connected.", quest.QuestId, playGoal.NameId, playGoal.ActivityId, player.CharacterId);
                break;

            case ActivityLaunchResult.NotAGamesMenuGame:
                _logger.LogWarning("Quest {questId}'s step {goalId} couldn't open activity {activityId} for character {characterId}: " +
                    "it isn't a game the Games menu opens.", quest.QuestId, playGoal.NameId, playGoal.ActivityId, player.CharacterId);
                break;
        }
    }

    public void OnEvent(Player player, QuestEvent questEvent)
    {
        var activeGoals = ActiveGoals(player).ToList();

        // Evergrove: an event that credits one quest (a pickup, a game won) goes to the tracked quest when it can take it,
        // then to the others by quest id, rather than in whatever order the quest log's dictionary holds them.
        if (questEvent.CreditsOneQuest)
        {
            var trackedQuestId = player.Quests.ActiveQuestId;

            activeGoals = activeGoals
                .OrderBy(active => active.Quest.QuestId != trackedQuestId)
                .ThenBy(active => active.Quest.QuestId)
                .ToList();
        }

        foreach (var (quest, progress, goalIndex, goal) in activeGoals)
        {
            if (!progress.OpenGoals.TryGetValue(goalIndex, out var goalProgress))
                continue;

            var result = goal.Progress(quest, questEvent, goalProgress);

            if (result == QuestGoalResult.None)
                continue;

            ApplyProgress(player, quest, progress, goalIndex, result);

            if (questEvent.CreditsOneQuest)
                return;
        }
    }

    private void ApplyProgress(Player player, QuestDefinition quest, QuestProgress progress, int goalIndex, QuestGoalResult result, Npc? npc = null)
    {
        if (result == QuestGoalResult.Completed)
        {
            CompleteGoal(player, quest, progress, goalIndex, npc);
            return;
        }

        if (result == QuestGoalResult.Progressed)
        {
            var goal = quest.Goals[goalIndex];
            var goalProgress = progress.ProgressFor(goalIndex);

            player.SendTunneled(new QuestObjectiveUpdatePacket
            {
                QuestId = quest.QuestId,
                ObjectiveId = goal.NameId,
                CurrentCount = goalProgress.Count,
                CompletedPercentage = QuestCompletion(quest, progress.GoalsDone),
                Unknown6 = RetailObjectiveUpdateUnknown6
            });

            SaveGoalProgress(player, quest.QuestId, goal.NameId, goalProgress);

            if (npc is not null)
                RefreshQuestNotification(player, npc.Guid);
        }

        RefreshObjectiveTarget(player);
    }

    private void SaveQuestRow(Player player, int questId, Action<DbCharacterQuest> update)
    {
        using var db = _dbContextFactory.CreateDbContext();
        var dbQuest = db.CharacterQuests.FirstOrDefault(x => x.QuestId == questId && x.CharacterId == player.CharacterId);
        if (dbQuest is null)
            return;

        update(dbQuest);
        db.SaveChanges();
    }

    private void SaveGoalProgress(Player player, int questId, int goalId, GoalProgress progress)
    {
        using var db = _dbContextFactory.CreateDbContext();

        db.CharacterQuestProgress
            .Where(p => p.CharacterId == player.CharacterId && p.QuestId == questId && p.GoalId == goalId)
            .ExecuteDelete();

        foreach (var (targetId, count) in progress.Save())
        {
            db.CharacterQuestProgress.Add(new DbCharacterQuestProgress
            {
                CharacterId = player.CharacterId,
                QuestId = questId,
                GoalId = goalId,
                TargetId = targetId,
                Count = count
            });
        }

        db.SaveChanges();
    }

    private void SaveGoalDone(Player player, int questId, int goalId)
    {
        using var db = _dbContextFactory.CreateDbContext();

        db.CharacterQuestProgress
            .Where(p => p.CharacterId == player.CharacterId && p.QuestId == questId && p.GoalId == goalId)
            .ExecuteDelete();

        db.CharacterQuestProgress.Add(new DbCharacterQuestProgress
        {
            CharacterId = player.CharacterId,
            QuestId = questId,
            GoalId = goalId,
            TargetId = 0,
            Count = GoalDoneCount
        });

        db.SaveChanges();
    }

    private void DeleteSavedGoalProgress(Player player, int questId)
    {
        using var db = _dbContextFactory.CreateDbContext();

        db.CharacterQuestProgress
            .Where(p => p.CharacterId == player.CharacterId && p.QuestId == questId)
            .ExecuteDelete();
    }

    private void SaveActiveQuestId(Player player, int questId)
    {
        using var db = _dbContextFactory.CreateDbContext();

        var dbCharacter = db.Characters.FirstOrDefault(c => c.Id == player.CharacterId);
        if (dbCharacter is null)
            return;

        dbCharacter.ActiveQuestId = questId != 0 ? questId : null;
        db.SaveChanges();
    }

    public void AcceptQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest) || !quest.IsOfferableFor(player.Quests))
            return;

        var progress = new QuestProgress(questId);
        progress.StartStep(quest, 0);

        player.Quests.Add(progress);
        player.Quests.ActiveQuestId = questId;
        player.Quests.LastAcceptedAt = DateTime.UtcNow;

        using (var db = _dbContextFactory.CreateDbContext())
        {
            db.CharacterQuests.Add(new DbCharacterQuest
            {
                QuestId = questId,
                CharacterId = player.CharacterId,
                Completed = false,
                CurrentGoalId = quest.Goals[0].NameId,
                Started = progress.Started
            });

            var dbCharacter = db.Characters.FirstOrDefault(c => c.Id == player.CharacterId);
            if (dbCharacter is not null)
                dbCharacter.ActiveQuestId = questId;

            db.SaveChanges();
        }

        SendActiveState(player, quest);

        // A login between two story quests left the finished set out (RestoreJournal); with the next one taken, the
        // client can have it and show this quest as the journal's current one.
        if (AdventurersJournal.IsStoryQuest(questId))
            SendJournalQuestStates(player);

        RefreshQuestNotifications(player, quest);

        RefreshQuestNodes(player, quest);

        player.SendTunneled(new CommandPacketQuestDialogComplete());
    }

    public bool CompleteQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return false;

        if (!player.Quests.TryGetInProgress(questId, out var progress) || progress.GoalIndex < quest.Goals.Count)
            return false;

        player.Quests.Complete(questId);
        SaveQuestRow(player, questId, q => q.Completed = true);

        if (player.Quests.ActiveQuestId == questId)
        {
            player.Quests.ActiveQuestId = 0;
            SaveActiveQuestId(player, 0);
        }

        player.SendTunneled(new QuestCompletePacket { QuestId = questId });

        player.SendTunneled(new CompletedQuestCountUpdatePacket
        {
            Count = player.Quests.CompletedCount
        });

        SendJournalQuestStates(player);

        TeachJob(player, quest);

        GrantReward(player, quest);

        RefreshQuestNotifications(player, quest);

        if (quest.NextQuestId is { } nextQuestId && _resourceManager.Quests.TryGet(nextQuestId, out var next))
            RefreshQuestNotifications(player, next.GiverNpcId);

        RefreshObjectiveTarget(player);
        return true;
    }

    public bool OfferNextQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest)
            || quest.NextQuestId is not { } nextQuestId
            || !_resourceManager.Quests.TryGet(nextQuestId, out var next)
            || next.GiverNpcId != quest.GoalTargetNpcId(quest.Goals.Count - 1)
            || !next.IsOfferableFor(player.Quests))
            return false;

        Offer(player, next, NextOfferDelayMs);
        return true;
    }

    private const int NextOfferDelayMs = 750;

    private static readonly TimeSpan AbandonBlockedAfterAccept = TimeSpan.FromSeconds(3);

    public void AbandonQuest(Player player, int questId)
    {
        if (DateTime.UtcNow - player.Quests.LastAcceptedAt < AbandonBlockedAfterAccept)
            return;

        if (!player.Quests.TryGetInProgress(questId, out _))
            return;

        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return;

        RemoveQuest(player, quest);
    }

    public bool ResetQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest) || !player.Quests.Contains(questId))
            return false;

        var wasCompleted = player.Quests.IsCompleted(questId);

        RemoveQuest(player, quest);

        if (wasCompleted)
            player.SendTunneled(new CompletedQuestCountUpdatePacket { Count = player.Quests.CompletedCount });

        return true;
    }

    private void RemoveQuest(Player player, QuestDefinition quest)
    {
        var questId = quest.QuestId;

        player.Quests.Remove(questId);
        QuestDialogue.Clear(player);

        if (player.Quests.PendingTurnInQuestId == questId)
            player.Quests.PendingTurnInQuestId = 0;

        var wasActive = player.Quests.ActiveQuestId == questId;
        if (wasActive)
            player.Quests.ActiveQuestId = 0;

        using (var db = _dbContextFactory.CreateDbContext())
        {
            var dbQuest = db.CharacterQuests.FirstOrDefault(x => x.QuestId == questId && x.CharacterId == player.CharacterId);
            if (dbQuest is not null)
                db.CharacterQuests.Remove(dbQuest);

            if (wasActive)
            {
                var dbCharacter = db.Characters.FirstOrDefault(c => c.Id == player.CharacterId);
                if (dbCharacter is not null)
                    dbCharacter.ActiveQuestId = null;
            }

            db.SaveChanges();
        }

        player.SendTunneled(new QuestAbandonedPacket { QuestId = questId });

        RefreshQuestNodes(player, quest);

        RefreshQuestNotifications(player, quest);

        RefreshObjectiveTarget(player);
    }

    public void SetActiveQuest(Player player, int questId)
    {
        if (!_resourceManager.Quests.TryGet(questId, out var quest))
            return;

        if (player.Quests.TryGetInProgress(questId, out var progress))
        {
            // Not written to the database here: the client sends one of these per click in the quest helper. The
            // tracked quest is saved when a quest is accepted and with the character at logout.
            player.Quests.ActiveQuestId = questId;

            foreach (var goalIndex in progress.OpenGoals.Keys.Order())
                SendObjectiveActivated(player, questId, quest.Goals[goalIndex]);

            SendObjectiveForGoal(player, quest, progress.FirstOpenGoalIndex);
        }
    }

    public ClientQuestData GetClientQuestData(Player player)
    {
        var questData = new ClientQuestData
        {
            CompletedCount = player.Quests.CompletedCount,
            ActiveQuestId = player.Quests.TryGetInProgress(player.Quests.ActiveQuestId, out _) ? player.Quests.ActiveQuestId : 0
        };

        foreach (var progress in player.Quests.InProgress)
        {
            if (_resourceManager.Quests.TryGet(progress.QuestId, out var quest))
                questData.Quests.Add(ClientQuestFor(player, quest, progress));
        }

        return questData;
    }

    public void SendActiveObjectives(Player player)
    {
        foreach (var (quest, _, _, goal) in ActiveGoals(player))
            SendObjectiveActivated(player, quest.QuestId, goal);
    }

    public void RestoreJournal(Player player)
    {
        RefreshObjectiveTarget(player);

        // The welcome window, sent next, shows the journal's current quest, which the client picks with the finished
        // set. Between two story quests it would pick the one not taken yet and show it without any text; without the
        // set it says there are no quests. The set follows once the next one is taken (AcceptQuest, AdventurersJournal).
        if (!AdventurersJournal.IsBetweenStoryQuests(player.Quests))
            SendJournalQuestStates(player);
    }

    private void SendJournalQuestStates(Player player)
    {
        var states = new Dictionary<int, int>();
        foreach (var progress in player.Quests.All)
            if (progress.Completed)
                states[progress.QuestId] = 1;

        if (states.Count > 0)
            player.SendTunneled(new AdventurersJournalQuestUpdatePacket { QuestStates = states });
    }

    private void RefreshQuestNotifications(Player player, QuestDefinition quest)
    {
        var npcIds = new HashSet<int> { quest.GiverNpcId, quest.TargetNpcId };

        foreach (var goal in quest.Goals.OfType<TalkToNpcGoal>())
            npcIds.UnionWith(goal.AllTalkTargetNpcIds());

        // Evergrove: the NPC who offers a play step's game shows the "?" while the step is open.
        npcIds.UnionWith(quest.Goals.OfType<PlayActivityGoal>().Select(goal => goal.NpcId));

        npcIds.UnionWith(quest.StateNpcIds());

        foreach (var excludedId in quest.ExcludesQuestIds)
        {
            if (!_resourceManager.Quests.TryGet(excludedId, out var excludedQuest))
                continue;

            npcIds.Add(excludedQuest.GiverNpcId);
            npcIds.Add(excludedQuest.TargetNpcId);
        }

        foreach (var npcId in npcIds)
            RefreshQuestNotifications(player, npcId);
    }

    private static void RemoveHiddenNpcs(Player player, QuestGoal goal)
    {
        foreach (var npcId in goal.HideNpcIdsWhenDone)
        {
            foreach (var npc in player.VisibleNpcs.Values.Where(npc => npc.DefinitionId == npcId).ToList())
            {
                player.SendTunneled(EntityHelper.GetRemovePlayerGracefullyPacket(npc.Guid, animate: goal.HideEffectId != 0,
                    delay: goal.HideDelayMs, effectDelay: goal.HideDelayMs, compositeEffectId: goal.HideEffectId));
                player.VisibleNpcs.TryRemove(npc.Guid, out _);
            }
        }
    }

    private void RefreshQuestNotifications(Player player, int npcId)
    {
        if (npcId == 0)
            return;

        foreach (var npc in player.Zone.Npcs.Where(npc => npc.DefinitionId == npcId).ToList())
            RefreshQuestNotification(player, npc.Guid);
    }

    public void RefreshQuestNotification(Player player, ulong npcGuid)
    {
        if (!player.VisibleNpcs.TryGetValue(npcGuid, out var npc))
            return;

        var removeNotifications = new PlayerUpdatePacketRemoveNotifications();

        foreach (var (iconState, effectId) in QuestBadgeIconStates(npc))
            removeNotifications.Notifications.Add(new RemoveNotificationInfo { Guid = npc.Guid, ThoughtBubbleIconId = iconState, CompositeEffectId = effectId });

        if (npc.Notification is { } notification)
        {
            removeNotifications.Notifications.Add(new RemoveNotificationInfo
            {
                Guid = npc.Guid,
                ThoughtBubbleIconId = notification.IconState,
                CompositeEffectId = notification.Unknown2
            });
        }

        if (removeNotifications.Notifications.Count > 0)
            player.SendTunneled(removeNotifications);

        var questBadge = GetQuestBadge(player, npc);

        var cursorEntry = questBadge is not null && !ReferenceEquals(questBadge, npc.Notification)
            ? InteractionMenuHelper.GetQuestCursorEntry(npc.Guid)
            : InteractionMenuHelper.GetOwnCursorEntry(npc);
        player.SendTunneled(new PlayerUpdatePacketNpcRelevance { Entries = { cursorEntry } });

        if ((questBadge ?? npc.Notification) is { } badge)
            player.SendTunneled(new PlayerUpdatePacketAddNotifications { Notifications = { badge } });
    }

    private const int QuestBadgeNotificationType = 2;
    private const int AvailableBadgeIconId = 2;

    public NotificationInfo? GetQuestBadge(Player player, Npc npc)
    {
        var (imageId, waiting) = QuestBadgeImageId(player, npc);
        if (imageId == 0)
            return null;

        if (waiting && npc.Notification is not null)
            return npc.Notification;

        return new NotificationInfo
        {
            Guid = npc.Guid,
            IsCompact = false,
            NotificationType = QuestBadgeNotificationType,
            IconId = waiting ? 0 : AvailableBadgeIconId,
            IconState = imageId,
            NameId = npc.NameId,
            ReferenceId = npc.SubTextNameId,
            Enabled = true
        };
    }

    private (int ImageId, bool Waiting) QuestBadgeImageId(Player player, Npc npc)
    {
        var quests = _resourceManager.Quests;

        if (quests.ByGiver.TryGetValue(npc.DefinitionId, out var giverQuestIds))
        {
            foreach (var questId in giverQuestIds)
            {
                if (quests.TryGet(questId, out var quest) && quest.IsOfferableFor(player.Quests))
                    return (quest.NotificationAvailable, false);
            }
        }

        if (quests.ByTarget.TryGetValue(npc.DefinitionId, out var targetQuestIds))
        {
            foreach (var questId in targetQuestIds)
            {
                if (player.Quests.TryGetInProgress(questId, out var progress) && quests.TryGet(questId, out var quest)
                    && quest.IsWaitingOn(progress, npc))
                    return (quest.NotificationActive, true);
            }
        }

        var trainerImageId = JobTrainers.NotificationImageFor(npc.DefinitionId,
            profileId => player.Profiles.Any(profile => profile.Id == profileId), _resourceManager.Profiles.ContainsKey);

        if (trainerImageId != 0)
            return (trainerImageId, false);

        return (0, false);
    }

    public bool CanGatherQuestNode(Player player, CollectionNode node)
        => ActiveGoals(player).Any(active => active.Goal is CollectGoal collectGoal
            && collectGoal.CanGather(node, active.Progress.OpenGoals[active.GoalIndex]));

    private void RefreshQuestNodes(Player player, QuestDefinition quest)
    {
        var nodeTypes = quest.Goals.OfType<CollectGoal>().Select(goal => goal.CollectNodeType).ToHashSet();
        if (nodeTypes.Count == 0)
            return;

        foreach (var node in player.Zone.Npcs.OfType<CollectionNode>()
            .Where(node => node.PoolDefinition.PlayerSpecific && nodeTypes.Contains(node.TypeDefinition.Key)).ToList())
        {
            var hidden = !CanGatherQuestNode(player, node);
            var shown = player.VisibleNpcs.ContainsKey(node.Guid);

            if (hidden && shown)
                player.OnRemoveVisibleNpcGracefully(node, animate: true);
            else if (!hidden && !shown && node.VisiblePlayers.ContainsKey(player.Guid))
                player.OnAddVisibleNpcs(node);
        }
    }

    public bool IsHiddenByQuest(Player player, Npc npc)
    {
        if (npc is CollectionNode { PoolDefinition.PlayerSpecific: true } questNode)
            return !CanGatherQuestNode(player, questNode);

        var quests = _resourceManager.Quests;

        if (!quests.ByStateNpc.TryGetValue(npc.DefinitionId, out var questIds))
            return false;

        foreach (var questId in questIds)
        {
            if (quests.TryGet(questId, out var quest) && quest.HidesNpcFor(player.Quests, npc.DefinitionId))
                return true;
        }

        return false;
    }

    public int GetQuestEffectId(Player player, Npc npc)
    {
        var quests = _resourceManager.Quests;

        if (!quests.ByStateNpc.TryGetValue(npc.DefinitionId, out var questIds))
            return 0;

        foreach (var questId in questIds)
        {
            if (quests.TryGet(questId, out var quest) && quest.NpcEffectIdFor(player.Quests, npc.DefinitionId) is var effectId and not 0)
                return effectId;
        }

        return 0;
    }

    private static void RefreshNpcEffects(Player player, QuestGoal goal)
    {
        foreach (var effect in goal.NpcEffectsUntilDone)
        {
            foreach (var npc in player.VisibleNpcs.Values.Where(npc => npc.DefinitionId == effect.NpcId).ToList())
            {
                player.OnRemoveVisibleNpcs(npc);
                player.OnAddVisibleNpcs(npc);
            }
        }
    }

    private HashSet<(int IconState, int EffectId)> QuestBadgeIconStates(Npc npc)
    {
        var quests = _resourceManager.Quests;
        var states = new HashSet<(int IconState, int EffectId)>();

        if (quests.ByGiver.TryGetValue(npc.DefinitionId, out var giverQuestIds))
        {
            foreach (var questId in giverQuestIds)
            {
                if (quests.TryGet(questId, out var quest) && quest.NotificationAvailable != 0)
                    states.Add((quest.NotificationAvailable, 0));
            }
        }

        if (quests.ByTarget.TryGetValue(npc.DefinitionId, out var targetQuestIds))
        {
            foreach (var questId in targetQuestIds)
            {
                if (quests.TryGet(questId, out var quest) && quest.NotificationActive != 0)
                    states.Add((quest.NotificationActive, 0));
            }
        }

        if (JobTrainers.IsTrainer(npc.DefinitionId))
            states.Add((JobTrainers.NotificationImageId, 0));

        return states;
    }

    private const float DefaultOfferCameraDistance = 0f;

    private void Offer(Player player, QuestDefinition quest, int delayMs = 0)
    {
        var offer = new QuestInfoPacket
        {
            QuestId = quest.QuestId,
            NpcDialogId = quest.GiverDialogueId,
            NpcAnimationId = QuestDialogue.TalkAnimationId,
            NameId = quest.TitleId,
            DescriptionId = quest.DescriptionId,
            MembersOnly = false,
            NpcGuid = NearestNpcGuid(player, quest.GiverNpcId),
            CameraDistance = quest.CameraDistance ?? DefaultOfferCameraDistance,
            Unknown11 = false,
            AcceptOnly = false
        };

        FillRewardBundle(offer.RewardBundle, quest);

        if (delayMs > 0)
            player.SendTunneledDelayed(offer, delayMs);
        else
            player.SendTunneled(offer);
    }

    private void FillRewardBundle(RewardBundleBase bundle, QuestDefinition quest)
    {
        bundle.Coins = quest.RewardCoins;
        bundle.Experience = quest.RewardExperience;
        bundle.Unknown3 = 0;
        bundle.Multiplier = 1f;
        bundle.IconId = -1;
        bundle.NameId = -1;

        foreach (var entry in BuildRewardEntries(quest))
            bundle.Entries.Add(entry);

        bundle.Success = bundle.Entries.Any(entry => entry is RewardBundleEntryCollectionAdd);
    }

    private List<RewardBundleEntryBase> BuildRewardEntries(QuestDefinition quest)
    {
        var entries = new List<RewardBundleEntryBase>();
        foreach (var definitionId in quest.RewardItems)
        {
            if (_resourceManager.ClientItemDefinitions.TryGetValue(definitionId, out var itemDef))
            {
                entries.Add(new RewardBundleEntryItem
                {
                    IconId = itemDef.Icon.Id,
                    TintId = itemDef.Icon.TintId,
                    NameId = itemDef.NameId,
                    DefinitionId = definitionId,
                    Quantity = 1
                });
            }
        }

        if (quest.RewardCollectionId != 0 &&
            _resourceManager.Collections.TryGetValue(quest.RewardCollectionId, out var collectionDef))
        {
            entries.Add(new RewardBundleEntryCollectionAdd
            {
                IconId = collectionDef.IconId,
                TintId = collectionDef.IconTintId,
                NameId = collectionDef.NameId,
                CollectionId = collectionDef.Id
            });
        }

        return entries;
    }

    private static ulong NearestUntalkedTarget(Player player, TalkToNpcGoal goal, GoalProgress goalProgress)
    {
        var untalkedNpcIds = goal.AllTalkTargetNpcIds()
            .Where(npcId => goalProgress is not TalkProgress talkProgress || !talkProgress.HasTalkedTo(npcId))
            .ToHashSet();

        return NearestNpc(player, npc => untalkedNpcIds.Contains(npc.DefinitionId))?.Guid ?? 0;
    }

    // Evergrove: a Collect step's target is the nearest pickup the player can still take for it. Upstream OSFR #166 hid
    // the target on Collect steps instead, because a player-specific pickup stays in the zone after a player takes it
    // (only that player stops seeing it), so the nearest one of its type was often one they already had. CanGather is
    // the pickup's own test (the step's node type, a player-specific pool, not yet taken by this player), so the target
    // is never a pickup the player has taken or couldn't pick up.
    private static ulong NearestPickup(Player player, QuestDefinition quest, int goalIndex, CollectGoal goal)
    {
        if (!player.Quests.TryGetInProgress(quest.QuestId, out var progress)
            || !progress.OpenGoals.TryGetValue(goalIndex, out var goalProgress))
            return 0;

        return NearestNpc(player, npc => npc is CollectionNode node && goal.CanGather(node, goalProgress))?.Guid ?? 0;
    }

    private void CompleteGoal(Player player, QuestDefinition quest, QuestProgress progress, int goalIndex, Npc? spokenBy = null, bool playDialogue = true)
    {
        var goals = quest.Goals;
        var completedGoal = goals[goalIndex];

        var stepDone = progress.FinishGoal(quest, goalIndex);
        bool isFinalGoal = progress.GoalIndex >= goals.Count;

        player.SendTunneled(new QuestObjectiveCompletePacket
        {
            QuestId = quest.QuestId,
            ObjectiveId = completedGoal.NameId,
            Percent = QuestCompletion(quest, progress.GoalsDone),
            Silent = isFinalGoal
        });

        if (stepDone)
        {
            SaveQuestRow(player, quest.QuestId, q => q.CurrentGoalId = isFinalGoal ? AllGoalsDoneGoalId : goals[progress.GoalIndex].NameId);
            DeleteSavedGoalProgress(player, quest.QuestId);
        }
        else
        {
            SaveGoalDone(player, quest.QuestId, completedGoal.NameId);
        }

        RemoveHiddenNpcs(player, completedGoal);
        RefreshNpcEffects(player, completedGoal);
        RefreshQuestNodes(player, quest);
        RefreshQuestNotifications(player, quest);

        if (isFinalGoal)
        {
            TurnIn(player, quest);
            return;
        }

        if (stepDone)
        {
            foreach (var openGoalIndex in progress.OpenGoals.Keys.Order())
            {
                player.SendTunneled(new QuestObjectiveAddedPacket
                {
                    QuestId = quest.QuestId,
                    Objective = ObjectiveFor(goals[openGoalIndex], ObjectiveActive)
                });
                SendObjectiveActivated(player, quest.QuestId, goals[openGoalIndex], restored: false);
            }
        }

        // The marker follows the tracked quest: another quest's step moving on mustn't point it elsewhere.
        if (quest.QuestId == player.Quests.ActiveQuestId)
            SendObjectiveForGoal(player, quest, progress.FirstOpenGoalIndex);
        else
            RefreshObjectiveTarget(player);

        if (playDialogue && completedGoal is TalkToNpcGoal)
        {
            var speakerNpcId = spokenBy?.DefinitionId ?? quest.GoalTargetNpcId(goalIndex);
            var speakerGuid = spokenBy?.Guid ?? NearestNpcGuid(player, speakerNpcId);

            QuestDialogue.Begin(player, completedGoal.ConversationFor(speakerNpcId), speakerGuid);
        }
    }

    private const int YouGotItTextId = 103085;

    private const int GreenCheckImageId = 300;

    private const int GreenButtonImageSet = 17;

    private void TurnIn(Player player, QuestDefinition quest)
    {
        var end = new QuestEndPacket
        {
            NpcGuid = NearestNpcGuid(player, quest.GoalTargetNpcId(quest.Goals.Count - 1)),
            QuestId = quest.QuestId,
            EndDialogTextId = quest.TurnInDialogueId,
            NpcAnimationId = QuestDialogue.TalkAnimationId,
            CameraDistance = quest.CameraDistance ?? DefaultOfferCameraDistance
        };

        FillRewardBundle(end.RewardBundle, quest);

        player.SendTunneled(end);

        player.Quests.PendingTurnInQuestId = quest.QuestId;
    }

    private const int QuestListRewardUnknown3 = 15;
    private const int CoinsIconId = 102;
    private const int CoinsTextId = 6513;
    private const int ExperienceIconId = 101;

    private RewardBundleBase QuestListRewardBundle(Player player, QuestDefinition quest)
    {
        var bundle = new RewardBundleBase
        {
            Coins = quest.RewardCoins,
            Experience = quest.RewardExperience,
            Unknown3 = QuestListRewardUnknown3,
            PlayerGuid = player.Guid
        };

        bundle.Entries.AddRange(BuildRewardEntries(quest));

        if (bundle.Entries.Count > 0)
        {
            bundle.IconId = bundle.Entries[0].IconId;
            bundle.NameId = bundle.Entries[0].NameId;
        }
        else if (quest.RewardCoins > 0)
        {
            bundle.IconId = CoinsIconId;
            bundle.NameId = CoinsTextId;
        }
        else if (quest.RewardExperience > 0)
        {
            bundle.IconId = ExperienceIconId;
        }

        return bundle;
    }

    private ClientQuest NewClientQuest(Player player, QuestDefinition quest, QuestProgress progress, bool suppressStartBanner) => new()
    {
        QuestId = quest.QuestId,
        TitleId = quest.TitleId,
        DescriptionId = quest.DescriptionId,
        HelperTextId = quest.ObjectiveDescriptionId,
        MembersOnly = false,
        TimeStarted = progress.Started.ToUnixTimeSeconds(),
        ProfileId = 0,
        CompletedPercentage = QuestCompletion(quest, progress.GoalsDone),
        SystemQuest = false,
        SuppressStartBanner = suppressStartBanner,
        RewardBundle = QuestListRewardBundle(player, quest)
    };

    private ClientQuest ClientQuestFor(Player player, QuestDefinition quest, QuestProgress progress)
    {
        var clientQuest = NewClientQuest(player, quest, progress, suppressStartBanner: true);
        var goals = quest.Goals;

        for (int i = 0; i < progress.StepEnd; i++)
        {
            clientQuest.Objectives[i + 1] = progress.IsGoalDone(i)
                ? ObjectiveFor(goals[i], ObjectiveComplete)
                : ObjectiveFor(goals[i], ObjectiveInactive, progress.ProgressFor(i).Count);
        }

        return clientQuest;
    }

    private void SendActiveState(Player player, QuestDefinition quest)
    {
        if (!player.Quests.TryGetInProgress(quest.QuestId, out var progress))
            return;

        player.SendTunneled(new QuestAddPacket
        {
            Quest = NewClientQuest(player, quest, progress, suppressStartBanner: false),
            ShowInTracker = quest.QuestId == player.Quests.ActiveQuestId
        });

        var goals = quest.Goals;

        for (int i = 0; i < progress.StepEnd; i++)
        {
            player.SendTunneled(new QuestObjectiveAddedPacket
            {
                QuestId = quest.QuestId,
                Objective = progress.IsGoalDone(i)
                    ? ObjectiveFor(goals[i], ObjectiveComplete)
                    : ObjectiveFor(goals[i], ObjectiveActive, progress.ProgressFor(i).Count)
            });
        }

        foreach (var goalIndex in progress.OpenGoals.Keys.Order())
            SendObjectiveActivated(player, quest.QuestId, goals[goalIndex], restored: false);

        SendObjectiveForGoal(player, quest, progress.FirstOpenGoalIndex);
    }

    private const int ObjectiveInactive = 0;
    private const int ObjectiveActive = 1;
    private const int ObjectiveComplete = 2;

    private static QuestObjectiveInfo ObjectiveFor(QuestGoal goal, int state, int currentCount = 0) => new()
    {
        ObjectiveId = goal.NameId,
        NameId = goal.NameId,
        DescriptionId = goal.DescriptionId != 0 ? goal.DescriptionId : goal.NameId,
        State = state,
        CurrentCount = state == ObjectiveComplete ? goal.RequiredCount : currentCount,
        RequiredCount = goal.RequiredCount
    };

    private static void SendObjectiveActivated(Player player, int questId, QuestGoal goal, bool restored = true)
    {
        player.SendTunneled(new QuestObjectiveActivatedPacket
        {
            QuestId = questId,
            ObjectiveId = goal.NameId,
            RequiredCount = restored ? goal.RequiredCount : 0,
            Unknown2 = restored
        });
    }

    private const int RetailObjectiveUpdateUnknown6 = 82;

    private static float QuestCompletion(QuestDefinition quest, int goalsDone) => (float)goalsDone / quest.Goals.Count;

    private static Npc? NearestNpc(Player player, int npcId) => NearestNpc(player, npc => npc.DefinitionId == npcId);

    private static Npc? NearestNpc(Player player, Func<Npc, bool> match)
    {
        Npc? nearest = null;
        var best = float.MaxValue;

        foreach (var npc in player.Zone.Npcs)
        {
            if (!match(npc))
                continue;

            var dx = npc.Position.X - player.Position.X;
            var dz = npc.Position.Z - player.Position.Z;
            var distance = dx * dx + dz * dz;

            if (distance < best)
            {
                best = distance;
                nearest = npc;
            }
        }

        return nearest;
    }

    private static ulong NearestNpcGuid(Player player, int npcId)
        => npcId != 0 && NearestNpc(player, npcId) is { } npc ? npc.Guid : 0;

    private ulong ResolveGoalTargetGuid(Player player, QuestDefinition quest, int goalIndex)
    {
        var goals = quest.Goals;

        // Never the turn-in NPC: a Collect step with no pickup left to take has no target at all.
        if (goalIndex >= 0 && goalIndex < goals.Count && goals[goalIndex] is CollectGoal collectGoal)
            return NearestPickup(player, quest, goalIndex, collectGoal);

        if (goalIndex >= 0 && goalIndex < goals.Count && goals[goalIndex] is TalkToNpcGoal { IsCounted: true } countedGoal
            && player.Quests.TryGetInProgress(quest.QuestId, out var progress))
        {
            var untalked = NearestUntalkedTarget(player, countedGoal, progress.ProgressFor(goalIndex));
            if (untalked != 0)
                return untalked;
        }

        return NearestNpcGuid(player, quest.GoalTargetNpcId(goalIndex));
    }

    /// <summary>Where a goal's objective marker stands: the NPC or pickup it points at (0 for a place), its name, where.</summary>
    internal readonly record struct ObjectiveTarget(ulong Guid, int NameId, Vector3 Position);

    /// <summary>
    /// The objective marker for a goal, which the client's Take Me There button and map pin follow, or null when it points
    /// at nothing. The marker packet, the Take Me There counter and the tracker all take it from here. When there is
    /// none, <paramref name="clear"/> says whether the last marker comes down (a hidden objective, a step won by playing
    /// a game that no NPC offers, a reach step in another zone, a Collect step with nothing left to take here: the client
    /// shows Take Me There only while there is a target) or stays as it is (an NPC that isn't in this zone), as upstream
    /// does.
    /// </summary>
    internal ObjectiveTarget? ObjectiveTargetFor(Player player, QuestDefinition quest, int goalIndex, out bool clear)
    {
        var goals = quest.Goals;
        var goal = goalIndex >= 0 && goalIndex < goals.Count ? goals[goalIndex] : null;

        clear = true;

        if (quest.HideObjectiveTarget)
            return null;

        // A step won by playing a game points at the NPC who offers the game (PlayActivityGoal.NpcId), as a talk step
        // points at its NPC. One that no NPC offers is played from the Games menu alone: there is nothing to point at, and
        // the last step's marker comes down rather than send the player back to its NPC.
        if (goal is PlayActivityGoal { NpcId: 0 })
            return null;

        if (goal is ReachLocationGoal reachGoal && reachGoal.ReachPosition != Vector3.Zero)
        {
            return reachGoal.ZoneDefinitionId == player.Zone.DefinitionId
                ? new ObjectiveTarget(0, goal.NameId, reachGoal.ReachPosition)
                : null;
        }

        // Every goal done (waiting to be handed in) leaves goalIndex at Goals.Count: the target is then the turn-in NPC.
        var targetGuid = ResolveGoalTargetGuid(player, quest, goalIndex);

        if (targetGuid != 0 && player.Zone.TryGetNpc(targetGuid, out var target))
            return new ObjectiveTarget(targetGuid, target.NameId, new Vector3(target.Position.X, target.Position.Y, target.Position.Z));

        clear = goal is CollectGoal;
        return null;
    }

    private void SendObjectiveForGoal(Player player, QuestDefinition quest, int goalIndex)
    {
        if (ObjectiveTargetFor(player, quest, goalIndex, out var clear) is { } target)
        {
            player.SendTunneled(new ObjectiveTargetUpdatePacket
            {
                Active = true,
                TaskId = player.ObjectiveTaskId,
                Guid = target.Guid,
                NameId = target.NameId,
                PositionX = target.Position.X,
                PositionY = target.Position.Y,
                PositionZ = target.Position.Z,
                PositionW = 1f
            });
        }
        else if (clear)
        {
            player.SendTunneled(new ObjectiveTargetUpdatePacket { Active = false });
        }
    }

    public void RefreshObjectiveTarget(Player player)
    {
        if (TryGetTrackedGoal(player, out var quest, out var goalIndex))
            SendObjectiveForGoal(player, quest, goalIndex);
        else
            player.SendTunneled(new ObjectiveTargetUpdatePacket { Active = false });
    }

    // Evergrove: where the tracked goal's objective stands, the same target as the objective marker, for the Take Me
    // There counter in the client's Lua metrics (TakeMeThereRequestPacketHandler). The button itself asks for a path
    // with ClientPathRequestPacket, which is answered from the navmesh as upstream OSFR does.
    public bool TryGetActiveObjectiveTarget(Player player, out Vector3 targetPosition)
    {
        targetPosition = default;

        if (!TryGetTrackedGoal(player, out var quest, out var goalIndex)
            || ObjectiveTargetFor(player, quest, goalIndex, out _) is not { } target)
            return false;

        targetPosition = target.Position;
        return true;
    }

    private bool TryGetTrackedGoal(Player player, out QuestDefinition quest, out int goalIndex)
    {
        var activeQuestId = player.Quests.ActiveQuestId;

        if (activeQuestId != 0 && TryGetTrackableGoal(player, activeQuestId, out quest, out goalIndex))
            return true;

        quest = null!;
        goalIndex = -1;
        return false;
    }

    private bool TryGetTrackableGoal(Player player, int questId, out QuestDefinition quest, out int goalIndex)
    {
        quest = null!;
        goalIndex = -1;
        if (!_resourceManager.Quests.TryGet(questId, out var q) || !player.Quests.TryGetInProgress(questId, out var progress))
            return false;

        int currentGoalIndex = progress.FirstOpenGoalIndex;

        if (ObjectiveTargetFor(player, q, currentGoalIndex, out _) is null)
            return false;

        quest = q;
        goalIndex = currentGoalIndex;
        return true;
    }

    // Evergrove: a quest that teaches a job (TeachesProfileId) gives it at the turn-in through the trainer's own grant,
    // JobManager.Grant: saved, its kit sent, and the player switched into it with the job swirl. It comes before the
    // rewards, so the quest's stars can go to the new job (RewardProfileId). A player who has the job already keeps the
    // job they have on.
    private void TeachJob(Player player, QuestDefinition quest)
    {
        var profileId = quest.TeachesProfileId;

        if (profileId == 0 || player.Profiles.Any(profile => profile.Id == profileId))
            return;

        JobGrantResult result;

        // The quest is saved as done already, so whatever the grant throws (the database out of reach for a moment) is
        // logged, and the rewards are paid all the same: the job can still be had from its trainer.
        try
        {
            result = _jobManager.Value.Grant(player, profileId);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Quest {questId} teaches job {profileId}, but giving it to character {characterId} failed; " +
                "the rewards are paid all the same.", quest.QuestId, profileId, player.CharacterId);
            return;
        }

        if (result != JobGrantResult.Granted)
        {
            _logger.LogWarning("Quest {questId} teaches job {profileId}, but character {characterId} wasn't given it: {result}.",
                quest.QuestId, profileId, player.CharacterId, result);
        }
    }

    // Evergrove: the reward payout is Evergrove's from before the merge, verified in game: coins saved without a toast
    // of their own, the stars paid to a job (task 31; upstream's version only shows them), and each reward item fitted to
    // the player's gender in its own tint, with the non-bundled item toast.
    private void GrantReward(Player player, QuestDefinition quest)
    {
        var coins = quest.RewardCoins;
        if (coins > 0)
        {
            int newTotal;
            using (var db = _dbContextFactory.CreateDbContext())
            {
                var dbCharacter = db.Characters.FirstOrDefault(c => c.Id == player.CharacterId);
                if (dbCharacter is null)
                    return;

                dbCharacter.Coins += coins;
                db.SaveChanges();
                newTotal = dbCharacter.Coins;
            }

            player.Coins = newTotal;
            player.SendTunneled(new ClientUpdatePacketCoinCount { Coins = newTotal });
        }

        // The stars go to the quest's job (RewardProfileId) when the character has it, whichever job they wear, else to
        // the job they have on. The celebration below shows them, so no toast of their own.
        var experience = quest.RewardExperience;
        if (experience > 0)
        {
            var profileId = quest.RewardProfileId != 0 && player.Profiles.Any(profile => profile.Id == quest.RewardProfileId)
                ? quest.RewardProfileId
                : player.ActiveProfileId;

            using var db = _dbContextFactory.CreateDbContext();
            RewardHelper.TryGrantExperience(_resourceManager, db, _logger, player, profileId, experience, showReward: false);
        }

        var grantedCollection = quest.RewardCollectionId != 0 &&
            _resourceManager.Collections.TryGetValue(quest.RewardCollectionId, out var rewardedCollection)
                ? rewardedCollection
                : null;

        if (coins > 0 || experience > 0 || grantedCollection is not null)
        {
            var celebration = new RewardBundlePacket();

            // Success also gates whether each entry's tail is written; only the actual
            // collection id needs one here, so only flip it when there is a collection to report.
            celebration.RewardBundle.Success = grantedCollection is not null;
            celebration.RewardBundle.Coins = coins;
            celebration.RewardBundle.Experience = experience;
            celebration.RewardBundle.Unknown3 = 0;
            celebration.RewardBundle.Multiplier = 1f;
            celebration.RewardBundle.IconId = -1;
            celebration.RewardBundle.NameId = -1;

            if (grantedCollection is not null)
            {
                celebration.RewardBundle.Entries.Add(new RewardBundleEntryCollectionAdd
                {
                    IconId = grantedCollection.IconId,
                    TintId = grantedCollection.IconTintId,
                    NameId = grantedCollection.NameId,
                    CollectionId = grantedCollection.Id
                });
            }

            player.SendTunneled(celebration);
        }

        foreach (var itemDefinitionId in quest.RewardItems)
        {
            var granted = _resourceManager.ClientItemDefinitions.TryGetValue(itemDefinitionId, out var rewardDef)
                ? _resourceManager.ClientItemDefinitions.ForGender(rewardDef, player.Gender).Id
                : itemDefinitionId;

            GrantItem(player, granted);

            player.SendTunneled(new RewardNonBundledItemPacket { ItemDefinitionId = granted, Quantity = 1 });
        }
    }

    private void GrantItem(Player player, int definitionId)
    {
        if (!_resourceManager.ClientItemDefinitions.TryGetValue(definitionId, out var itemDef))
            return;

        itemDef = _resourceManager.ClientItemDefinitions.ForGender(itemDef, player.Gender);
        definitionId = itemDef.Id;

        int tint = ItemTints.Default(itemDef);

        int itemId, count;
        using (var db = _dbContextFactory.CreateDbContext())
        {
            var row = db.Characters
                .Where(c => c.Id == player.CharacterId)
                .Select(c => new
                {
                    Character = c,
                    Item = c.Items.FirstOrDefault(i => i.Definition == definitionId && i.Tint == tint),
                    NextId = c.Items.Max(i => (int?)i.Id) ?? 0
                })
                .FirstOrDefault();

            if (row is null)
                return;

            if (row.Item is not null)
            {
                row.Item.Count += 1;
                itemId = row.Item.Id;
                count = row.Item.Count;
            }
            else
            {
                var dbItem = new DbItem { Id = row.NextId + 1, Definition = definitionId, Tint = tint, Count = 1 };
                row.Character.Items.Add(dbItem);
                itemId = dbItem.Id;
                count = 1;
            }

            db.SaveChanges();
        }

        var clientItem = player.Items.FirstOrDefault(x => x.Definition == definitionId && x.Tint == tint);
        if (clientItem is not null)
        {
            clientItem.Count = count;
            player.SendTunneled(new ClientUpdatePacketItemUpdate { ItemGuid = clientItem.Id, Count = clientItem.Count });
        }
        else
        {
            clientItem = new ClientItem { Id = itemId, Tint = tint, Count = count, Definition = definitionId };
            player.Items.Add(clientItem);

            using var writer = new PacketWriter();
            clientItem.Serialize(writer);
            itemDef.Serialize(writer);
            player.SendTunneled(new ClientUpdatePacketItemAdd { Payload = writer.Buffer });
        }
    }
}
