using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Store;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Buying took the client's quantity on trust: a free item came in any number in one request, and a coin pet was a
/// database row per unit asked for.
/// </summary>
[TestClass]
public sealed class StorePurchaseTests
{
    private static ClientItemDefinition Item(int id = 0, int maxStackSize = -1, int iconTintId = 0) =>
        new() { Id = id, MaxStackSize = maxStackSize, Icon = { TintId = iconTintId } };

    [TestMethod]
    public void AnItemWithNoStackLimit_SellsUpToTheQuantityPickersNinetyNine()
    {
        foreach (var maxStackSize in new[] { -1, 0 })
        {
            Assert.IsTrue(StorePurchase.IsQuantityAllowed(Item(maxStackSize: maxStackSize), 1));
            Assert.IsTrue(StorePurchase.IsQuantityAllowed(Item(maxStackSize: maxStackSize), 99));
            Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(maxStackSize: maxStackSize), 100));
            Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(maxStackSize: maxStackSize), int.MaxValue));
        }
    }

    [TestMethod]
    public void AnItemWithAStackLimit_SellsUpToItsLimit()
    {
        Assert.IsTrue(StorePurchase.IsQuantityAllowed(Item(maxStackSize: 50), 50));
        Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(maxStackSize: 50), 51));
        Assert.IsTrue(StorePurchase.IsQuantityAllowed(Item(maxStackSize: 1), 1));
        Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(maxStackSize: 1), 2));
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    [DataRow(-1_000_000L)]
    public void NoneOrANegativeQuantity_IsRefused(long quantity)
    {
        Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(), quantity));
        Assert.IsFalse(StorePurchase.IsQuantityAllowed(Item(maxStackSize: 50), quantity));
    }

    [TestMethod]
    public void AnOrderOfManyPets_IsRefused()
    {
        // 15956, Taz: 9 coins for a member and no stack limit, like every coin store pet. 1,000 starting coins bought 111.
        var taz = new ClientItemDefinition { Id = 15956, Type = 2, MaxStackSize = -1 };

        Assert.IsTrue(StorePurchase.IsQuantityAllowed(taz, 1));
        Assert.IsFalse(StorePurchase.IsQuantityAllowed(taz, 111));
    }
}
