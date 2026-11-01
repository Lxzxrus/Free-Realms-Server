using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The Gateway queues an equip's database write per connection (GatewayConnection.QueueWork). The queue had no limit,
/// so a client sending equips faster than the database saved them grew it without end (stage 1 findings core-4 and
/// economy-3).
/// </summary>
[TestClass]
public sealed class SerialWorkQueueTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    // the items run one at a time, so the lists they add to need no lock
    private readonly List<Exception> _errors = [];

    [TestMethod]
    public void Work_RunsOneAtATime_InOrder()
    {
        var queue = new SerialWorkQueue(100);
        var ran = new List<int>();
        var running = 0;
        var overlapped = false;

        for (var i = 0; i < 50; i++)
        {
            var item = i;

            Assert.IsTrue(queue.TryQueue(() =>
            {
                overlapped |= Interlocked.Increment(ref running) > 1;
                ran.Add(item);
                Interlocked.Decrement(ref running);
            }, _errors.Add));
        }

        Assert.IsTrue(queue.WhenIdle().Wait(Wait));

        Assert.IsFalse(overlapped);
        CollectionAssert.AreEqual(Enumerable.Range(0, 50).ToList(), ran);
        Assert.AreEqual(0, queue.Pending);
        Assert.IsEmpty(_errors);
    }

    [TestMethod]
    public void FullQueue_RefusesWork_UntilSomeHasRun()
    {
        var queue = new SerialWorkQueue(3);
        using var gate = new ManualResetEventSlim();
        var ran = 0;

        // the first holds the queue up, as a slow database write would
        Assert.IsTrue(queue.TryQueue(() => gate.Wait(Wait), _errors.Add));
        Assert.IsTrue(queue.TryQueue(() => ran++, _errors.Add));
        Assert.IsTrue(queue.TryQueue(() => ran++, _errors.Add));

        Assert.IsTrue(queue.IsFull);
        Assert.IsFalse(queue.TryQueue(() => ran++, _errors.Add));
        Assert.AreEqual(3, queue.Pending, "the refused item was not queued");

        gate.Set();
        Assert.IsTrue(queue.WhenIdle().Wait(Wait));

        Assert.AreEqual(2, ran);
        Assert.IsFalse(queue.IsFull);

        Assert.IsTrue(queue.TryQueue(() => ran++, _errors.Add));
        Assert.IsTrue(queue.WhenIdle().Wait(Wait));

        Assert.AreEqual(3, ran);
        Assert.IsEmpty(_errors);
    }

    [TestMethod]
    public void Exception_IsReported_AndTheWorkAfterItStillRuns()
    {
        var queue = new SerialWorkQueue(10);
        var ran = false;

        Assert.IsTrue(queue.TryQueue(() => throw new InvalidOperationException("A database error."), _errors.Add));
        Assert.IsTrue(queue.TryQueue(() => ran = true, _errors.Add));
        Assert.IsTrue(queue.WhenIdle().Wait(Wait));

        Assert.IsInstanceOfType<InvalidOperationException>(_errors.Single());
        Assert.IsTrue(ran);
        Assert.AreEqual(0, queue.Pending);
    }
}
