using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Trading;
using Sanctuary.Game.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Trade requests as <see cref="TradeManager"/> handles them: three players who can all see each other in one zone,
/// a clock the test moves, and every packet the manager sends recorded instead of going to a client. Nothing here
/// reaches an item or the database: these cover asking, answering and what each player is told.
/// </summary>
[TestClass]
public sealed class TradeManagerTests
{
    private const byte TradeCanceledMessage = 1;
    private const byte PartnerDisconnectedMessage = 2;
    private const byte ClearsRequestMessage = 8;

    private readonly ManualClock _clock = new();
    private readonly List<(Player Player, ISerializablePacket Packet)> _sent = [];
    private readonly IZone _zone = DispatchProxy.Create<IZone, QuestWorld.EmptyZone>();
    private TradeManager _trades = null!;
    private Player _alice = null!;
    private Player _bob = null!;
    private Player _cara = null!;

    [TestInitialize]
    public void Setup()
    {
        _trades = new TradeManager(
            NullLogger<TradeManager>.Instance,
            DispatchProxy.Create<IResourceManager, Unused>(),
            DispatchProxy.Create<ITradeCommitter, Unused>(),
            timeProvider: _clock)
        {
            PacketSink = (player, packet) => _sent.Add((player, packet)),
            RunExternalActionsInline = true
        };

        _alice = NewPlayer(1, "Alice");
        _bob = NewPlayer(2, "Bob");
        _cara = NewPlayer(3, "Cara");
    }

    [TestCleanup]
    public void Cleanup() => _trades.Dispose();

    [TestMethod]
    public void PendingRequest_DoesNotMakeEitherPlayerBusy()
    {
        _trades.RequestTrade(_alice, _bob);

        Assert.IsNotNull(_trades.PendingSessionIdOf(_alice));
        Assert.IsTrue(_trades.CanTrade(_cara, _bob), "Bob can still be asked by someone else.");
        Assert.IsTrue(_trades.CanTrade(_bob, _cara), "Bob can still ask someone else.");
        Assert.IsTrue(_trades.CanTrade(_cara, _alice), "Alice can still be asked while her request waits.");

        _sent.Clear();
        _trades.RequestTrade(_cara, _bob);

        Assert.IsNotNull(_trades.PendingSessionIdOf(_cara), "Cara's request reaches Bob as well.");
        Assert.AreEqual(1, Invites(_bob).Count);
    }

    [TestMethod]
    public void AcceptingOneRequest_WithdrawsEveryOtherRequestOfBothPlayers()
    {
        _trades.RequestTrade(_alice, _bob);
        int aliceToBob = _trades.PendingSessionIdOf(_alice)!.Value;
        _trades.RequestTrade(_cara, _bob);
        int caraToBob = _trades.PendingSessionIdOf(_cara)!.Value;
        _sent.Clear();

        _trades.HandleClientCommand(_bob, new TradeInviteReplyCommand(aliceToBob, Accepted: true));

        Assert.IsNull(_trades.PendingSessionIdOf(_cara), "Cara's request to Bob is withdrawn.");
        Assert.IsTrue(Received(_bob, BaseTradePacket.CreateMessage(ClearsRequestMessage)), "Bob's screen drops Cara's request.");
        Assert.IsTrue(Received(_bob, BaseTradePacket.CreateEndSession(caraToBob)));
        CollectionAssert.Contains(Messages(_cara), "Bob started another trade.");
        Assert.IsFalse(_trades.CanTrade(_cara, _bob), "Bob is busy once the window is open.");
        Assert.IsFalse(_trades.CanTrade(_cara, _alice));

        // A late answer to the withdrawn request does nothing.
        _sent.Clear();
        _trades.HandleClientCommand(_bob, new TradeInviteReplyCommand(caraToBob, Accepted: true));
        Assert.AreEqual(0, _sent.Count);
    }

    [TestMethod]
    public void RequestingAgain_IsRefusedWithAReason()
    {
        _trades.RequestTrade(_alice, _bob);
        int first = _trades.PendingSessionIdOf(_alice)!.Value;
        _sent.Clear();

        _trades.RequestTrade(_alice, _bob);

        Assert.AreEqual(first, _trades.PendingSessionIdOf(_alice));
        Assert.AreEqual(0, Invites(_bob).Count, "Bob gets no second popup.");
        CollectionAssert.Contains(Messages(_alice), "You already sent Bob a trade request.");

        _trades.RequestTrade(_bob, _alice);
        CollectionAssert.Contains(Messages(_bob), "Alice already sent you a trade request.");
    }

    [TestMethod]
    public void ANewRequest_ReplacesTheOldOne_AfterTheRequestInterval()
    {
        _trades.RequestTrade(_alice, _bob);
        int toBob = _trades.PendingSessionIdOf(_alice)!.Value;
        _sent.Clear();

        _trades.RequestTrade(_alice, _cara);
        Assert.AreEqual(toBob, _trades.PendingSessionIdOf(_alice), "Too soon: the first request stands.");
        CollectionAssert.Contains(Messages(_alice), "Please wait a moment before sending another trade request.");

        _clock.Advance(TimeSpan.FromSeconds(3));
        _sent.Clear();
        _trades.RequestTrade(_alice, _cara);

        Assert.AreNotEqual(toBob, _trades.PendingSessionIdOf(_alice));
        Assert.AreEqual(1, Invites(_cara).Count);
        Assert.IsTrue(Received(_bob, BaseTradePacket.CreateMessage(ClearsRequestMessage)), "Bob's popup is cleared.");
        Assert.IsTrue(Received(_bob, BaseTradePacket.CreateEndSession(toBob)));
    }

