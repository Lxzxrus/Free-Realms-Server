using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A player's pet requests that query the database or are shown to the players nearby (rename, equip, utility, favourite
/// trick, feed and groom) are spaced <see cref="PetHelper.RequestIntervalMs"/> apart, the way path requests are, so a
/// client can't keep the gateway's packet thread busy with them (<see cref="PetHelper.TryStartRequest"/>). One that comes
/// sooner waits its turn rather than being dropped, so a quick click in the pet window still gets its reply
/// (<see cref="PetHelper.StartWaitingRequest"/>).
/// </summary>
[TestClass]
public sealed class PetRequestThrottleTests
{
    private static Player NewPlayer() => new(null!, null!, null!, null!, null!);

    private static readonly Action Nothing = () => { };

    [TestMethod]
    public void TheFirstRequest_IsAllowed()
    {
        Assert.IsTrue(PetHelper.TryStartRequest(NewPlayer(), Nothing));
    }

    [TestMethod]
    public void ARequestRightAfterAnother_IsRefused()
    {
        var player = NewPlayer();

        Assert.IsTrue(PetHelper.TryStartRequest(player, Nothing));
        Assert.IsFalse(PetHelper.TryStartRequest(player, Nothing));
    }

    [TestMethod]
    public void AnAllowedRequest_MakesTheNextWait()
    {
        var player = NewPlayer();
        var before = Environment.TickCount64;

        Assert.IsTrue(PetHelper.TryStartRequest(player, Nothing));

        Assert.IsTrue(player.NextPetRequestAt >= before + PetHelper.RequestIntervalMs);
        Assert.IsTrue(player.NextPetRequestAt <= Environment.TickCount64 + PetHelper.RequestIntervalMs);
    }

    [TestMethod]
    public void ARefusedRequest_DoesNotMakeTheWaitLonger()
    {
        var player = NewPlayer();

        Assert.IsTrue(PetHelper.TryStartRequest(player, Nothing));

        var nextRequestAt = player.NextPetRequestAt;

        Assert.IsFalse(PetHelper.TryStartRequest(player, Nothing));
        Assert.AreEqual(nextRequestAt, player.NextPetRequestAt);
    }

    [TestMethod]
    public void ARequestAfterTheWait_IsAllowed()
    {
        var player = NewPlayer();

        Assert.IsTrue(PetHelper.TryStartRequest(player, Nothing));

        // As if the wait had passed.
        player.NextPetRequestAt = Environment.TickCount64;

        Assert.IsTrue(PetHelper.TryStartRequest(player, Nothing));
    }

    [TestMethod]
    public void ARequestTooSoon_IsCarriedOutWhenItsTurnComes()
    {
        var player = NewPlayer();
        var carriedOut = false;

        Assert.IsTrue(PetHelper.TryStartRequest(player, Nothing));
        Assert.IsFalse(PetHelper.TryStartRequest(player, () => carriedOut = true));

        PetHelper.StartWaitingRequest(player, player.NextPetRequestAt - 1);
        Assert.IsFalse(carriedOut, "Its turn hasn't come yet.");

        var turn = player.NextPetRequestAt;

        PetHelper.StartWaitingRequest(player, turn);
        Assert.IsTrue(carriedOut);
        Assert.AreEqual(turn + PetHelper.RequestIntervalMs, player.NextPetRequestAt, "The next waits for it in turn.");
    }

    [TestMethod]
    public void WaitingRequests_AreCarriedOutInOrder_OneATurn()
    {
        var player = NewPlayer();
        var carriedOut = new List<string>();

        Assert.IsTrue(PetHelper.TryStartRequest(player, Nothing));
        Assert.IsFalse(PetHelper.TryStartRequest(player, () => carriedOut.Add("hat")));
        Assert.IsFalse(PetHelper.TryStartRequest(player, () => carriedOut.Add("collar")));

        PetHelper.StartWaitingRequest(player, player.NextPetRequestAt);
        CollectionAssert.AreEqual(new[] { "hat" }, carriedOut);

        PetHelper.StartWaitingRequest(player, player.NextPetRequestAt);
        CollectionAssert.AreEqual(new[] { "hat", "collar" }, carriedOut);
    }

    [TestMethod]
    public void ARequestAfterTheWait_StillWaitsBehindOnesAlreadyWaiting()
    {
        var player = NewPlayer();
        var carriedOut = new List<string>();

        Assert.IsTrue(PetHelper.TryStartRequest(player, Nothing));
        Assert.IsFalse(PetHelper.TryStartRequest(player, () => carriedOut.Add("first")));

        // The wait has passed, but the first is still waiting: the second must not overtake it.
        player.NextPetRequestAt = Environment.TickCount64;

        Assert.IsFalse(PetHelper.TryStartRequest(player, () => carriedOut.Add("second")));

        PetHelper.StartWaitingRequest(player, player.NextPetRequestAt);
        PetHelper.StartWaitingRequest(player, player.NextPetRequestAt);
        CollectionAssert.AreEqual(new[] { "first", "second" }, carriedOut);
    }

    [TestMethod]
    public void AFlood_WaitsNoMoreThanTheCap()
    {
        var player = NewPlayer();
        var carriedOut = 0;

        Assert.IsTrue(PetHelper.TryStartRequest(player, Nothing));

        for (var i = 0; i < 10_000; i++)
            PetHelper.TryStartRequest(player, () => carriedOut++);

        Assert.AreEqual(PetHelper.MaxWaitingRequests, player.WaitingPetRequests.Count);

        // A turn each, however long the client keeps sending.
        for (var turn = 0; turn < PetHelper.MaxWaitingRequests + 5; turn++)
            PetHelper.StartWaitingRequest(player, player.NextPetRequestAt);

        Assert.AreEqual(PetHelper.MaxWaitingRequests, carriedOut);
    }

    [TestMethod]
    public void NothingWaiting_CarriesOutNothing_AndLeavesTheWait()
    {
        var player = NewPlayer();

        Assert.IsTrue(PetHelper.TryStartRequest(player, Nothing));

        var nextRequestAt = player.NextPetRequestAt;

        PetHelper.StartWaitingRequest(player, nextRequestAt + 5000);

        Assert.AreEqual(nextRequestAt, player.NextPetRequestAt);
    }
}
