using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A player's pet requests that query the database or are shown to the players nearby (rename, equip, utility, favourite
/// trick, feed and groom) are spaced <see cref="PetHelper.RequestIntervalMs"/> apart, the way path requests are, so a
/// client can't keep the gateway's packet thread busy with them (<see cref="PetHelper.TryStartRequest"/>).
/// </summary>
[TestClass]
public sealed class PetRequestThrottleTests
{
    private static Player NewPlayer() => new(null!, null!, null!, null!, null!);

    [TestMethod]
    public void TheFirstRequest_IsAllowed()
    {
        Assert.IsTrue(PetHelper.TryStartRequest(NewPlayer()));
    }

    [TestMethod]
    public void ARequestRightAfterAnother_IsRefused()
    {
        var player = NewPlayer();

        Assert.IsTrue(PetHelper.TryStartRequest(player));
        Assert.IsFalse(PetHelper.TryStartRequest(player));
    }

    [TestMethod]
    public void AnAllowedRequest_MakesTheNextWait()
    {
        var player = NewPlayer();
        var before = Environment.TickCount64;

        Assert.IsTrue(PetHelper.TryStartRequest(player));

        Assert.IsTrue(player.NextPetRequestAt >= before + PetHelper.RequestIntervalMs);
        Assert.IsTrue(player.NextPetRequestAt <= Environment.TickCount64 + PetHelper.RequestIntervalMs);
    }

    [TestMethod]
    public void ARefusedRequest_DoesNotMakeTheWaitLonger()
    {
        var player = NewPlayer();

        Assert.IsTrue(PetHelper.TryStartRequest(player));

        var nextRequestAt = player.NextPetRequestAt;

        Assert.IsFalse(PetHelper.TryStartRequest(player));
        Assert.AreEqual(nextRequestAt, player.NextPetRequestAt);
    }

    [TestMethod]
    public void ARequestAfterTheWait_IsAllowed()
    {
        var player = NewPlayer();

        Assert.IsTrue(PetHelper.TryStartRequest(player));

        // As if the wait had passed.
        player.NextPetRequestAt = Environment.TickCount64;

        Assert.IsTrue(PetHelper.TryStartRequest(player));
    }
}
