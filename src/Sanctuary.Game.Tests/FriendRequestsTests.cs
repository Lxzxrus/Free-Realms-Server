using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class FriendRequestsTests
{
    [TestMethod]
    public void RefusedRequests_AreExplained()
    {
        Assert.AreEqual("You can't add yourself as a friend.", FriendRequests.Explain(FriendRequestResult.ToSelf, "Ariel Moon"));
        Assert.AreEqual("Ariel Moon is already your friend.", FriendRequests.Explain(FriendRequestResult.AlreadyFriends, "Ariel Moon"));
        Assert.AreEqual("You've already sent Ariel Moon a friend request.", FriendRequests.Explain(FriendRequestResult.AlreadyRequested, "Ariel Moon"));
    }

    [TestMethod]
    public void SentAndIgnoredRequests_LookTheSame()
    {
        // The client shows its own "request sent" message, and an ignored sender must not be able to tell the difference.
        Assert.IsNull(FriendRequests.Explain(FriendRequestResult.Sent, "Ariel Moon"));
        Assert.IsNull(FriendRequests.Explain(FriendRequestResult.Ignored, "Ariel Moon"));
    }

    [TestMethod]
    public void MissingAndOfflinePlayers_AreExplained()
    {
        Assert.AreEqual("There's no player named Nobody Here.", FriendRequests.NoSuchPlayer("Nobody Here"));
        Assert.AreEqual("Ariel Moon isn't online. Friend requests can only be sent to players who are online.", FriendRequests.Offline("Ariel Moon"));
    }
}
