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
    public void TheClientsFourSlots_AreSlots()
    {
        Assert.AreEqual(4, Player.ItemActionBarSlotCount);

        for (var slot = 0; slot < Player.ItemActionBarSlotCount; slot++)
            Assert.IsTrue(Player.IsItemActionBarSlot(slot), $"Slot {slot} is one of the bar's.");
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
