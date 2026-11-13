using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class ZoneManagerTests
{
    private const int Seaside = 14, Combat = 500, FabledRealms = 1;
    private const ulong Gone = 10, Eldest = 12;

    /// <summary>
    /// A house's instance is kept by the house, so a house loaded for a character that's then deleted, and found again
    /// under the character it moved to (Housing.HouseHandover), is the same instance, not a second copy.
    /// </summary>
    [TestMethod]
    public void AHousesInstance_IsKeptByTheHouseNotItsHolder()
    {
        Assert.AreEqual(ZoneManager.InstanceKey(Seaside, Gone, 100), ZoneManager.InstanceKey(Seaside, Eldest, 100));
        Assert.AreEqual((Seaside, (ulong?)100), ZoneManager.InstanceKey(Seaside, Gone, 100));
        Assert.AreNotEqual(
            ZoneManager.InstanceKey(Seaside, Eldest, 100),
            ZoneManager.InstanceKey(Seaside, Eldest, 101),
            "Two houses are two instances.");
    }

    [TestMethod]
    public void OtherZones_AreKeptByTheirOwner()
    {
        Assert.AreEqual((Combat, (ulong?)Gone), ZoneManager.InstanceKey(Combat, Gone, null));
        Assert.AreNotEqual(ZoneManager.InstanceKey(Combat, Gone, null), ZoneManager.InstanceKey(Combat, Eldest, null));
        Assert.AreEqual((FabledRealms, (ulong?)null), ZoneManager.InstanceKey(FabledRealms, null, null));
    }
}