    [TestMethod]
    public void Declining_StopsTheSameRequesterForAWhile()
    {
        _trades.RequestTrade(_alice, _bob);
        _trades.HandleClientCommand(_bob, new TradeInviteReplyCommand(_trades.PendingSessionIdOf(_alice)!.Value, Accepted: false));
        Assert.IsTrue(Received(_alice, BaseTradePacket.CreateRejected(_bob.Name)));

        _clock.Advance(TimeSpan.FromSeconds(10));
        _sent.Clear();
        _trades.RequestTrade(_alice, _bob);

        Assert.IsNull(_trades.PendingSessionIdOf(_alice));
        Assert.AreEqual(0, Invites(_bob).Count);
        StringAssert.StartsWith(Messages(_alice).Single(), "Bob turned down your last trade request.");

        // Bob can still ask Alice, and Alice can ask anyone else.
        _trades.RequestTrade(_bob, _alice);
        Assert.IsNotNull(_trades.PendingSessionIdOf(_bob));
        _trades.RequestTrade(_alice, _cara);
        Assert.IsNotNull(_trades.PendingSessionIdOf(_alice));

        // Thirty seconds after Bob said no, Alice may ask him again.
        _trades.HandleClientCommand(_bob, new TradeCancelCommand(_trades.PendingSessionIdOf(_bob)!.Value));
        _clock.Advance(TimeSpan.FromSeconds(21));
        _trades.CleanupExpiredAndInvalidSessions();
        _sent.Clear();
        _trades.RequestTrade(_alice, _bob);
        Assert.AreEqual(1, Invites(_bob).Count, "The cooldown ends.");
    }

    [TestMethod]
    public void ARequestTarget_HoldsOnlySoManyRequests()
    {
        var options = new TradeOptions();
        var players = Enumerable.Range(10, options.MaximumPendingInvitesPerTarget + 1)
            .Select(id => NewPlayer((ulong)id, $"Player{id}"))
            .ToArray();

        foreach (Player player in players)
        {
            _trades.RequestTrade(player, _bob);
        }

        Assert.AreEqual(options.MaximumPendingInvitesPerTarget, Invites(_bob).Count);
        Assert.IsNull(_trades.PendingSessionIdOf(players[^1]));
        StringAssert.StartsWith(Messages(players[^1]).Single(), "Bob has too many trade requests waiting.");
    }

    [TestMethod]
    public void AnUnansweredRequest_TimesOutAfterTheClientDropsIt()
    {
        _trades.RequestTrade(_alice, _bob);
        int session = _trades.PendingSessionIdOf(_alice)!.Value;

        _clock.Advance(TimeSpan.FromSeconds(300));
        _trades.CleanupExpiredAndInvalidSessions();
        Assert.AreEqual(session, _trades.PendingSessionIdOf(_alice), "Still open while the client shows it.");

        _sent.Clear();
        _clock.Advance(TimeSpan.FromSeconds(10));
        _trades.CleanupExpiredAndInvalidSessions();

        Assert.IsNull(_trades.PendingSessionIdOf(_alice));
        Assert.IsTrue(Received(_bob, BaseTradePacket.CreateMessage(ClearsRequestMessage)));
        Assert.IsTrue(Received(_bob, BaseTradePacket.CreateEndSession(session)));
        CollectionAssert.Contains(Messages(_alice), "Your trade request to Bob timed out.");
    }

    [TestMethod]
    public void ZoningOrLeaving_ClearsRequests()
    {
        _trades.RequestTrade(_alice, _bob);
        int aliceToBob = _trades.PendingSessionIdOf(_alice)!.Value;
        _trades.RequestTrade(_bob, _cara);
        int bobToCara = _trades.PendingSessionIdOf(_bob)!.Value;
        _sent.Clear();

        _trades.OnZoning(_bob);

        Assert.IsNull(_trades.PendingSessionIdOf(_alice));
        Assert.IsNull(_trades.PendingSessionIdOf(_bob));
        CollectionAssert.Contains(Messages(_alice), "Your trade request to Bob was canceled.");
        Assert.IsTrue(Received(_cara, BaseTradePacket.CreateMessage(ClearsRequestMessage)), "Cara's popup from Bob is cleared.");
        Assert.IsTrue(Received(_cara, BaseTradePacket.CreateEndSession(bobToCara)));
        Assert.IsTrue(Received(_bob, BaseTradePacket.CreateEndSession(aliceToBob)));

        _clock.Advance(TimeSpan.FromSeconds(3));
        _trades.RequestTrade(_cara, _alice);
        int caraToAlice = _trades.PendingSessionIdOf(_cara)!.Value;
        _sent.Clear();

        _trades.OnDisconnected(_cara);

        Assert.IsNull(_trades.PendingSessionIdOf(_cara));
        Assert.IsTrue(Received(_alice, BaseTradePacket.CreateMessage(PartnerDisconnectedMessage)));
        Assert.IsTrue(Received(_alice, BaseTradePacket.CreateEndSession(caraToAlice)));
    }

