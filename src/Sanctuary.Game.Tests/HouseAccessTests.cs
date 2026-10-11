using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HouseAccessTests
{
    private const ulong OwnerId = 7;
    private const ulong VisitorId = 8;

    private static FriendData Friend(ulong characterId) => new() { Guid = GuidHelper.GetPlayerGuid(characterId) };

    [TestMethod]
    public void Owner_MayBeInLockedHouse()
    {
        Assert.IsTrue(HouseAccess.MayBeInLockedHouse(GuidHelper.GetPlayerGuid(OwnerId), [], OwnerId));
    }

    [TestMethod]
    public void OwnersFriend_MayBeInLockedHouse()
    {
        Assert.IsTrue(HouseAccess.MayBeInLockedHouse(GuidHelper.GetPlayerGuid(VisitorId), [Friend(OwnerId)], OwnerId));
    }

    [TestMethod]
    public void Stranger_MayNotBeInLockedHouse()
    {
        Assert.IsFalse(HouseAccess.MayBeInLockedHouse(GuidHelper.GetPlayerGuid(VisitorId), [Friend(99)], OwnerId));
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
}
