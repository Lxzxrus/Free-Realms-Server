using System;
using System.Runtime.CompilerServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class ZoneTeleportsTests
{
    [TestMethod]
    public void TryStart_RefusesAnotherZoneTeleportUntilTheIntervalHasPassed()
    {
        // A Player can't be built in a test; the limit needs only its time.
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));

        Assert.IsTrue(ZoneTeleports.TryStart(player));
        Assert.IsFalse(ZoneTeleports.TryStart(player), "A real client is still loading the zone it asked for.");

        player.NextZoneTeleportAt = Environment.TickCount64;

        Assert.IsTrue(ZoneTeleports.TryStart(player), "The interval has passed.");
        Assert.IsFalse(ZoneTeleports.TryStart(player));
    }
}
