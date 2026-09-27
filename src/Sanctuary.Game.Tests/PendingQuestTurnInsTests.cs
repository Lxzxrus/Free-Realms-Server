using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class PendingQuestTurnInsTests
{
    /// <summary>
    /// Turn-ins used to wait in a single slot, so when two quests finished in the same tick the
    /// second overwrote the first and the first could never be handed in.
    /// </summary>
    [TestMethod]
    public void QuestsFinishingTogether_AreAllHandedInInOrder()
    {
        var pending = new PendingQuestTurnIns();

        pending.Enqueue(2563);
        pending.Enqueue(2564);

        Assert.IsTrue(pending.TryDequeue(_ => true, out var first));
        Assert.AreEqual(2563, first);

        Assert.IsTrue(pending.TryDequeue(_ => true, out var second));
        Assert.AreEqual(2564, second);

        Assert.IsFalse(pending.TryDequeue(_ => true, out _));
    }

    [TestMethod]
    public void ReopeningTheSameTurnIn_QueuesItOnce()
    {
        var pending = new PendingQuestTurnIns();

        Assert.IsTrue(pending.Enqueue(2563));
        Assert.IsFalse(pending.Enqueue(2563));

        Assert.AreEqual(1, pending.Count);
    }

    [TestMethod]
    public void AQuestAbandonedBeforeItsTurnIn_IsSkipped()
    {
        var pending = new PendingQuestTurnIns();
        var active = new HashSet<int> { 2564 };

        pending.Enqueue(2563);
        pending.Enqueue(2564);

        Assert.IsTrue(pending.TryDequeue(active.Contains, out var questId));
        Assert.AreEqual(2564, questId);
        Assert.AreEqual(0, pending.Count);
    }

    [TestMethod]
    public void NothingPending_HandsInNothing()
    {
        Assert.IsFalse(new PendingQuestTurnIns().TryDequeue(_ => true, out _));
    }
}
