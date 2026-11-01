using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Interactions;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Every snowball pickup is shown to everyone near the player, so one within a second of the player's last is ignored
/// (SnowballInteraction.TryStartPickup). The snowball the last one gave lasts a minute, so a person loses nothing.
/// </summary>
[TestClass]
public sealed class SnowballPickupTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 20, 12, 0, 0, TimeSpan.Zero);

    private static Player NewPlayer() => new(null!, null!, null!, null!, null!);

    [TestMethod]
    public void FirstPickup_GoesAhead()
    {
        Assert.IsTrue(SnowballInteraction.TryStartPickup(NewPlayer(), Now));
    }

    [TestMethod]
    public void PickupWithinASecondOfTheLast_IsIgnored()
    {
        var player = NewPlayer();

        Assert.IsTrue(SnowballInteraction.TryStartPickup(player, Now));
        Assert.IsFalse(SnowballInteraction.TryStartPickup(player, Now));
        Assert.IsFalse(SnowballInteraction.TryStartPickup(player, Now + TimeSpan.FromMilliseconds(999)));

        // An ignored pickup doesn't push the next one back.
        Assert.IsTrue(SnowballInteraction.TryStartPickup(player, Now + TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void PickupsASecondApart_AllGoAhead()
    {
        var player = NewPlayer();

        for (var second = 0; second < 5; second++)
            Assert.IsTrue(SnowballInteraction.TryStartPickup(player, Now + TimeSpan.FromSeconds(second)));
    }

    [TestMethod]
    public void EachPlayer_HasTheirOwnWait()
    {
        var player = NewPlayer();
        var otherPlayer = NewPlayer();

        Assert.IsTrue(SnowballInteraction.TryStartPickup(player, Now));
        Assert.IsTrue(SnowballInteraction.TryStartPickup(otherPlayer, Now));
    }
}
