using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The item action bar's slot number is the client's (InventoryPacketItemActionBarAssign). Any int used to be kept, and
/// for an item on cooldown each new slot number scheduled a re-enable packet that every zone tick walked.
/// </summary>
[TestClass]
public sealed class ItemActionBarTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void TheClientsFourSlots_AreSlots(int slot)
    {
        Assert.IsTrue(Player.IsItemActionBarSlot(slot));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(4)]
    [DataRow(int.MaxValue)]
    [DataRow(int.MinValue)]
    public void AnyOtherSlotNumber_IsNotASlot(int slot)
    {
        Assert.IsFalse(Player.IsItemActionBarSlot(slot));
    }
}
