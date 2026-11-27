using System.Collections.Generic;
using System.Numerics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Pathfinding;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class PathStateTests
{
    private static List<Vector3> Drain(PathState state)
    {
        var result = new List<Vector3>();
        while (!state.IsEmpty)
        {
            // A broken Advance would otherwise loop forever.
            if (result.Count >= 100)
                Assert.Fail("Advance didn't empty the path.");

            state.TryGetCurrentTarget(out var pos);
            result.Add(pos);
            state.Advance();
        }

        return result;
    }

    [TestMethod]
    public void NewState_IsEmpty()
    {
        var state = new PathState();
        Assert.IsTrue(state.IsEmpty);
        Assert.IsFalse(state.TryGetCurrentTarget(out var pos));
        Assert.AreEqual(default(Vector3), pos);
    }

    [TestMethod]
    public void Set_PeeksWithoutRemoving()
    {
        var state = new PathState();
        var a = new Vector3(1, 0, 0);
        var b = new Vector3(2, 0, 0);
        var c = new Vector3(3, 0, 0);
        state.Set(new Queue<Vector3>(new[] { a, b, c }));

        Assert.IsFalse(state.IsEmpty);
        Assert.IsTrue(state.TryGetCurrentTarget(out var first));
        Assert.AreEqual(a, first);
        Assert.IsTrue(state.TryGetCurrentTarget(out var second));
        Assert.AreEqual(a, second);
    }

    [TestMethod]
    public void Advance_RemovesCurrentAndAdvances()
    {
        var state = new PathState();
        var a = new Vector3(1, 0, 0);
        var b = new Vector3(2, 0, 0);
        state.Set(new Queue<Vector3>(new[] { a, b }));

        state.Advance();
        Assert.IsTrue(state.TryGetCurrentTarget(out var next));
        Assert.AreEqual(b, next);

        state.Advance();
        Assert.IsTrue(state.IsEmpty);

        state.Advance();
        Assert.IsTrue(state.IsEmpty);
    }

    [TestMethod]
    public void SetTarget_ReplacesEntireQueue()
    {
        var state = new PathState();
        var a = new Vector3(1, 0, 0);
        var b = new Vector3(2, 0, 0);
        state.Set(new Queue<Vector3>(new[] { a, b }));

        var x = new Vector3(5, 0, 0);
        state.SetTarget(x);

        Assert.IsFalse(state.IsEmpty);
        Assert.IsTrue(state.TryGetCurrentTarget(out var pos));
        Assert.AreEqual(x, pos);
        state.Advance();
        Assert.IsTrue(state.IsEmpty);
    }

    [TestMethod]
    public void SetGoal_ReplacesLastWaypoint()
    {
        var state = new PathState();
        var a = new Vector3(1, 0, 0);
        var b = new Vector3(2, 0, 0);
        var c = new Vector3(3, 0, 0);
        state.Set(new Queue<Vector3>(new[] { a, b, c }));

        var g = new Vector3(9, 0, 0);
        state.SetGoal(g);

        var drained = Drain(state);
        Assert.AreEqual(3, drained.Count);
        Assert.AreEqual(a, drained[0]);
        Assert.AreEqual(b, drained[1]);
        Assert.AreEqual(g, drained[2]);
    }

    [TestMethod]
    public void SetGoal_SingleWaypoint_ReplacedByGoal()
    {
        var state = new PathState();
        var a = new Vector3(1, 0, 0);
        state.Set(new Queue<Vector3>(new[] { a }));

        var g = new Vector3(9, 0, 0);
        state.SetGoal(g);

        var drained = Drain(state);
        Assert.AreEqual(1, drained.Count);
        Assert.AreEqual(g, drained[0]);
    }

    [TestMethod]
    public void SetGoal_EmptyState_LeavesOnlyGoal()
    {
        var state = new PathState();
        var g = new Vector3(9, 0, 0);
        state.SetGoal(g);

        var drained = Drain(state);
        Assert.AreEqual(1, drained.Count);
        Assert.AreEqual(g, drained[0]);
    }

    [TestMethod]
    public void Clear_EmptiesState()
    {
        var state = new PathState();
        var a = new Vector3(1, 0, 0);
        var b = new Vector3(2, 0, 0);
        state.Set(new Queue<Vector3>(new[] { a, b }));

        state.Clear();

        Assert.IsTrue(state.IsEmpty);
        Assert.IsFalse(state.TryGetCurrentTarget(out _));
    }

    [TestMethod]
    public void Drain_ReturnsAllWaypointsInOrder()
    {
        var state = new PathState();
        var a = new Vector3(1, 0, 0);
        var b = new Vector3(2, 0, 0);
        var c = new Vector3(3, 0, 0);
        state.Set(new Queue<Vector3>(new[] { a, b, c }));

        var drained = Drain(state);
        Assert.AreEqual(3, drained.Count);
        Assert.AreEqual(a, drained[0]);
        Assert.AreEqual(b, drained[1]);
        Assert.AreEqual(c, drained[2]);
        Assert.IsTrue(state.IsEmpty);
    }
}
