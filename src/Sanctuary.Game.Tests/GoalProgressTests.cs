using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class GoalProgressTests
{
    [TestMethod]
    public void GoalProgress_NewHasZeroCount_IncrementAddsOne()
    {
        var gp = new GoalProgress();
        Assert.AreEqual(0, gp.Count);
        gp.Increment();
        Assert.AreEqual(1, gp.Count);
        gp.Increment();
        Assert.AreEqual(2, gp.Count);
    }

    [TestMethod]
    public void GoalProgress_Save_ReturnsNoRowsWhenCountIsZero()
    {
        var gp = new GoalProgress();
        var rows = gp.Save().ToArray();
        Assert.AreEqual(0, rows.Length);
    }

    [TestMethod]
    public void GoalProgress_Save_ReturnsOneRowWhenCountAboveZero()
    {
        var gp = new GoalProgress();
        gp.Increment();
        gp.Increment();
        gp.Increment();
        var rows = gp.Save().ToArray();
        CollectionAssert.AreEquivalent(new (ulong, int)[] { (0UL, 3) }, rows);
    }

    [TestMethod]
    public void GoalProgress_Load_AddsToCount()
    {
        var gp = new GoalProgress();
        gp.Load(0, 3);
        gp.Load(0, 2);
        Assert.AreEqual(5, gp.Count);
    }

    [TestMethod]
    public void GoalProgress_Load_TargetIdIsIgnored()
    {
        var gp = new GoalProgress();
        gp.Load(7, 2);
        Assert.AreEqual(2, gp.Count);
    }

    [TestMethod]
    public void GoalProgress_RoundTrip_PreservesCount()
    {
        var source = new GoalProgress();
        source.Increment();
        source.Increment();
        source.Increment();

        var dest = new GoalProgress();
        foreach (var (targetId, count) in source.Save())
            dest.Load(targetId, count);

        Assert.AreEqual(source.Count, dest.Count);
    }

    [TestMethod]
    public void TalkProgress_TryAdd_ReturnsTrueFirstTimeFalseAfter()
    {
        var tp = new TalkProgress();
        Assert.IsTrue(tp.TryAdd(1));
        Assert.IsFalse(tp.TryAdd(1));
        Assert.AreEqual(1, tp.Count);
    }

    [TestMethod]
    public void TalkProgress_CountCountsDistinctNpcsOnly()
    {
        var tp = new TalkProgress();
        tp.TryAdd(1);
        tp.TryAdd(2);
        tp.TryAdd(3);
        tp.TryAdd(1);
        Assert.AreEqual(3, tp.Count);
    }

    [TestMethod]
    public void TalkProgress_HasTalkedTo_OnlyForAddedNpcs()
    {
        var tp = new TalkProgress();
        tp.TryAdd(10);
        tp.TryAdd(20);
        Assert.IsTrue(tp.HasTalkedTo(10));
        Assert.IsTrue(tp.HasTalkedTo(20));
        Assert.IsFalse(tp.HasTalkedTo(30));
    }

    [TestMethod]
    public void TalkProgress_Save_ReturnsOneRowPerNpc()
    {
        var tp = new TalkProgress();
        tp.TryAdd(5);
        tp.TryAdd(7);
        tp.TryAdd(9);

        var rows = tp.Save().ToArray();
        CollectionAssert.AreEquivalent(new (ulong, int)[] { (5UL, 1), (7UL, 1), (9UL, 1) }, rows);
    }

    [TestMethod]
    public void TalkProgress_Load_NonZeroTarget_AddsNpcAndIgnoresCount()
    {
        var tp = new TalkProgress();
        tp.Load(42, 5);
        Assert.AreEqual(1, tp.Count);
        Assert.IsTrue(tp.HasTalkedTo(42));
    }

    [TestMethod]
    public void TalkProgress_Load_ZeroTarget_AddsNothing()
    {
        var tp = new TalkProgress();
        tp.Load(0, 5);
        Assert.AreEqual(0, tp.Count);
    }

    [TestMethod]
    public void TalkProgress_Load_SameNpcTwice_CountsOnce()
    {
        var tp = new TalkProgress();
        tp.Load(42, 5);
        tp.Load(42, 5);
        Assert.AreEqual(1, tp.Count);
        Assert.IsTrue(tp.HasTalkedTo(42));
    }

    [TestMethod]
    public void TalkProgress_RoundTrip_PreservesNpcsAndCount()
    {
        var source = new TalkProgress();
        source.TryAdd(1);
        source.TryAdd(2);
        source.TryAdd(3);

        var dest = new TalkProgress();
        foreach (var (targetId, count) in source.Save())
            dest.Load(targetId, count);

        Assert.AreEqual(source.Count, dest.Count);
        Assert.IsTrue(dest.HasTalkedTo(1));
        Assert.IsTrue(dest.HasTalkedTo(2));
        Assert.IsTrue(dest.HasTalkedTo(3));
    }
}
