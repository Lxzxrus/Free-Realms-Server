using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class ZoneLoadRepliesTests
{
    [TestMethod]
    public void RepeatForTheSameZoneEntry_IsAnsweredOnlyAfterAWhile()
    {
        var replies = new ZoneLoadReplies();

        Assert.IsTrue(replies.TryAnswer(zoneEntry: 0, now: 10_000));
        Assert.IsFalse(replies.TryAnswer(zoneEntry: 0, now: 10_000));
        Assert.IsFalse(replies.TryAnswer(zoneEntry: 0, now: 10_000 + ZoneLoadReplies.RepeatAfterMs - 1));
        Assert.IsTrue(replies.TryAnswer(zoneEntry: 0, now: 10_000 + ZoneLoadReplies.RepeatAfterMs));
    }

    [TestMethod]
    public void FirstForEachZoneEntry_IsAlwaysAnswered()
    {
        var replies = new ZoneLoadReplies();

        Assert.IsTrue(replies.TryAnswer(zoneEntry: 0, now: 10_000), "The zone the player logged in to.");
        Assert.IsTrue(replies.TryAnswer(zoneEntry: 1, now: 10_001), "Into a house at once.");
        Assert.IsTrue(replies.TryAnswer(zoneEntry: 2, now: 10_002), "And out again: a new entry for this answer.");
        Assert.IsFalse(replies.TryAnswer(zoneEntry: 2, now: 10_003));
    }

    [TestMethod]
    public void BackInTheZoneAnsweredLast_IsAnsweredAgain()
    {
        // Answered in the world (entry 1), moved into a house (2) and sent out again as its owner locked it (3), before
        // the client reported the house: back in the world, it loads it again and must be answered, however soon.
        var replies = new ZoneLoadReplies();

        Assert.IsTrue(replies.TryAnswer(zoneEntry: 1, now: 10_000));
        Assert.IsTrue(replies.TryAnswer(zoneEntry: 3, now: 10_500));
        Assert.IsFalse(replies.TryAnswer(zoneEntry: 3, now: 10_600));
    }

    [TestMethod]
    public void ReadyAndFinishedLoading_AreAnsweredApart()
    {
        // The player has one of each: a "ready" answered doesn't use up the "finished loading" that follows it.
        var ready = new ZoneLoadReplies();
        var finishedLoading = new ZoneLoadReplies();

        Assert.IsTrue(ready.TryAnswer(zoneEntry: 0, now: 10_000));
        Assert.IsTrue(finishedLoading.TryAnswer(zoneEntry: 0, now: 10_500));
        Assert.IsFalse(finishedLoading.TryAnswer(zoneEntry: 0, now: 10_600));
    }
}
