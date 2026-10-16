using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Housing;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HouseAccessTests
{
    // The owner bought the house; the alt is another character on the owner's account. The visitor plays on another
    // account, and the alt's friend is a friend of the alt only.
    private const ulong OwnerId = 7;
    private const ulong AltId = 9;
    private const ulong VisitorId = 8;

    private const ulong OwnersAccount = 1;
    private const ulong VisitorsAccount = 2;

    private static FriendData Friend(ulong characterId) => new() { Guid = GuidHelper.GetPlayerGuid(characterId) };

    private static DbHouse House(bool published = false, bool locked = false) => new()
    {
        Id = 1,
        CharacterId = OwnerId,
        IsPublished = published,
        IsLocked = locked
    };

    private static bool MayEnter(ulong playerUserId, FriendData[] friends, DbHouse house) =>
        HouseAccess.MayEnter(playerUserId, friends, isMember: true, house, OwnersAccount);

    [TestMethod]
    public void Owner_MayBeInLockedHouse()
    {
        Assert.IsTrue(HouseAccess.MayBeInLockedHouse(OwnersAccount, [], OwnersAccount, OwnerId));
    }

    [TestMethod]
    public void OwnersFriend_MayBeInLockedHouse()
    {
        Assert.IsTrue(HouseAccess.MayBeInLockedHouse(VisitorsAccount, [Friend(OwnerId)], OwnersAccount, OwnerId));
    }

    [TestMethod]
    public void Stranger_MayNotBeInLockedHouse()
    {
        Assert.IsFalse(HouseAccess.MayBeInLockedHouse(VisitorsAccount, [Friend(99)], OwnersAccount, OwnerId));
    }

    [TestMethod]
    public void FriendOfSomeoneElse_IsNotOwnersFriend()
    {
        Assert.IsFalse(HouseAccess.IsOwnersFriend([Friend(VisitorId), Friend(99)], OwnerId));
        Assert.IsTrue(HouseAccess.IsOwnersFriend([Friend(99), Friend(OwnerId)], OwnerId));
    }

    [TestMethod]
    public void FriendListHoldsGuids_NotCharacterIds()
    {
        // A friend entry whose raw guid happens to equal the owner's character id isn't the owner.
        Assert.IsFalse(HouseAccess.IsOwnersFriend([new FriendData { Guid = OwnerId }], OwnerId));
    }

    [TestMethod]
    public void EveryCharacterOnTheAccount_IsTheOwner()
    {
        Assert.IsTrue(HouseAccess.IsOwner(OwnersAccount, OwnersAccount));
        Assert.IsFalse(HouseAccess.IsOwner(VisitorsAccount, OwnersAccount));
    }

    [TestMethod]
    public void NoAccount_OwnsNothing()
    {
        // A player whose account wasn't loaded must not own a house whose owner couldn't be found either.
        Assert.IsFalse(HouseAccess.IsOwner(0, 0));
        Assert.IsFalse(HouseAccess.MayBeInLockedHouse(0, [], 0, OwnerId));
    }

    [TestMethod]
    public void SecondCharacterOnTheAccount_EntersAnUnpublishedOrLockedHouse()
    {
        Assert.IsTrue(MayEnter(OwnersAccount, [], House()));
        Assert.IsTrue(MayEnter(OwnersAccount, [], House(published: true, locked: true)));
        Assert.IsTrue(HouseAccess.MayBeInLockedHouse(OwnersAccount, [], OwnersAccount, OwnerId));
    }

    [TestMethod]
    public void AnotherAccount_IsNotLetIn_EvenWithTheSameCharacterIdAsTheBuyer()
    {
        // Ownership is the account's: a character id alone, even the buyer's, is not enough.
        Assert.IsFalse(MayEnter(VisitorsAccount, [], House()));
        Assert.IsFalse(MayEnter(VisitorsAccount, [], House(published: true, locked: true)));
        Assert.IsFalse(HouseAccess.MayBeInLockedHouse(VisitorsAccount, [], OwnersAccount, OwnerId));
    }

    [TestMethod]
    public void VisitorsRights_AreUnchanged()
    {
        // Published and unlocked: anyone. Otherwise: friends of the character who bought it, as before.
        Assert.IsTrue(MayEnter(VisitorsAccount, [], House(published: true)));
        Assert.IsTrue(MayEnter(VisitorsAccount, [Friend(OwnerId)], House()));
        Assert.IsTrue(MayEnter(VisitorsAccount, [Friend(OwnerId)], House(published: true, locked: true)));
    }

    [TestMethod]
    public void FriendOfTheAltOnly_GainsNothing()
    {
        // The house's friends are the buyer's. Sharing the house with the alt doesn't share it with the alt's friends.
        Assert.IsFalse(MayEnter(VisitorsAccount, [Friend(AltId)], House()));
        Assert.IsFalse(HouseAccess.MayBeInLockedHouse(VisitorsAccount, [Friend(AltId)], OwnersAccount, OwnerId));
    }
}
