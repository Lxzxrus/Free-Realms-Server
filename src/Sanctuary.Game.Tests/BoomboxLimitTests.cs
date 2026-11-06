using System;
using System.Numerics;
using System.Reflection;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources.Definitions;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// An account's characters may have <see cref="BoomboxHelper.MaxBoomboxesPerAccount"/> boomboxes playing in a zone and a
/// zone <see cref="BoomboxHelper.MaxBoomboxesPerZone"/> players' boomboxes in all (<see cref="BoomboxHelper.CanPlace"/>).
/// The counts are kept with the zone, by account, so neither logging out and back in nor switching to another character
/// resets them the way a relog reset the boombox's use cooldown.
/// </summary>
[TestClass]
public sealed class BoomboxLimitTests
{
    private const ulong ArielsAccount = 1;
    private const ulong NatesAccount = 2;
    private const ulong FirstOfManyAccounts = 100;

    private static long _lastGuid = 100_000_000_000;

    /// <summary>A zone for the dance bookkeeping, which only keeps it by the zone; nothing on it is called.</summary>
    public class StandInZone : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }

    private static IZone NewZone() => DispatchProxy.Create<IZone, StandInZone>();

    private static ulong NewGuid() => (ulong)Interlocked.Increment(ref _lastGuid);

    /// <summary>One of the account's characters, logged in to the zone.</summary>
    private static Player Character(IZone zone, ulong userId) =>
        new(null!, null!, null!, null!, null!) { Guid = NewGuid(), UserId = userId, Zone = zone };

    private static void Place(Player owner, int durationMs = 180_000)
    {
        var boombox = new Npc(owner.Zone) { Guid = NewGuid() };
        var definition = new BoomboxDefinition { DurationMs = durationMs };

        BoomboxHelper.StartDanceLoop(owner.Zone, boombox, Vector4.Zero, definition, 0, 0, 0, 0, owner: owner);
    }

    private static void PlaceScripted(IZone zone)
    {
        var boombox = new Npc(zone) { Guid = NewGuid() };

        BoomboxHelper.StartDanceLoop(zone, boombox, Vector4.Zero, new BoomboxDefinition(), 0, 0, 0, 0, permanent: true);
    }

    [TestMethod]
    public void AnEmptyZone_TakesABoombox()
    {
        Assert.IsTrue(BoomboxHelper.CanPlace(Character(NewZone(), ArielsAccount)));
    }

    [TestMethod]
    public void AnAccount_StopsAtItsLimit_AndOthersMayStillPlace()
    {
        var zone = NewZone();
        var ariel = Character(zone, ArielsAccount);

        for (var i = 0; i < BoomboxHelper.MaxBoomboxesPerAccount; i++)
        {
            Assert.IsTrue(BoomboxHelper.CanPlace(ariel));
            Place(ariel);
        }

        Assert.IsFalse(BoomboxHelper.CanPlace(ariel));
        Assert.IsTrue(BoomboxHelper.CanPlace(Character(zone, NatesAccount)));
    }

    /// <summary>An account has ten character slots, so a limit per character let one account fill a zone.</summary>
    [TestMethod]
    public void AnotherCharacterOnTheAccount_SharesItsLimit()
    {
        var zone = NewZone();

        for (var i = 0; i < BoomboxHelper.MaxBoomboxesPerAccount; i++)
            Place(Character(zone, ArielsAccount));

        Assert.IsFalse(BoomboxHelper.CanPlace(Character(zone, ArielsAccount)));
    }

    [TestMethod]
    public void AZone_StopsAtItsLimit()
    {
        var zone = NewZone();

        // Each of many accounts puts out as many as it may.
        for (var i = 0; i < BoomboxHelper.MaxBoomboxesPerZone; i++)
            Place(Character(zone, FirstOfManyAccounts + (ulong)(i / BoomboxHelper.MaxBoomboxesPerAccount)));

        // Ariel has none out, but the zone is full.
        Assert.IsFalse(BoomboxHelper.CanPlace(Character(zone, ArielsAccount)));
    }

    [TestMethod]
    public void AnotherZonesBoomboxes_DontCount()
    {
        var zone = NewZone();

        for (var i = 0; i < BoomboxHelper.MaxBoomboxesPerAccount; i++)
            Place(Character(zone, ArielsAccount));

        Assert.IsTrue(BoomboxHelper.CanPlace(Character(NewZone(), ArielsAccount)));
    }

    [TestMethod]
    public void ScriptedBoomboxes_DontCount()
    {
        var zone = NewZone();

        for (var i = 0; i < BoomboxHelper.MaxBoomboxesPerZone; i++)
            PlaceScripted(zone);

        Assert.IsTrue(BoomboxHelper.CanPlace(Character(zone, ArielsAccount)));
    }

    [TestMethod]
    public void BoomboxesThatHaveStopped_DontCount()
    {
        var zone = NewZone();
        var ariel = Character(zone, ArielsAccount);

        for (var i = 0; i < BoomboxHelper.MaxBoomboxesPerZone; i++)
            Place(ariel, durationMs: 0);

        Assert.IsTrue(BoomboxHelper.CanPlace(ariel));
    }
}