    [TestMethod]
    public void InviterCancel_ClearsTheInviteesPopup()
    {
        _trades.RequestTrade(_alice, _bob);
        int session = _trades.PendingSessionIdOf(_alice)!.Value;
        _sent.Clear();

        _trades.HandleClientCommand(_alice, new TradeCancelCommand(session));

        Assert.IsNull(_trades.PendingSessionIdOf(_alice));
        Assert.IsTrue(Received(_bob, BaseTradePacket.CreateMessage(ClearsRequestMessage)));
        Assert.IsTrue(Received(_bob, BaseTradePacket.CreateEndSession(session)));
    }

    [TestMethod]
    public void BusyPlayers_AreToldWhy()
    {
        _trades.RequestTrade(_alice, _bob);
        _trades.HandleClientCommand(_bob, new TradeInviteReplyCommand(_trades.PendingSessionIdOf(_alice)!.Value, Accepted: true));
        _sent.Clear();

        _trades.RequestTrade(_cara, _bob);
        _trades.RequestTrade(_alice, _cara);

        CollectionAssert.Contains(Messages(_cara), "Bob is busy trading right now.");
        CollectionAssert.Contains(Messages(_alice), "You are already trading.");
        Assert.AreEqual(0, Invites(_bob).Count + Invites(_cara).Count);
    }

    [TestMethod]
    public void IgnoredPlayers_GetNoPopupAndNoReason()
    {
        _bob.Ignores.Add(new IgnoreData { Guid = _alice.Guid });

        _trades.RequestTrade(_alice, _bob);

        Assert.IsNull(_trades.PendingSessionIdOf(_alice));
        Assert.AreEqual(0, _sent.Count, "Nothing tells Alice she is ignored.");
    }

    [TestMethod]
    public void TradingCardsPetsAndMounts_CannotBeOffered()
    {
        Assert.IsFalse(TradeManager.IsTradeableItemType(7), "Trading cards.");
        Assert.IsFalse(TradeManager.IsTradeableItemType(ItemDefinitionHelper.PetItemType));
        Assert.IsFalse(TradeManager.IsTradeableItemType(ItemDefinitionHelper.MountItemType));
        Assert.IsTrue(TradeManager.IsTradeableItemType(1), "Clothing and other ordinary items.");
        Assert.IsTrue(TradeManager.IsTradeableItemType(29), "Housing decorations stay tradeable unless marked NoTrade.");
    }

    [TestMethod]
    public void Options_RejectAZeroRequestCap()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new TradeManager(
            NullLogger<TradeManager>.Instance,
            DispatchProxy.Create<IResourceManager, Unused>(),
            DispatchProxy.Create<ITradeCommitter, Unused>(),
            new TradeOptions { MaximumPendingInvitesPerTarget = 0 },
            _clock));
    }

    private Player NewPlayer(ulong id, string name)
    {
        var player = new Player(null!, null!, null!, null!, null!)
        {
            Guid = id,
            Zone = _zone
        };
        player.Name.FirstName = name;

        foreach (Player other in new[] { _alice, _bob, _cara }.Where(other => other is not null))
        {
            player.VisiblePlayers[other.Guid] = other;
            other.VisiblePlayers[player.Guid] = player;
        }

        return player;
    }

    private IEnumerable<byte[]> TradePayloads(Player player) =>
        _sent.Where(entry => ReferenceEquals(entry.Player, player) && entry.Packet is not ChatPacketDebugChat)
            .Select(entry => entry.Packet.Serialize());

    private bool Received(Player player, byte[] payload) =>
        TradePayloads(player).Any(sent => sent.AsSpan().SequenceEqual(payload));

    private List<byte[]> Invites(Player player)
    {
        byte[] prefix = BaseTradePacket.CreateInvite(new NameData(), 0, 0)[..3];
        return TradePayloads(player).Where(sent => sent.AsSpan().StartsWith(prefix)).ToList();
    }

    private List<string> Messages(Player player) =>
        _sent.Where(entry => ReferenceEquals(entry.Player, player))
            .Select(entry => entry.Packet)
            .OfType<ChatPacketDebugChat>()
            .Select(chat => chat.Message ?? string.Empty)
            .ToList();

    /// <summary>A clock that moves only when the test says so. Its timers never fire: tests run the cleanup.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = TimeSpan.FromDays(1).Ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public override DateTimeOffset GetUtcNow() => new(_ticks, TimeSpan.Zero);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new NeverFires();

        private sealed class NeverFires : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public System.Threading.Tasks.ValueTask DisposeAsync() => default;
        }
    }

    /// <summary>The item and database side, which no request test reaches.</summary>
    public class Unused : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }
}
