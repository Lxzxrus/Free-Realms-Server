using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Trading;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The planner decides every durable outcome of a trade commit: final stacks, new item IDs and
/// coin balances for both players. <see cref="TradeCommitter"/> only loads the snapshots and
/// writes the plan, so these tests cover the commit logic without a client or a database.
/// </summary>
[TestClass]
public sealed class TradeTransferPlannerTests
{
    private const ulong Alice = 1;
    private const ulong Bob = 2;

    private static TradeStackSnapshot Stack(
        int itemId,
        int definition,
        int count,
        int tint = 0,
        int reserved = 0,
        bool known = true,
        bool noTrade = false,
        int maxStackSize = 0) =>
        new(itemId, definition, tint, count, reserved, known, noTrade, PetProtected: false, maxStackSize);

    private static TradePartySnapshot Party(ulong guid, int coins, params TradeStackSnapshot[] items) =>
        new(guid, coins, items);

    private static Dictionary<int, int> Offer(params (int ItemId, int Count)[] items) =>
        items.ToDictionary(item => item.ItemId, item => item.Count);

    private static TradeCommitResult Plan(
        TradePartySnapshot first,
        Dictionary<int, int> firstItems,
        int firstCoins,
        TradePartySnapshot second,
        Dictionary<int, int> secondItems,
        int secondCoins,
        out TradeTransferPlan? plan) =>
        TradeTransferPlanner.TryPlan(
            new TradePlanningRequest(first, firstItems, firstCoins, second, secondItems, secondCoins),
            out plan);

    [TestMethod]
    public void Swap_MovesItemsAndCoinsBothWays()
    {
        var alice = Party(Alice, 100, Stack(1, 1000, 5));
        var bob = Party(Bob, 20, Stack(1, 2000, 2), Stack(4, 3000, 1));

        var result = Plan(alice, Offer((1, 3)), 50, bob, Offer((1, 2)), 0, out var plan);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsNotNull(plan);

        Assert.AreEqual(50, plan.First.FinalCoins);
        CollectionAssert.AreEqual(
            new[] { new TradePlannedStack(1, 1000, 0, 2), new TradePlannedStack(2, 2000, 0, 2) },
            plan.First.FinalItems.ToArray());

        // Bob's stack of 2000 left entirely, so its row is gone; the incoming stack takes the
        // next ID after his highest, not the freed one.
        Assert.AreEqual(70, plan.Second.FinalCoins);
        CollectionAssert.AreEqual(
            new[] { new TradePlannedStack(4, 3000, 0, 1), new TradePlannedStack(5, 1000, 0, 3) },
            plan.Second.FinalItems.ToArray());
    }

    [TestMethod]
    public void IncomingStack_FoldsIntoRecipientsStackOfTheSameDefinitionAndTint()
    {
        var alice = Party(Alice, 0, Stack(1, 1000, 5));
        var bob = Party(Bob, 0, Stack(7, 1000, 2), Stack(8, 1000, 1, tint: 3));

        var result = Plan(alice, Offer((1, 5)), 0, bob, Offer(), 0, out var plan);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsNotNull(plan);
        Assert.AreEqual(0, plan.First.FinalItems.Count);
        CollectionAssert.AreEqual(
            new[] { new TradePlannedStack(7, 1000, 0, 7), new TradePlannedStack(8, 1000, 3, 1) },
            plan.Second.FinalItems.ToArray());
    }

    [TestMethod]
    public void Plan_ConservesEveryItemAndCoin()
    {
        var alice = Party(Alice, 300, Stack(1, 1000, 5), Stack(2, 2000, 4, tint: 1), Stack(3, 3000, 1));
        var bob = Party(Bob, 40, Stack(1, 2000, 6, tint: 1), Stack(2, 4000, 9));

        var result = Plan(alice, Offer((1, 5), (2, 4)), 125, bob, Offer((2, 3)), 40, out var plan);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsNotNull(plan);

        Assert.AreEqual(alice.Coins + bob.Coins, plan.First.FinalCoins + plan.Second.FinalCoins);
        CollectionAssert.AreEquivalent(
            Totals(alice.Items.Select(i => (i.Definition, i.Tint, i.Count)).Concat(bob.Items.Select(i => (i.Definition, i.Tint, i.Count)))),
            Totals(plan.First.FinalItems.Concat(plan.Second.FinalItems).Select(i => (i.Definition, i.Tint, i.Count))));

        static List<string> Totals(IEnumerable<(int Definition, int Tint, int Count)> stacks) =>
            stacks.GroupBy(s => (s.Definition, s.Tint))
                .Select(g => $"{g.Key.Definition}/{g.Key.Tint}={g.Sum(s => s.Count)}")
                .ToList();
    }

