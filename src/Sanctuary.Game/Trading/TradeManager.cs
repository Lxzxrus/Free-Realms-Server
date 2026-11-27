using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Sanctuary.Core.IO;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Trading;

public sealed class TradeManager : ITradeManager, IDisposable
{
    private const int TradingCardItemType = 7;

    private readonly object _sync = new();
    private readonly Dictionary<int, TradeSession> _sessions = [];

    // Open trade windows (active or confirming) only. An unanswered request is not here, so it never makes either
    // player busy: the target can still be asked by others, ask others, and answer whichever request they like.
    private readonly Dictionary<ulong, TradeSession> _sessionsByPlayer = [];
    private readonly Dictionary<ulong, TradeSession> _committingByPlayer = [];

    // Each player has at most one request out. Sending another replaces it.
    private readonly Dictionary<ulong, TradeSession> _pendingByInviter = [];
    private readonly Dictionary<ulong, long> _lastRequestTimestamps = [];
    private readonly Dictionary<(ulong Inviter, ulong Invitee), long> _declinedTimestamps = [];
    private readonly ConcurrentQueue<Action> _externalActions = new();
    private readonly ILogger<TradeManager> _logger;
    private readonly IResourceManager _resourceManager;
    private readonly ITradeCommitter _committer;
    private readonly TradeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _cleanupTimer;
    private int _externalDrainScheduled;
    private int _nextSessionId = Random.Shared.Next(1, int.MaxValue - 1);

    /// <summary>Tests: receives every packet instead of the player's connection.</summary>
    internal Action<Player, ISerializablePacket>? PacketSink { get; init; }

    /// <summary>Tests: runs the sends of a transition on the caller's thread, once the lock is released.</summary>
    internal bool RunExternalActionsInline { get; init; }

    public TradeManager(
        ILogger<TradeManager> logger,
        IResourceManager resourceManager,
        ITradeCommitter committer,
        TradeOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(resourceManager);
        ArgumentNullException.ThrowIfNull(committer);

        _logger = logger;
        _resourceManager = resourceManager;
        _committer = committer;
        _options = options ?? new TradeOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;

        ValidateOptions(_options);

        _cleanupTimer = _timeProvider.CreateTimer(
            static state => ((TradeManager)state!).CleanupExpiredAndInvalidSessions(),
            this,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    public bool CanTrade(Player requester, Player target)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(target);

        lock (_sync)
        {
            return IsEligiblePair(requester, target)
                && !IsBusyLocked(requester.Guid)
                && !IsBusyLocked(target.Guid);
        }
    }

    public void RequestTrade(Player requester, Player target)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(target);

        RunTransition(actions => RequestTradeLocked(requester, target, actions));
    }

    public void HandleClientCommand(Player player, TradeCommand command)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(command);

