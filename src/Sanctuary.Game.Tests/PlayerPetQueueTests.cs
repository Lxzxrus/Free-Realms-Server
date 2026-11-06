using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Pet packets wait for the zone's tick in <see cref="PlayerPet"/>'s queue. It is capped, and a tick carries out only
/// what was queued before it, so a client flooding pet packets can neither grow the queue without limit nor keep the
/// zone's tick from finishing.
/// </summary>
[TestClass]
public sealed class PlayerPetQueueTests
{
    private static Player NewPlayer() => new(null!, null!, null!, null!, null!);

    [TestMethod]
    public void APlayersOwnActions_AreAllCarriedOut()
    {
        var player = NewPlayer();
        var carriedOut = 0;

        for (var i = 0; i < 5; i++)
            player.PlayerPet.QueueAction(() => carriedOut++);

        player.PlayerPet.UpdateEveryTick();

        Assert.AreEqual(5, carriedOut);
    }

    [TestMethod]
    public void AFlood_StopsAtTheCap()
    {
        var player = NewPlayer();
        var carriedOut = 0;

        for (var i = 0; i < 10_000; i++)
            player.PlayerPet.QueueAction(() => carriedOut++);

        player.PlayerPet.UpdateEveryTick();
        player.PlayerPet.UpdateEveryTick();

        Assert.AreEqual(PlayerPet.MaxQueuedActions, carriedOut, "The rest were dropped, not kept for later ticks.");
    }

    [TestMethod]
    public void ActionsQueuedDuringATick_WaitForTheNextOne()
    {
        var player = NewPlayer();
        var carriedOut = 0;

        // Upstream's tick emptied the queue until it stayed empty, so this never returned.
        void QueueAgain()
        {
            carriedOut++;
            player.PlayerPet.QueueAction(QueueAgain);
        }

        player.PlayerPet.QueueAction(QueueAgain);

        player.PlayerPet.UpdateEveryTick();
        Assert.AreEqual(1, carriedOut);

        player.PlayerPet.UpdateEveryTick();
        Assert.AreEqual(2, carriedOut);
    }

    /// <summary>
    /// The zone goes on ticking a player who has logged out until it removes them, and a pet summoned then (a restore
    /// queued by a zone load, or a summon waiting its turn) was left in the zone with nothing to remove it.
    /// </summary>
    [TestMethod]
    public void AfterLogout_NothingIsCarriedOut()
    {
        var player = NewPlayer();
        var carriedOut = 0;

        player.PlayerPet.QueueAction(() => carriedOut++);
        player.PlayerPet.Dispose();

        player.PlayerPet.UpdateEveryTick();

        Assert.AreEqual(0, carriedOut);
    }
}
