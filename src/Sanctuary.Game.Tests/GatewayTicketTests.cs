using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A Gateway ticket stayed valid until the next login replaced it (audit triage, SEC-15). Now it carries the time the
/// Login server made it, and the Gateway refuses it after <see cref="GatewayTicket.Lifetime"/>.
/// </summary>
[TestClass]
public sealed class GatewayTicketTests
{
    private static readonly DateTimeOffset Issued = new(2026, 11, 23, 4, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void FreshTicketIsAccepted()
    {
        var ticket = GatewayTicket.Create(Issued);

        Assert.IsTrue(GatewayTicket.IsFresh(ticket, Issued));
        Assert.IsTrue(GatewayTicket.IsFresh(ticket, Issued + GatewayTicket.Lifetime));
    }

    [TestMethod]
    public void OldTicketIsRefused()
    {
        var ticket = GatewayTicket.Create(Issued);

        Assert.IsFalse(GatewayTicket.IsFresh(ticket, Issued + GatewayTicket.Lifetime + TimeSpan.FromMilliseconds(1)));
        Assert.IsFalse(GatewayTicket.IsFresh(ticket, Issued + TimeSpan.FromDays(3)));
    }

    [TestMethod]
    public void TicketFromTheFutureIsRefusedPastTheSkew()
    {
        var ticket = GatewayTicket.Create(Issued);

        Assert.IsTrue(GatewayTicket.IsFresh(ticket, Issued - GatewayTicket.ClockSkew));
        Assert.IsFalse(GatewayTicket.IsFresh(ticket, Issued - GatewayTicket.ClockSkew - TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void TicketWithoutATimeIsRefused()
    {
        // what the Login server issued before, and what a ticket typed in by hand would be
        Assert.IsFalse(GatewayTicket.IsFresh(Guid.NewGuid(), Issued));
        Assert.IsFalse(GatewayTicket.IsFresh(Guid.Empty, Issued));
    }

    [TestMethod]
    [DataRow("ffffffff-ffff-7fff-bfff-ffffffffffff", DisplayName = "the largest time 48 bits hold")]
    [DataRow("e7000000-0000-7000-8000-000000000000", DisplayName = "just past the year 9999")]
    [DataRow("e677d21f-dbff-7000-8000-000000000000", DisplayName = "the last millisecond of 9999")]
    [DataRow("00000000-0000-7000-8000-000000000000", DisplayName = "1970")]
    public void TicketWithAForgedTimeIsRefusedWithoutThrowing(string forged)
    {
        // a version 7 Guid, as Guid.TryParse reads it from the client, whose time is the client's to choose
        var ticket = Guid.Parse(forged);

        Assert.AreEqual(7, ticket.Version);
        Assert.IsFalse(GatewayTicket.IsFresh(ticket, Issued));
    }

    [TestMethod]
    public void TicketSurvivesTheClientsTextForm()
    {
        // the client is given the ticket as 32 hex digits and sends it back the same way
        var ticket = GatewayTicket.Create(Issued);

        Assert.IsTrue(Guid.TryParse(ticket.ToString("N"), out var parsed));
        Assert.IsTrue(GatewayTicket.IsFresh(parsed, Issued + TimeSpan.FromSeconds(10)));
    }

    [TestMethod]
    public void TicketsAreDistinct()
    {
        Assert.AreNotEqual(GatewayTicket.Create(Issued), GatewayTicket.Create(Issued));
    }
}
