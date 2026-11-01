using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class ZoneLoadRepliesTests
{
    [TestMethod]
    public void RepeatInTheSameZone_IsAnsweredOnlyAfterAWhile()
    {
        var replies = new ZoneLoadReplies();
        var zone = new object();

        Assert.IsTrue(replies.TryAnswer(zone, now: 10_000));
        Assert.IsFalse(replies.TryAnswer(zone, now: 10_000));
        Assert.IsFalse(replies.TryAnswer(zone, now: 10_000 + ZoneLoadReplies.RepeatAfterMs - 1));
        Assert.IsTrue(replies.TryAnswer(zone, now: 10_000 + ZoneLoadReplies.RepeatAfterMs));
    }

    [TestMethod]
    public void FirstInANewZone_IsAlwaysAnswered()
    {
        var replies = new ZoneLoadReplies();
        var world = new object();
        var house = new object();

        Assert.IsTrue(replies.TryAnswer(world, now: 10_000));
        Assert.IsTrue(replies.TryAnswer(house, now: 10_001), "Into a house at once.");
        Assert.IsTrue(replies.TryAnswer(world, now: 10_002), "And out again: a new zone for this answer.");
        Assert.IsFalse(replies.TryAnswer(world, now: 10_003));
    }

    [TestMethod]
    public void ReadyAndFinishedLoading_AreAnsweredApart()
    {
        // The player has one of each: a "ready" answered doesn't use up the "finished loading" that follows it.
        var ready = new ZoneLoadReplies();
        var finishedLoading = new ZoneLoadReplies();
        var zone = new object();

        Assert.IsTrue(ready.TryAnswer(zone, now: 10_000));
        Assert.IsTrue(finishedLoading.TryAnswer(zone, now: 10_500));
        Assert.IsFalse(finishedLoading.TryAnswer(zone, now: 10_600));
    }
}
