using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class EmptyZoneGraceTests
{
    [TestMethod]
    public void ZoneThatStayedEmpty_Closes()
    {
        var grace = new EmptyZoneGrace();
        var zone = new object();

        var ticket = grace.Begin(zone);

        Assert.IsTrue(grace.End(zone, ticket));
    }

    [TestMethod]
    public void ZoneThatEmptiedAgain_WaitsForTheLatestGracePeriod()
    {
        var grace = new EmptyZoneGrace();
        var zone = new object();

        var first = grace.Begin(zone);
        var second = grace.Begin(zone);

        Assert.IsFalse(grace.End(zone, first), "The first grace period must not close the zone early.");
        Assert.IsTrue(grace.End(zone, second));
    }

    [TestMethod]
    public void Ticket_ClosesOnlyOnce()
    {
        var grace = new EmptyZoneGrace();
        var zone = new object();

        var ticket = grace.Begin(zone);

        Assert.IsTrue(grace.End(zone, ticket));
        Assert.IsFalse(grace.End(zone, ticket));
    }

    [TestMethod]
    public void Zones_HaveTheirOwnGracePeriods()
    {
        var grace = new EmptyZoneGrace();
        var house = new object();
        var otherHouse = new object();

        var houseTicket = grace.Begin(house);
        var otherTicket = grace.Begin(otherHouse);

        Assert.IsFalse(grace.End(house, otherTicket));
        Assert.IsTrue(grace.End(house, houseTicket));
        Assert.IsTrue(grace.End(otherHouse, otherTicket));
    }
}
