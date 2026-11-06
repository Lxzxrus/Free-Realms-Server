using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A player's pets find treasure at most once per cooldown, whichever pet is out: cheap coin-store pets must not be
/// cycled for chests (PetHelper.IsTreasureOnCooldown).
/// </summary>
[TestClass]
public sealed class PetTreasureCooldownTests
{
    private const int FirstPet = 1;
    private const int SecondPet = 2;

    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void NoTreasureYet_PetsMayDig()
    {
        Assert.IsFalse(PetHelper.IsTreasureOnCooldown(new Dictionary<int, DateTimeOffset>(), Now));
    }

    [TestMethod]
    public void SecondPet_CantDig_Within8HoursOfTheFirstPetsTreasure()
    {
        var treasureFoundAt = new Dictionary<int, DateTimeOffset> { [FirstPet] = Now - TimeSpan.FromHours(1) };

        // The second pet has never found anything, yet it rests with the first.
        Assert.IsFalse(treasureFoundAt.ContainsKey(SecondPet));
        Assert.IsTrue(PetHelper.IsTreasureOnCooldown(treasureFoundAt, Now));
        Assert.IsTrue(PetHelper.IsTreasureOnCooldown(treasureFoundAt, Now + TimeSpan.FromHours(6.9)));
    }

    [TestMethod]
    public void AnyPet_MayDigAgain_8HoursAfterThePlayersLastTreasure()
    {
        var treasureFoundAt = new Dictionary<int, DateTimeOffset>
        {
            [FirstPet] = Now - TimeSpan.FromHours(20),
            [SecondPet] = Now - TimeSpan.FromHours(8) - TimeSpan.FromSeconds(1)
        };

        Assert.IsFalse(PetHelper.IsTreasureOnCooldown(treasureFoundAt, Now));

        // The latest find counts, whichever pet made it.
        treasureFoundAt[SecondPet] = Now - TimeSpan.FromHours(7);
        Assert.IsTrue(PetHelper.IsTreasureOnCooldown(treasureFoundAt, Now));
    }
}
