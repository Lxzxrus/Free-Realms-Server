using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HouseInvitesTests
{
    private const ulong Guest = 8;
    private const ulong Owner = 7;
    private const ulong House = 1;
    private const ulong OtherHouse = 2;

    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Invite_LetsTheGuestIntoThatHouseOnly()
    {
        var invites = new HouseInvites();
        invites.Add(Guest, House, Owner, Now);

        Assert.IsTrue(invites.IsInvited(Guest, House, Now));
        Assert.IsFalse(invites.IsInvited(Guest, OtherHouse, Now));
        Assert.IsFalse(invites.IsInvited(Owner, House, Now));
    }

    [TestMethod]
    public void Invite_RunsOut()
    {
        var invites = new HouseInvites();
        invites.Add(Guest, House, Owner, Now);

        Assert.IsTrue(invites.IsInvited(Guest, House, Now + HouseInvites.Lifetime - TimeSpan.FromSeconds(1)));
        Assert.IsFalse(invites.IsInvited(Guest, House, Now + HouseInvites.Lifetime));
        Assert.IsFalse(invites.TryUse(Guest, House, Now + HouseInvites.Lifetime));
    }

    [TestMethod]
    public void InvitingAgain_RenewsTheInvite()
    {
        var invites = new HouseInvites();
        invites.Add(Guest, House, Owner, Now);
        invites.Add(Guest, House, Owner, Now + TimeSpan.FromMinutes(4));

        Assert.IsTrue(invites.IsInvited(Guest, House, Now + TimeSpan.FromMinutes(8)));
    }

    [TestMethod]
    public void Invite_IsUsedOnce()
    {
        var invites = new HouseInvites();
        invites.Add(Guest, House, Owner, Now);

        Assert.IsTrue(invites.TryUse(Guest, House, Now));
        Assert.IsFalse(invites.IsInvited(Guest, House, Now));
        Assert.IsFalse(invites.TryUse(Guest, House, Now));
    }

    [TestMethod]
    public void Declining_SaysWhoInvited()
    {
        var invites = new HouseInvites();
        invites.Add(Guest, House, Owner, Now);

        Assert.IsTrue(invites.TryRemove(Guest, House, Now, out var inviter));
        Assert.AreEqual(Owner, inviter);
        Assert.IsFalse(invites.IsInvited(Guest, House, Now));
    }

    [TestMethod]
    public void DecliningWithoutAnInvite_DoesNothing()
    {
        var invites = new HouseInvites();

        Assert.IsFalse(invites.TryRemove(Guest, House, Now, out var inviter));
        Assert.AreEqual(0UL, inviter);
    }
}