        RunTransition(actions => HandleClientCommandLocked(player, command, actions));
    }

    public void OnDisconnected(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        RunTransition(actions => OnDisconnectedLocked(player, actions));
    }

    public void OnZoning(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        RunTransition(actions => OnZoningLocked(player, actions));
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();

        TradeSession[] committing;
        TradeSession[] active;
        lock (_sync)
        {
            committing = _committingByPlayer.Values.Distinct().ToArray();
            active = _sessions.Values.ToArray();
        }

        foreach (TradeSession session in active)
        {
            session.CommitCancellation.Dispose();
        }

        foreach (TradeSession session in committing)
        {
            CancelCommit(session);
        }
    }

    private void RequestTradeLocked(Player requester, Player target, List<Action> actions)
    {
        if (!IsEligiblePair(requester, target))
        {
            _logger.LogDebug(
                "Rejected an ineligible player trade request. ( Requester: {RequesterGuid}, Target: {TargetGuid} )",
                requester.Guid,
                target.Guid);
            return;
        }

        string? refusal = GetRequestRefusalLocked(requester, target);
        if (refusal is not null)
        {
            QueueSystemMessage(actions, requester, refusal);
            _logger.LogDebug(
                "Refused a player trade request. ( Requester: {RequesterGuid}, Target: {TargetGuid}, Reason: {Reason} )",
                requester.Guid,
                target.Guid,
                refusal);
            return;
        }

        // A new request replaces the requester's older one, and clears it from that player's screen.
        if (_pendingByInviter.TryGetValue(requester.Guid, out TradeSession? previous))
        {
            ClosePendingLocked(previous, actions, inviterNotice: null);
        }

        int sessionId = AllocateSessionIdLocked();
        long createdTimestamp = _timeProvider.GetTimestamp();
        var session = new TradeSession(sessionId, requester, target, createdTimestamp);

        _sessions.Add(sessionId, session);
        _pendingByInviter.Add(requester.Guid, session);
        _lastRequestTimestamps[requester.Guid] = createdTimestamp;

        QueueSend(actions, target, BaseTradePacket.CreateInvite(requester.Name, requester.Guid, sessionId));
        QueueSystemMessage(actions, requester, $"You sent {target.Name.FullName} a trade request.");
        _logger.LogInformation(
            "Created a player trade invitation. ( Session: {SessionId}, Inviter: {InviterGuid}, Invitee: {InviteeGuid} )",
            sessionId,
            requester.Guid,
            target.Guid);
    }

    private void HandleClientCommandLocked(Player player, TradeCommand command, List<Action> actions)
    {
        if (command.SessionId <= 0
            || !_sessions.TryGetValue(command.SessionId, out TradeSession? session)
            || !session.Contains(player)
            || (session.State != TradeSessionState.Pending
                && (!_sessionsByPlayer.TryGetValue(player.Guid, out TradeSession? playerSession)
                    || !ReferenceEquals(session, playerSession))))
        {
            _logger.LogDebug(
                "Ignored a player trade packet for an unknown or mismatched session. ( Player: {PlayerGuid}, Session: {SessionId}, Command: {CommandType} )",
                player.Guid,
                command.SessionId,
                command.GetType().Name);
            return;
        }

        if (!EnsureSessionContextLocked(session, actions))
        {
            return;
        }

        switch (command)
        {
            case TradeInviteReplyCommand inviteReply:
                HandleInviteReplyLocked(session, player, inviteReply.Accepted, actions);
                break;
            case TradeAcceptCommand accept:
                HandleAcceptLocked(session, player, accept.Revision, actions);
                break;
            case TradeCancelCommand:
                CancelLocked(session, player, actions);
                break;
            case TradeLockCommand tradeLock:
                HandleLockLocked(session, player, tradeLock.Revision, tradeLock.Locked, actions);
                break;
            case TradeOfferItemCommand offerItem:
                HandleOfferItemLocked(session, player, offerItem.ItemGuid, offerItem.ResultingCount, actions);
                break;
            case TradeCoinCountCommand coinCount:
                HandleCoinCountLocked(session, player, coinCount.ResultingCoins, actions);
                break;
            default:
                _logger.LogDebug(
                    "Ignored an unsupported player trade command. ( Player: {PlayerGuid}, Session: {SessionId}, Command: {CommandType} )",
                    player.Guid,
                    command.SessionId,
                    command.GetType().Name);
                break;
        }
    }

    private void OnDisconnectedLocked(Player player, List<Action> actions)
    {
        foreach (TradeSession pending in PendingSessionsOfLocked(player))
        {
            if (pending.IsInvitee(player))
            {
                ClosePendingLocked(
                    pending,
                    actions,
                    inviterNotice: $"Your trade request to {player.Name.FullName} was canceled.",
                    notifyInvitee: false);
            }
            else
            {
                // Native message 2 also clears the request from the invitee's action queue.
                ClosePendingLocked(pending, actions, inviterNotice: null, TradeMessageCode.PartnerDisconnected);
            }
        }

        if (_sessionsByPlayer.TryGetValue(player.Guid, out TradeSession? session))
        {
            if (!session.Contains(player))
            {
                return;
            }

            Player other = session.Other(player);
            RemoveSessionLocked(session);

            QueueSend(actions, other, BaseTradePacket.CreateMessage(TradeMessageCode.PartnerDisconnected));
            QueueSend(actions, other, BaseTradePacket.CreateEndSession(session.Id));

            _logger.LogInformation(
                "Closed a player trade after disconnect. ( Session: {SessionId}, Player: {PlayerGuid} )",
                session.Id,
                player.Guid);
            return;
        }

        if (_committingByPlayer.TryGetValue(player.Guid, out TradeSession? committingSession)
            && committingSession.Contains(player))
        {
            committingSession.Interruption = new TradeInterruption(TradeInterruptionKind.Disconnected, player);
            CancelCommit(committingSession);
        }
    }

    private void OnZoningLocked(Player player, List<Action> actions)
    {
        foreach (TradeSession pending in PendingSessionsOfLocked(player))
        {
            ClosePendingLocked(
                pending,
                actions,
                inviterNotice: pending.IsInvitee(player)
                    ? $"Your trade request to {player.Name.FullName} was canceled."
                    : null);
        }

        if (_sessionsByPlayer.TryGetValue(player.Guid, out TradeSession? session))
        {
            if (!session.Contains(player))
            {
                return;
            }

            Player partner = session.Other(player);
            RemoveSessionLocked(session);
            QueueSend(actions, player, BaseTradePacket.CreateMessage(TradeMessageCode.SelfChangedInstance));
            QueueSend(actions, partner, BaseTradePacket.CreateMessage(TradeMessageCode.PartnerChangedInstance));
            QueueSend(actions, session.Inviter, BaseTradePacket.CreateEndSession(session.Id));
            QueueSend(actions, session.Invitee, BaseTradePacket.CreateEndSession(session.Id));
            _logger.LogInformation(
                "Closed a player trade before zoning. ( Session: {SessionId}, Player: {PlayerGuid} )",
                session.Id,
                player.Guid);
            return;
        }

        if (_committingByPlayer.TryGetValue(player.Guid, out TradeSession? committingSession)
            && committingSession.Contains(player))
        {
            committingSession.Interruption ??= new TradeInterruption(TradeInterruptionKind.Zoning, player);
            CancelCommit(committingSession);
        }
    }

    private void HandleInviteReplyLocked(
        TradeSession session,
        Player player,
        bool accepted,
        List<Action> actions)
    {
        if (session.State != TradeSessionState.Pending || !session.IsInvitee(player))
        {
            return;
        }

        Touch(session);

        if (!accepted)
        {
            DeclineLocked(session, actions);
            return;
        }

        if (!IsEligiblePair(session.Inviter, session.Invitee))
        {
            AbortForContextLocked(session, actions);
            return;
        }

        if (IsBusyLocked(session.Inviter.Guid) || IsBusyLocked(session.Invitee.Guid))
        {
            // Not expected: opening a window withdraws both players' other requests. Never open a second window.
            ClosePendingLocked(session, actions, inviterNotice: null);
            QueueSystemMessage(actions, session.Invitee, $"{session.Inviter.Name.FullName} is busy trading right now.");
            return;
        }

        // Both players are now trading with each other, so every other request either of them sent or received is
        // withdrawn and cleared from the other player's screen.
        foreach (TradeSession other in _sessions.Values
            .Where(candidate => !ReferenceEquals(candidate, session)
                && candidate.State == TradeSessionState.Pending
                && (candidate.Contains(session.Inviter) || candidate.Contains(session.Invitee)))
            .ToArray())
        {
            Player trader = other.Contains(session.Inviter) ? session.Inviter : session.Invitee;
            ClosePendingLocked(
                other,
                actions,
                inviterNotice: other.IsInvitee(trader) ? $"{trader.Name.FullName} started another trade." : null);
        }

        DetachPendingLocked(session);
        session.State = TradeSessionState.Active;
        session.Revision = 1;
        _sessionsByPlayer.Add(session.Inviter.Guid, session);
        _sessionsByPlayer.Add(session.Invitee.Guid, session);

        QueueSend(
            actions,
            session.Inviter,
            BaseTradePacket.CreateStartSession(session.Invitee.Guid, session.Invitee.Name, session.Id));
        QueueSend(
            actions,
            session.Invitee,
            BaseTradePacket.CreateStartSession(session.Inviter.Guid, session.Inviter.Name, session.Id));

        _logger.LogInformation(
            "Started a player trade session. ( Session: {SessionId}, Inviter: {InviterGuid}, Invitee: {InviteeGuid} )",
            session.Id,
            session.Inviter.Guid,
            session.Invitee.Guid);
    }

    private void HandleOfferItemLocked(
        TradeSession session,
        Player player,
        int itemGuid,
        int resultingCount,
        List<Action> actions)
    {
        if (session.State != TradeSessionState.Active || itemGuid <= 0 || resultingCount < 0)
        {
            return;
        }

        TradeOffer offer = session.OfferFor(player);
        TradeOffer otherOffer = session.OtherOffer(player);
        if (offer.Locked)
        {
            return;
        }

        offer.Items.TryGetValue(itemGuid, out int previousCount);
        if (previousCount == resultingCount)
        {
            return;
        }

        ClientItem? item = null;
        if (resultingCount > 0)
        {
            if (!TryGetTradeableItem(player, itemGuid, out item, out int availableCount)
                || resultingCount > availableCount
                || (previousCount == 0 && offer.Items.Count >= _options.MaximumDistinctItemsPerPlayer))
            {
                QueueSend(actions, player, BaseTradePacket.CreateMessage(TradeMessageCode.OwnItemInvalid));
                return;
            }
        }
        else if (previousCount == 0)
        {
            return;
        }

        Touch(session);
        bool partnerWasLocked = otherOffer.Locked;
        ResetConfirmationAfterOfferChange(session);

        if (resultingCount == 0)
        {
            offer.Items.Remove(itemGuid);
        }
        else
        {
            offer.Items[itemGuid] = resultingCount;
        }

        int revision = AdvanceRevision(session);
        Player other = session.Other(player);

        if (resultingCount == 0)
        {
            QueueSend(
                actions,
                player,
                BaseTradePacket.CreateUpdateItemCount(session.Id, revision, isSelf: true, itemGuid, 0));
            QueueSend(actions, other, BaseTradePacket.CreateItemRemoved(session.Id, revision, itemGuid));
        }
        else if (previousCount == 0)
        {
            ItemInstance offeredItem = CreateOfferedItem(item!, resultingCount);
            QueueSend(
                actions,
                player,
                BaseTradePacket.CreateUpdateAddItem(session.Id, revision, isSelf: true, offeredItem));
            QueueSend(
                actions,
                other,
                BaseTradePacket.CreateUpdateAddItem(session.Id, revision, isSelf: false, offeredItem));
        }
        else
        {
            QueueSend(
                actions,
                player,
                BaseTradePacket.CreateUpdateItemCount(session.Id, revision, isSelf: true, itemGuid, resultingCount));
            QueueSend(
                actions,
                other,
                BaseTradePacket.CreateUpdateItemCount(session.Id, revision, isSelf: false, itemGuid, resultingCount));
        }

        if (partnerWasLocked)
        {
            QueueSend(actions, other, BaseTradePacket.CreateUpdateLock(session.Id, locked: false));
        }
    }

    private void HandleCoinCountLocked(
        TradeSession session,
        Player player,
        int offeredCoins,
        List<Action> actions)
    {
        if (session.State != TradeSessionState.Active || offeredCoins < 0 || offeredCoins > player.Coins)
        {
            return;
        }

        TradeOffer offer = session.OfferFor(player);
        TradeOffer otherOffer = session.OtherOffer(player);
        if (offer.Locked || offer.Coins == offeredCoins)
        {
            return;
        }

        Touch(session);
        bool partnerWasLocked = otherOffer.Locked;
        ResetConfirmationAfterOfferChange(session);
        offer.Coins = offeredCoins;

        int revision = AdvanceRevision(session);
        Player other = session.Other(player);

        QueueSend(
            actions,
            player,
            BaseTradePacket.CreateUpdateCoinCount(session.Id, revision, isSelf: true, offeredCoins));
        QueueSend(
            actions,
            other,
            BaseTradePacket.CreateUpdateCoinCount(session.Id, revision, isSelf: false, offeredCoins));

        if (partnerWasLocked)
        {
            QueueSend(actions, other, BaseTradePacket.CreateUpdateLock(session.Id, locked: false));
        }
    }

    private void HandleLockLocked(
        TradeSession session,
        Player player,
        int revision,
        bool locked,
        List<Action> actions)
    {
        if (session.State is TradeSessionState.Pending or TradeSessionState.Closed)
        {
            return;
        }

        TradeOffer offer = session.OfferFor(player);
        if (revision != session.Revision)
        {
            if (locked && session.State == TradeSessionState.Active && !offer.Locked)
            {
                QueueSend(actions, player, BaseTradePacket.CreateUpdateLock(session.Id, locked: false));
            }

            return;
        }

        Player other = session.Other(player);

        if (!locked)
        {
            if (!offer.Locked)
            {
                return;
            }

            offer.Locked = false;
            session.State = TradeSessionState.Active;
            session.ConfirmSentTimestamp = null;
            session.InviterOffer.Accepted = false;
            session.InviteeOffer.Accepted = false;
            Touch(session);
            QueueSend(actions, other, BaseTradePacket.CreateUpdateLock(session.Id, locked: false));
            return;
        }

        if (session.State != TradeSessionState.Active || offer.Locked)
        {
            return;
        }

        if (!ValidateOffer(player, offer))
        {
            QueueSend(actions, player, BaseTradePacket.CreateMessage(TradeMessageCode.OwnItemInvalid));
            return;
        }

        Touch(session);
        offer.Locked = true;
        QueueSend(actions, other, BaseTradePacket.CreateUpdateLock(session.Id, locked: true));

        if (!session.BothLocked)
        {
            return;
        }

        if (!ValidateOffer(session.Inviter, session.InviterOffer)
            || !ValidateOffer(session.Invitee, session.InviteeOffer))
        {
            AbortForInvalidOfferLocked(session, actions);
            return;
        }

        session.State = TradeSessionState.Confirming;
        session.ConfirmSentTimestamp = _timeProvider.GetTimestamp();
        QueueSend(actions, session.Inviter, BaseTradePacket.CreateConfirm(session.Id));
        QueueSend(actions, session.Invitee, BaseTradePacket.CreateConfirm(session.Id));
    }

    private void HandleAcceptLocked(
        TradeSession session,
        Player player,
        int revision,
        List<Action> actions)
    {
        if (session.State != TradeSessionState.Confirming
            || revision != session.Revision
            || !session.BothLocked
            || session.ConfirmSentTimestamp is not long confirmSentTimestamp
            || _timeProvider.GetElapsedTime(confirmSentTimestamp) < _options.ConfirmationDelay)
        {
            return;
        }

        TradeOffer offer = session.OfferFor(player);
        if (offer.Accepted)
        {
            return;
        }

        if (!ValidateOffer(session.Inviter, session.InviterOffer)
            || !ValidateOffer(session.Invitee, session.InviteeOffer))
        {
            AbortForInvalidOfferLocked(session, actions);
            return;
        }

        Touch(session);
        offer.Accepted = true;
        QueueSend(actions, session.Other(player), BaseTradePacket.CreateUpdateAccept(session.Id));

        if (session.BothAccepted)
        {
            PrepareCommitLocked(session, actions);
        }
    }

    private void CancelLocked(TradeSession session, Player player, List<Action> actions)
    {
        if (session.State == TradeSessionState.Closed)
        {
            return;
        }

        if (session.State == TradeSessionState.Pending)
        {
            if (session.IsInvitee(player))
            {
                DeclineLocked(session, actions);
            }
            else
            {
                ClosePendingLocked(session, actions, inviterNotice: null);
            }

            return;
        }

        RemoveSessionLocked(session);
        QueueSend(actions, session.Other(player), BaseTradePacket.CreateMessage(TradeMessageCode.TradeCanceled));
        QueueSend(actions, session.Inviter, BaseTradePacket.CreateEndSession(session.Id));
        QueueSend(actions, session.Invitee, BaseTradePacket.CreateEndSession(session.Id));
    }

    private void PrepareCommitLocked(TradeSession session, List<Action> actions)
    {
        if (!IsEligiblePair(session.Inviter, session.Invitee)
            || !ValidateOffer(session.Inviter, session.InviterOffer)
            || !ValidateOffer(session.Invitee, session.InviteeOffer))
        {
            AbortForInvalidOfferLocked(session, actions);
            return;
        }

        session.State = TradeSessionState.Committing;
        DetachSessionMappingsLocked(session);
        _committingByPlayer.Add(session.Inviter.Guid, session);
        _committingByPlayer.Add(session.Invitee.Guid, session);

        var request = new TradeCommitRequest(
            new TradeCommitParty(
                session.Inviter,
                new Dictionary<int, int>(session.InviterOffer.Items),
                session.InviterOffer.Coins),
            new TradeCommitParty(
                session.Invitee,
                new Dictionary<int, int>(session.InviteeOffer.Items),
                session.InviteeOffer.Coins));

        actions.Add(() => _ = Task.Run(() => CommitDetachedAsync(session, request)));
    }

    private async Task CommitDetachedAsync(TradeSession session, TradeCommitRequest request)
    {
        TradeCommitResult result;
        try
        {
            result = await _committer.CommitAsync(request, session.CommitCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (session.CommitCancellation.IsCancellationRequested)
        {
            result = TradeCommitResult.Fail(
                TradeCommitFailureKind.CommitOutcomeUncertain,
                "A trade participant disconnected while the transfer was being committed.",
                durable: true);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "The trade committer threw before reporting a trustworthy outcome. ( Session: {SessionId} )",
                session.Id);
            result = TradeCommitResult.Fail(
                TradeCommitFailureKind.CommitOutcomeUncertain,
                "The trade committer failed without a trustworthy outcome.",
                durable: true);
        }

        try
        {
            if (result.Succeeded && result.Durable)
            {
                Send(session.Inviter, BaseTradePacket.CreateMessage(TradeMessageCode.TradeCompleted));
                Send(session.Invitee, BaseTradePacket.CreateMessage(TradeMessageCode.TradeCompleted));
                Send(session.Inviter, BaseTradePacket.CreateEndSession(session.Id));
                Send(session.Invitee, BaseTradePacket.CreateEndSession(session.Id));
                _logger.LogInformation(
                    "Atomically completed a player inventory trade. ( Session: {SessionId}, Inviter: {InviterGuid}, Invitee: {InviteeGuid} )",
                    session.Id,
                    session.Inviter.Guid,
                    session.Invitee.Guid);
                return;
            }

            if (result.Succeeded)
            {
                result = TradeCommitResult.Fail(
                    TradeCommitFailureKind.Persistence,
                    "The trade committer did not report a durable transfer.");
            }

            if (result.Durable)
            {
                _logger.LogCritical(
                    "A player trade reached a durable or uncertain failure and requires clean client reloads. No terminal packet will be sent. ( Session: {SessionId}, Failure: {FailureKind}, Detail: {Detail} )",
                    session.Id,
                    result.FailureKind,
                    result.Message);
                DisconnectForUncertainOutcome(session.Inviter);
                DisconnectForUncertainOutcome(session.Invitee);
                return;
            }

            if (SendCommitInterruption(session))
                return;

            if (result.FailureKind == TradeCommitFailureKind.ParticipantChanged
                && result.ResponsiblePlayerGuid is ulong disconnectedGuid)
            {
                Player disconnected = disconnectedGuid == session.Inviter.Guid
                    ? session.Inviter
                    : session.Invitee;
                Player partner = session.Other(disconnected);
                Send(partner, BaseTradePacket.CreateMessage(TradeMessageCode.PartnerDisconnected));
                Send(partner, BaseTradePacket.CreateEndSession(session.Id));
                return;
            }

            SendCommitFailure(session, result);
            Send(session.Inviter, BaseTradePacket.CreateEndSession(session.Id));
            Send(session.Invitee, BaseTradePacket.CreateEndSession(session.Id));
            _logger.LogWarning(
                "Player trade failed closed before durability. ( Session: {SessionId}, Failure: {FailureKind}, Detail: {Detail} )",
                session.Id,
                result.FailureKind,
                result.Message);
        }
        finally
        {
            ClearCommittingPlayers(session);
            session.CommitCancellation.Dispose();
        }
    }

    private void SendCommitFailure(TradeSession session, TradeCommitResult result)
    {
        if (result.FailureKind == TradeCommitFailureKind.StackOverflow
            && result.ResponsiblePlayerGuid is ulong overflowRecipient)
        {
            bool inviterIsRecipient = overflowRecipient == session.Inviter.Guid;
            Send(
                session.Inviter,
                BaseTradePacket.CreateMessage(
                    inviterIsRecipient
                        ? TradeMessageCode.PartnersOfferExceedsYourMaxStack
                        : TradeMessageCode.YourOfferExceedsPartnersMaxStack));
            Send(
                session.Invitee,
                BaseTradePacket.CreateMessage(
                    inviterIsRecipient
                        ? TradeMessageCode.YourOfferExceedsPartnersMaxStack
                        : TradeMessageCode.PartnersOfferExceedsYourMaxStack));
            return;
        }

        if (result.ResponsiblePlayerGuid is ulong responsiblePlayer)
        {
            bool inviterResponsible = responsiblePlayer == session.Inviter.Guid;
            Send(
                session.Inviter,
                BaseTradePacket.CreateMessage(
                    inviterResponsible ? TradeMessageCode.OwnItemInvalid : TradeMessageCode.PartnerItemInvalid));
            Send(
                session.Invitee,
                BaseTradePacket.CreateMessage(
                    inviterResponsible ? TradeMessageCode.PartnerItemInvalid : TradeMessageCode.OwnItemInvalid));
            return;
        }

        Send(session.Inviter, BaseTradePacket.CreateMessage(TradeMessageCode.TradeCanceled));
        Send(session.Invitee, BaseTradePacket.CreateMessage(TradeMessageCode.TradeCanceled));
    }

    private bool SendCommitInterruption(TradeSession session)
    {
        TradeInterruption? interruption;
        lock (_sync)
            interruption = session.Interruption;

        if (interruption is null)
            return false;

        Player partner = session.Other(interruption.Player);
        if (interruption.Kind == TradeInterruptionKind.Disconnected)
        {
            Send(partner, BaseTradePacket.CreateMessage(TradeMessageCode.PartnerDisconnected));
            Send(partner, BaseTradePacket.CreateEndSession(session.Id));
            return true;
        }

        Send(interruption.Player, BaseTradePacket.CreateMessage(TradeMessageCode.SelfChangedInstance));
        Send(partner, BaseTradePacket.CreateMessage(TradeMessageCode.PartnerChangedInstance));
        Send(session.Inviter, BaseTradePacket.CreateEndSession(session.Id));
        Send(session.Invitee, BaseTradePacket.CreateEndSession(session.Id));
        return true;
    }

    private bool ValidateOffer(Player player, TradeOffer offer)
    {
        if (offer.Coins < 0
            || offer.Coins > player.Coins
            || offer.Items.Count > _options.MaximumDistinctItemsPerPlayer)
        {
            return false;
        }

        foreach ((int itemGuid, int count) in offer.Items)
        {
            if (count <= 0
                || !TryGetTradeableItem(player, itemGuid, out _, out int availableCount)
                || count > availableCount)
            {
                return false;
            }
        }

        return true;
    }

    private bool TryGetTradeableItem(
        Player player,
        int itemGuid,
        out ClientItem? item,
        out int availableCount)
    {
        item = player.Items.FirstOrDefault(candidate => candidate.Id == itemGuid);
        availableCount = 0;

        if (item is null
            || item.Count <= 0
            || !_resourceManager.ClientItemDefinitions.TryGetValue(item.Definition, out ClientItemDefinition? definition)
            || definition.NoTrade
            || !IsTradeableItemType(definition.Type))
        {
            return false;
        }

        bool equipped = player.Profiles.Any(
            profile => profile.Items.Values.Any(profileItem => profileItem.Id == itemGuid));
        int reservedCount = Math.Max(Math.Clamp(item.ConsumedCount, 0, item.Count), equipped ? 1 : 0);
        availableCount = Math.Max(0, item.Count - reservedCount);
        return availableCount > 0;
    }

    private bool EnsureSessionContextLocked(TradeSession session, List<Action> actions)
    {
        if (IsEligiblePair(session.Inviter, session.Invitee))
        {
            return true;
        }

        AbortForContextLocked(session, actions);
        return false;
    }

    private static bool IsEligiblePair(Player requester, Player target)
    {
        if (ReferenceEquals(requester, target)
            || requester.Guid == target.Guid
            || !ReferenceEquals(requester.Zone, target.Zone)
            || requester.Ignores.Any(entry => entry.Guid == target.Guid)
            || target.Ignores.Any(entry => entry.Guid == requester.Guid))
        {
            return false;
        }

        return requester.VisiblePlayers.TryGetValue(target.Guid, out Player? requesterView)
            && ReferenceEquals(requesterView, target)
            && target.VisiblePlayers.TryGetValue(requester.Guid, out Player? targetView)
            && ReferenceEquals(targetView, requester);
    }

    /// <summary>
    /// Trading cards stay with their owner, and so do pets and mounts (as upstream OSFR's trade rules, PR #179).
    /// Evergrove keeps pets and mounts as their own rows, not inventory items, so this only stops a stray one.
    /// </summary>
    internal static bool IsTradeableItemType(int itemType) =>
        itemType is not (TradingCardItemType or ItemDefinitionHelper.PetItemType or ItemDefinitionHelper.MountItemType);

    private void AbortForContextLocked(TradeSession session, List<Action> actions)
    {
        if (session.State == TradeSessionState.Pending)
        {
            ClosePendingLocked(
                session,
                actions,
                inviterNotice: $"Your trade request to {session.Invitee.Name.FullName} was canceled.");
            return;
        }

        RemoveSessionLocked(session);
        QueueSend(actions, session.Inviter, BaseTradePacket.CreateMessage(TradeMessageCode.TradeCanceled));
        QueueSend(actions, session.Invitee, BaseTradePacket.CreateMessage(TradeMessageCode.TradeCanceled));
        QueueSend(actions, session.Inviter, BaseTradePacket.CreateEndSession(session.Id));
        QueueSend(actions, session.Invitee, BaseTradePacket.CreateEndSession(session.Id));
        _logger.LogInformation(
            "Closed a player trade after its world eligibility changed. ( Session: {SessionId} )",
            session.Id);
    }

    private void AbortForInvalidOfferLocked(TradeSession session, List<Action> actions)
    {
        bool inviterValid = ValidateOffer(session.Inviter, session.InviterOffer);
        bool inviteeValid = ValidateOffer(session.Invitee, session.InviteeOffer);
        RemoveSessionLocked(session);

        QueueSend(
            actions,
            session.Inviter,
            BaseTradePacket.CreateMessage(
                inviterValid ? TradeMessageCode.PartnerItemInvalid : TradeMessageCode.OwnItemInvalid));
        QueueSend(
            actions,
            session.Invitee,
            BaseTradePacket.CreateMessage(
                inviteeValid ? TradeMessageCode.PartnerItemInvalid : TradeMessageCode.OwnItemInvalid));
        QueueSend(actions, session.Inviter, BaseTradePacket.CreateEndSession(session.Id));
        QueueSend(actions, session.Invitee, BaseTradePacket.CreateEndSession(session.Id));
    }

    internal void CleanupExpiredAndInvalidSessions()
    {
        try
        {
            RunTransition(actions =>
            {
                foreach (TradeSession session in _sessions.Values.ToArray())
                {
                    if (!IsEligiblePair(session.Inviter, session.Invitee))
                    {
                        AbortForContextLocked(session, actions);
                        continue;
                    }

                    if (session.State == TradeSessionState.Pending)
                    {
                        if (_timeProvider.GetElapsedTime(session.CreatedTimestamp) >= _options.InviteTimeout)
                        {
                            ClosePendingLocked(
                                session,
                                actions,
                                inviterNotice: $"Your trade request to {session.Invitee.Name.FullName} timed out.");
                            _logger.LogInformation(
                                "Expired a player trade invitation. ( Session: {SessionId} )",
                                session.Id);
                        }

                        continue;
                    }

                    if (_timeProvider.GetElapsedTime(session.LastActivityTimestamp) < _options.SessionTimeout)
                    {
                        continue;
                    }

                    RemoveSessionLocked(session);
                    QueueSend(actions, session.Inviter, BaseTradePacket.CreateMessage(TradeMessageCode.TradeCanceled));
                    QueueSend(actions, session.Invitee, BaseTradePacket.CreateMessage(TradeMessageCode.TradeCanceled));
                    QueueSend(actions, session.Inviter, BaseTradePacket.CreateEndSession(session.Id));
                    QueueSend(actions, session.Invitee, BaseTradePacket.CreateEndSession(session.Id));

                    _logger.LogInformation(
                        "Expired a player trade session. ( Session: {SessionId} )",
                        session.Id);
                }

                PruneRequestLimitsLocked();
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Player trade cleanup failed without terminating the game service.");
        }
    }

    private void RunTransition(Action<List<Action>> transition)
    {
        var actions = new List<Action>();
        bool scheduleDrain;

        lock (_sync)
        {
            transition(actions);
            scheduleDrain = PublishExternalBatchLocked(actions);
        }

        if (scheduleDrain && RunExternalActionsInline)
        {
            DrainExternalActions();
        }
        else if (scheduleDrain)
        {
            ThreadPool.UnsafeQueueUserWorkItem(
                static manager => manager.DrainExternalActions(),
                this,
                preferLocal: false);
        }
    }

    private bool PublishExternalBatchLocked(List<Action> actions)
    {
        if (actions.Count == 0)
        {
            return false;
        }

        Action[] batch = actions.ToArray();
        _externalActions.Enqueue(() =>
        {
            foreach (Action action in batch)
            {
                RunExternalAction(action);
            }
        });

        return Interlocked.CompareExchange(ref _externalDrainScheduled, 1, 0) == 0;
    }

    private void DrainExternalActions()
    {
        while (true)
        {
            while (_externalActions.TryDequeue(out Action? action))
            {
                RunExternalAction(action);
            }

            Volatile.Write(ref _externalDrainScheduled, 0);
            if (_externalActions.IsEmpty)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _externalDrainScheduled, 1, 0) != 0)
            {
                return;
            }
        }
    }

    private void RunExternalAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            _logger.LogCritical(
                exception,
                "An isolated player-trade external action failed without terminating the game service.");
        }
    }

    private void QueueSend(List<Action> actions, Player player, byte[] payload) =>
        actions.Add(() => Send(player, new RawTradePacket(payload)));

    private void QueueSystemMessage(List<Action> actions, Player player, string message) =>
        actions.Add(() => Send(player, ChatHelper.CreateSystemMessage(message)));

    private void Send(Player player, byte[] payload) => Send(player, new RawTradePacket(payload));

    private void Send(Player player, ISerializablePacket packet)
    {
        try
        {
            if (PacketSink is { } sink)
            {
                sink(player, packet);
            }
            else
            {
                player.SendTunneled(packet);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to send a player trade packet. ( Player: {PlayerGuid} )",
                player.Guid);
        }
    }

    private void DisconnectForUncertainOutcome(Player player)
    {
        try
        {
            player.Disconnect();
        }
        catch (Exception exception)
        {
            _logger.LogCritical(
                exception,
                "Failed to disconnect a trade participant after an uncertain commit outcome. ( Player: {PlayerGuid} )",
                player.Guid);
        }
    }

    private static void CancelCommit(TradeSession session)
    {
        try
        {
            session.CommitCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The commit already reached a terminal outcome.
        }
    }

    private bool IsBusyLocked(ulong playerGuid) =>
        _sessionsByPlayer.ContainsKey(playerGuid) || _committingByPlayer.ContainsKey(playerGuid);

    /// <summary>Why a request can't be sent, in words for the requester, or null when it can.</summary>
    private string? GetRequestRefusalLocked(Player requester, Player target)
    {
        string targetName = target.Name.FullName;

        if (IsBusyLocked(requester.Guid))
        {
            return "You are already trading.";
        }

        if (IsBusyLocked(target.Guid))
        {
            return $"{targetName} is busy trading right now.";
        }

        if (_pendingByInviter.TryGetValue(requester.Guid, out TradeSession? outgoing)
            && outgoing.Invitee.Guid == target.Guid)
        {
            return $"You already sent {targetName} a trade request.";
        }

        if (_pendingByInviter.TryGetValue(target.Guid, out TradeSession? incoming)
            && incoming.Invitee.Guid == requester.Guid)
        {
            return $"{targetName} already sent you a trade request.";
        }

        if (_lastRequestTimestamps.TryGetValue(requester.Guid, out long lastRequest)
            && _timeProvider.GetElapsedTime(lastRequest) < _options.RequestInterval)
        {
            return "Please wait a moment before sending another trade request.";
        }

        if (_declinedTimestamps.TryGetValue((requester.Guid, target.Guid), out long declined)
            && _timeProvider.GetElapsedTime(declined) < _options.DeclinedRequestCooldown)
        {
            return $"{targetName} turned down your last trade request. Please wait a little before asking again.";
        }

        int waiting = _sessions.Values.Count(
            session => session.State == TradeSessionState.Pending && session.Invitee.Guid == target.Guid);
        if (waiting >= _options.MaximumPendingInvitesPerTarget)
        {
            return $"{targetName} has too many trade requests waiting. Please try again soon.";
        }

        return null;
    }

    private TradeSession[] PendingSessionsOfLocked(Player player) =>
        _sessions.Values
            .Where(session => session.State == TradeSessionState.Pending && session.Contains(player))
            .ToArray();

    private void DeclineLocked(TradeSession session, List<Action> actions)
    {
        RemoveSessionLocked(session);
        _declinedTimestamps[(session.Inviter.Guid, session.Invitee.Guid)] = _timeProvider.GetTimestamp();
        QueueSend(actions, session.Inviter, BaseTradePacket.CreateRejected(session.Invitee.Name));
        _logger.LogInformation(
            "Player trade invitation declined. ( Session: {SessionId}, Invitee: {InviteeGuid} )",
            session.Id,
            session.Invitee.Guid);
    }

    /// <summary>
    /// Closes an unanswered request. The invitee is sent native message 8 ("Your trade with [player] has been
    /// canceled."), which also removes the request from the client's action queue (upstream OSFR PR #194), and the
    /// inviter is told why when there is something to tell.
    /// </summary>
    private void ClosePendingLocked(
        TradeSession session,
        List<Action> actions,
        string? inviterNotice,
        byte inviteeMessage = TradeMessageCode.PartnerChangedInstance,
        bool notifyInvitee = true)
    {
        if (session.State != TradeSessionState.Pending)
        {
            return;
        }

        RemoveSessionLocked(session);

        if (notifyInvitee)
        {
            QueueSend(actions, session.Invitee, BaseTradePacket.CreateMessage(inviteeMessage));
            QueueSend(actions, session.Invitee, BaseTradePacket.CreateEndSession(session.Id));
        }

        if (inviterNotice is not null)
        {
            QueueSystemMessage(actions, session.Inviter, inviterNotice);
        }
    }

    private void DetachPendingLocked(TradeSession session)
    {
        if (_pendingByInviter.TryGetValue(session.Inviter.Guid, out TradeSession? pending)
            && ReferenceEquals(pending, session))
        {
            _pendingByInviter.Remove(session.Inviter.Guid);
        }
    }

    private void PruneRequestLimitsLocked()
    {
        foreach ((ulong guid, long timestamp) in _lastRequestTimestamps.ToArray())
        {
            if (_timeProvider.GetElapsedTime(timestamp) >= _options.RequestInterval)
            {
                _lastRequestTimestamps.Remove(guid);
            }
        }

        foreach (((ulong, ulong) pair, long timestamp) in _declinedTimestamps.ToArray())
        {
            if (_timeProvider.GetElapsedTime(timestamp) >= _options.DeclinedRequestCooldown)
            {
                _declinedTimestamps.Remove(pair);
            }
        }
    }

    /// <summary>Tests: the session id of the request a player has out, if any.</summary>
    internal int? PendingSessionIdOf(Player inviter)
    {
        lock (_sync)
        {
            return _pendingByInviter.TryGetValue(inviter.Guid, out TradeSession? session) ? session.Id : null;
        }
    }

    private int AllocateSessionIdLocked()
    {
        do
        {
            _nextSessionId = _nextSessionId == int.MaxValue ? 1 : _nextSessionId + 1;
        }
        while (_sessions.ContainsKey(_nextSessionId));

        return _nextSessionId;
    }

    private void RemoveSessionLocked(TradeSession session)
    {
        if (session.State == TradeSessionState.Closed)
        {
            return;
        }

        DetachSessionMappingsLocked(session);
        session.State = TradeSessionState.Closed;
        session.CommitCancellation.Dispose();
    }

    private void DetachSessionMappingsLocked(TradeSession session)
    {
        _sessions.Remove(session.Id);
        DetachPendingLocked(session);

        if (_sessionsByPlayer.TryGetValue(session.Inviter.Guid, out TradeSession? inviterSession)
            && ReferenceEquals(inviterSession, session))
        {
            _sessionsByPlayer.Remove(session.Inviter.Guid);
        }

        if (_sessionsByPlayer.TryGetValue(session.Invitee.Guid, out TradeSession? inviteeSession)
            && ReferenceEquals(inviteeSession, session))
        {
            _sessionsByPlayer.Remove(session.Invitee.Guid);
        }
    }

    private void ClearCommittingPlayers(TradeSession session)
    {
        lock (_sync)
        {
            if (_committingByPlayer.TryGetValue(session.Inviter.Guid, out TradeSession? inviterSession)
                && ReferenceEquals(inviterSession, session))
            {
                _committingByPlayer.Remove(session.Inviter.Guid);
            }

            if (_committingByPlayer.TryGetValue(session.Invitee.Guid, out TradeSession? inviteeSession)
                && ReferenceEquals(inviteeSession, session))
            {
                _committingByPlayer.Remove(session.Invitee.Guid);
            }

            session.State = TradeSessionState.Closed;
        }
    }

    private void Touch(TradeSession session) =>
        session.LastActivityTimestamp = _timeProvider.GetTimestamp();

    private static void ResetConfirmationAfterOfferChange(TradeSession session)
    {
        session.State = TradeSessionState.Active;
        session.ConfirmSentTimestamp = null;
        session.InviterOffer.Locked = false;
        session.InviteeOffer.Locked = false;
        session.InviterOffer.Accepted = false;
        session.InviteeOffer.Accepted = false;
    }

    private static int AdvanceRevision(TradeSession session)
    {
        session.Revision = session.Revision == int.MaxValue ? 1 : session.Revision + 1;
        return session.Revision;
    }

    private static ItemInstance CreateOfferedItem(ClientItem source, int count) =>
        new()
        {
            Definition = source.Definition,
            Tint = source.Tint,
            Id = source.Id,
            Count = count,
            ConsumedCount = 0,
            LastCastTime = source.LastCastTime,
            AbilityCount = source.AbilityCount
        };

    private static void ValidateOptions(TradeOptions options)
    {
        if (options.InviteTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Trade invite timeout must be positive.");
        }

        if (options.SessionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Trade session timeout must be positive.");
        }

        if (options.ConfirmationDelay < TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Trade confirmation delay cannot be shorter than retail's ten seconds.");
        }

        if (options.MaximumDistinctItemsPerPlayer is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "A trade offer must allow between one and fifty distinct items.");
        }

        if (options.RequestInterval < TimeSpan.Zero || options.DeclinedRequestCooldown < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Trade request limits cannot be negative.");
        }

        if (options.MaximumPendingInvitesPerTarget is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "A player must be able to hold between one and ten trade requests.");
        }
    }

    private sealed class TradeSession(
        int id,
        Player inviter,
        Player invitee,
        long createdTimestamp)
    {
        internal int Id { get; } = id;
        internal Player Inviter { get; } = inviter;
        internal Player Invitee { get; } = invitee;
        internal long CreatedTimestamp { get; } = createdTimestamp;
        internal long LastActivityTimestamp { get; set; } = createdTimestamp;
        internal long? ConfirmSentTimestamp { get; set; }
        internal TradeSessionState State { get; set; } = TradeSessionState.Pending;
        internal int Revision { get; set; }
        internal TradeOffer InviterOffer { get; } = new();
        internal TradeOffer InviteeOffer { get; } = new();
        internal CancellationTokenSource CommitCancellation { get; } = new();
        internal TradeInterruption? Interruption { get; set; }
        internal bool BothLocked => InviterOffer.Locked && InviteeOffer.Locked;
        internal bool BothAccepted => InviterOffer.Accepted && InviteeOffer.Accepted;

        internal bool Contains(Player player) =>
            ReferenceEquals(player, Inviter) || ReferenceEquals(player, Invitee);

        internal bool IsInvitee(Player player) => ReferenceEquals(player, Invitee);

        internal Player Other(Player player) => ReferenceEquals(player, Inviter)
            ? Invitee
            : ReferenceEquals(player, Invitee)
                ? Inviter
                : throw new InvalidOperationException("Player is not a member of this trade session.");

        internal TradeOffer OfferFor(Player player) => ReferenceEquals(player, Inviter)
            ? InviterOffer
            : ReferenceEquals(player, Invitee)
                ? InviteeOffer
                : throw new InvalidOperationException("Player is not a member of this trade session.");

        internal TradeOffer OtherOffer(Player player) => ReferenceEquals(player, Inviter)
            ? InviteeOffer
            : ReferenceEquals(player, Invitee)
                ? InviterOffer
                : throw new InvalidOperationException("Player is not a member of this trade session.");
    }

    private sealed class TradeOffer
    {
        internal Dictionary<int, int> Items { get; } = [];
        internal int Coins { get; set; }
        internal bool Locked { get; set; }
        internal bool Accepted { get; set; }
    }

    private sealed record TradeInterruption(TradeInterruptionKind Kind, Player Player);

    private enum TradeInterruptionKind
    {
        Disconnected,
        Zoning
    }

    private enum TradeSessionState
    {
        Pending,
        Active,
        Confirming,
        Committing,
        Closed
    }

    private static class TradeMessageCode
    {
        internal const byte TradeCanceled = 1;
        internal const byte PartnerDisconnected = 2;
        internal const byte PartnerItemInvalid = 3;
        internal const byte OwnItemInvalid = 4;
        internal const byte TradeCompleted = 6;
        internal const byte SelfChangedInstance = 7;
        internal const byte PartnerChangedInstance = 8;
        internal const byte YourOfferExceedsPartnersMaxStack = 9;
        internal const byte PartnersOfferExceedsYourMaxStack = 10;
    }

    private sealed class RawTradePacket(byte[] payload) : ISerializablePacket
    {
        public byte[] Serialize() => payload;
    }
}
