using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Store;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Selling to the coin store took the client's quantity on trust (task 30): a negative one grew the stack, and
/// the coins were added with int arithmetic that wraps.
/// </summary>
[TestClass]
public sealed class CoinStoreSaleTests
{
    private static ClientItemDefinition Item(int resellValue, bool noSale = false) =>
        new() { Id = 10422, ResellValue = resellValue, NoSale = noSale };

    [TestMethod]
    public void SellingSome_LeavesTheRest_AndPaysResellValueEach()
    {
        Assert.IsTrue(CoinStoreSale.TryPlan(Item(27), ownedCount: 5, quantity: 2, coins: 100, out var plan, out _));

        Assert.AreEqual(3, plan.RemainingCount);
        Assert.AreEqual(154, plan.Coins);
    }

    [TestMethod]
    public void SellingAll_LeavesNone()
    {
        Assert.IsTrue(CoinStoreSale.TryPlan(Item(0), ownedCount: 1, quantity: 1, coins: 7, out var plan, out _));

        Assert.AreEqual(0, plan.RemainingCount);
        Assert.AreEqual(7, plan.Coins);
    }

    [TestMethod]
    public void NegativeQuantity_IsRefused_NotAddedToTheStack()
    {
        Assert.IsFalse(CoinStoreSale.TryPlan(Item(27), ownedCount: 1, quantity: -1_000_000, coins: 0, out _, out var refusal));
        Assert.AreEqual(CoinStoreSaleRefusal.Quantity, refusal);
    }

    [TestMethod]
    public void ZeroQuantity_IsRefused()
    {
        Assert.IsFalse(CoinStoreSale.TryPlan(Item(27), ownedCount: 1, quantity: 0, coins: 0, out _, out var refusal));
        Assert.AreEqual(CoinStoreSaleRefusal.Quantity, refusal);
    }

    [TestMethod]
    public void MoreThanOwned_IsRefused()
    {
        Assert.IsFalse(CoinStoreSale.TryPlan(Item(27), ownedCount: 2, quantity: 3, coins: 0, out _, out var refusal));
        Assert.AreEqual(CoinStoreSaleRefusal.Quantity, refusal);
    }

    [TestMethod]
    public void CoinsThatWouldWrap_AreRefused()
    {
        // 2500 is the highest resell value shipped; the client can claim any stack size it likes once the stack is big.
        Assert.IsFalse(CoinStoreSale.TryPlan(Item(2500), ownedCount: int.MaxValue, quantity: 1_000_000, coins: 0, out _, out var refusal));
        Assert.AreEqual(CoinStoreSaleRefusal.Overflow, refusal);

        Assert.IsFalse(CoinStoreSale.TryPlan(Item(1), ownedCount: 1, quantity: 1, coins: int.MaxValue, out _, out refusal));
        Assert.AreEqual(CoinStoreSaleRefusal.Overflow, refusal);
    }

    [TestMethod]
    public void NoSaleItem_IsRefused()
    {
        Assert.IsFalse(CoinStoreSale.TryPlan(Item(27, noSale: true), ownedCount: 1, quantity: 1, coins: 0, out _, out var refusal));
        Assert.AreEqual(CoinStoreSaleRefusal.NoSale, refusal);
    }

    [TestMethod]
    public void NegativeResellValue_PaysNothing()
    {
        Assert.IsTrue(CoinStoreSale.TryPlan(Item(-5), ownedCount: 1, quantity: 1, coins: 10, out var plan, out _));
        Assert.AreEqual(10, plan.Coins);
    }
}