    [TestMethod]
    public void EquippedOrConsumedUnits_CannotBeTraded()
    {
        var alice = Party(Alice, 0, Stack(1, 1000, 3, reserved: 1));
        var bob = Party(Bob, 0);

        var tooMany = Plan(alice, Offer((1, 3)), 0, bob, Offer(), 0, out var plan);
        Assert.IsFalse(tooMany.Succeeded);
        Assert.AreEqual(TradeCommitFailureKind.ReservedItem, tooMany.FailureKind);
        Assert.AreEqual(Alice, tooMany.ResponsiblePlayerGuid);
        Assert.IsNull(plan);

        var allowed = Plan(alice, Offer((1, 2)), 0, bob, Offer(), 0, out _);
        Assert.IsTrue(allowed.Succeeded, allowed.Message);
    }

    [TestMethod]
    public void NoTradeItem_IsRefused()
    {
        var alice = Party(Alice, 0, Stack(1, 1000, 1, noTrade: true));
        var bob = Party(Bob, 0);

        var result = Plan(alice, Offer((1, 1)), 0, bob, Offer(), 0, out _);

        Assert.AreEqual(TradeCommitFailureKind.NoTrade, result.FailureKind);
        Assert.AreEqual(1, result.ResponsibleItemGuid);
    }

    [TestMethod]
    public void UnknownDefinitionAnywhereInInventory_RefusesTheTrade()
    {
        var alice = Party(Alice, 0, Stack(1, 1000, 1), Stack(2, 999999, 1, known: false));
        var bob = Party(Bob, 0);

        var result = Plan(alice, Offer((1, 1)), 0, bob, Offer(), 0, out _);

        Assert.AreEqual(TradeCommitFailureKind.UnknownDefinition, result.FailureKind);
        Assert.AreEqual(2, result.ResponsibleItemGuid);
    }

    [TestMethod]
    public void OfferOfAnItemThePlayerNoLongerHas_IsRefused()
    {
        var alice = Party(Alice, 0, Stack(1, 1000, 2));
        var bob = Party(Bob, 0);

        Assert.AreEqual(
            TradeCommitFailureKind.InventoryMismatch,
            Plan(alice, Offer((9, 1)), 0, bob, Offer(), 0, out _).FailureKind);
        Assert.AreEqual(
            TradeCommitFailureKind.InventoryMismatch,
            Plan(alice, Offer((1, 3)), 0, bob, Offer(), 0, out _).FailureKind);
        Assert.AreEqual(
            TradeCommitFailureKind.InventoryMismatch,
            Plan(alice, Offer((1, 0)), 0, bob, Offer(), 0, out _).FailureKind);
    }

    [TestMethod]
    public void DuplicateStacksInOneInventory_AreRefused()
    {
        var alice = Party(Alice, 0, Stack(1, 1000, 1), Stack(2, 1000, 1));
        var bob = Party(Bob, 0);

        var result = Plan(alice, Offer(), 0, bob, Offer(), 0, out _);

        Assert.AreEqual(TradeCommitFailureKind.InventoryMismatch, result.FailureKind);
    }

    [TestMethod]
    public void CoinOfferAboveBalance_IsRefused()
    {
        var alice = Party(Alice, 10);
        var bob = Party(Bob, 0);

        Assert.AreEqual(
            TradeCommitFailureKind.CoinMismatch,
            Plan(alice, Offer(), 11, bob, Offer(), 0, out _).FailureKind);
        Assert.AreEqual(
            TradeCommitFailureKind.CoinMismatch,
            Plan(alice, Offer(), -1, bob, Offer(), 0, out _).FailureKind);
    }

    [TestMethod]
    public void CoinsPastIntMax_AreRefused()
    {
        var alice = Party(Alice, 10);
        var bob = Party(Bob, int.MaxValue - 5);

        var result = Plan(alice, Offer(), 10, bob, Offer(), 0, out _);

        Assert.AreEqual(TradeCommitFailureKind.CoinOverflow, result.FailureKind);
        Assert.AreEqual(Bob, result.ResponsiblePlayerGuid);
    }

    [TestMethod]
    public void IncomingStackPastTheRecipientsMaxStack_IsRefusedAndBlamesTheRecipient()
    {
        var alice = Party(Alice, 0, Stack(1, 1000, 5, maxStackSize: 10));
        var bob = Party(Bob, 0, Stack(1, 1000, 8, maxStackSize: 10));

        var result = Plan(alice, Offer((1, 5)), 0, bob, Offer(), 0, out _);

        Assert.AreEqual(TradeCommitFailureKind.StackOverflow, result.FailureKind);
        Assert.AreEqual(Bob, result.ResponsiblePlayerGuid);
    }

    [TestMethod]
    public void TradingWithYourself_IsRefused()
    {
        var alice = Party(Alice, 0);

        var result = Plan(alice, Offer(), 0, alice, Offer(), 0, out _);

        Assert.AreEqual(TradeCommitFailureKind.InvalidRequest, result.FailureKind);
    }
}
