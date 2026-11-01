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
/// A character may have <see cref="BoomboxHelper.MaxBoomboxesPerCharacter"/> boomboxes playing in a zone and a zone
/// <see cref="BoomboxHelper.MaxBoomboxesPerZone"/> players' boomboxes in all (<see cref="BoomboxHelper.CanPlace"/>). The
/// counts are kept with the zone, by character, so logging out and back in doesn't reset them the way it reset the
/// boombox's use cooldown.
/// </summary>
[TestClass]
public sealed class BoomboxLimitTests
{
    private const ulong Ariel = 0x11;
    private const ulong Nate = 0x21;
    private const ulong ManyPlayers = 0x1001;
    private const ulong Scripted = 0;

    private static long _lastNpcGuid = 100_000_000_000;

    /// <summary>A zone for the dance bookkeeping, which only keeps it by the zone; nothing on it is called.</summary>
    public class StandInZone : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }

    private static IZone NewZone() => DispatchProxy.Create<IZone, StandInZone>();

    private static void Place(IZone zone, ulong ownerGuid, int durationMs = 180_000, bool permanent = false)
    {
        var boombox = new Npc(zone) { Guid = (ulong)Interlocked.Increment(ref _lastNpcGuid) };
        var definition = new BoomboxDefinition { DurationMs = durationMs };

        BoomboxHelper.StartDanceLoop(zone, boombox, Vector4.Zero, definition, 0, 0, 0, 0, permanent, ownerGuid);
    }

    [TestMethod]
    public void AnEmptyZone_TakesABoombox()
    {
        Assert.IsTrue(BoomboxHelper.CanPlace(NewZone(), Ariel));
    }

    [TestMethod]
    public void ACharacter_StopsAtTheirLimit_AndOthersMayStillPlace()
    {
        var zone = NewZone();

        for (var i = 0; i < BoomboxHelper.MaxBoomboxesPerCharacter; i++)
        {
            Assert.IsTrue(BoomboxHelper.CanPlace(zone, Ariel));
            Place(zone, Ariel);
        }

        // The count is the zone's, by character guid, so it is the same after a relog.
        Assert.IsFalse(BoomboxHelper.CanPlace(zone, Ariel));
        Assert.IsTrue(BoomboxHelper.CanPlace(zone, Nate));
    }

    [TestMethod]
    public void AZone_StopsAtItsLimit()
    {
        var zone = NewZone();

        // Each of many players puts out as many as they may.
        for (var i = 0; i < BoomboxHelper.MaxBoomboxesPerZone; i++)
            Place(zone, ManyPlayers + (ulong)(i / BoomboxHelper.MaxBoomboxesPerCharacter) * 0x10);

        // Ariel has none out, but the zone is full.
        Assert.IsFalse(BoomboxHelper.CanPlace(zone, Ariel));
    }

    [TestMethod]
    public void AnotherZonesBoomboxes_DontCount()
    {
        var zone = NewZone();

        for (var i = 0; i < BoomboxHelper.MaxBoomboxesPerCharacter; i++)
            Place(zone, Ariel);

        Assert.IsTrue(BoomboxHelper.CanPlace(NewZone(), Ariel));
    }

    [TestMethod]
    public void ScriptedBoomboxes_DontCount()
    {
        var zone = NewZone();

        for (var i = 0; i < BoomboxHelper.MaxBoomboxesPerZone; i++)
            Place(zone, Scripted, permanent: true);

        Assert.IsTrue(BoomboxHelper.CanPlace(zone, Ariel));
    }

    [TestMethod]
    public void BoomboxesThatHaveStopped_DontCount()
    {
        var zone = NewZone();

        for (var i = 0; i < BoomboxHelper.MaxBoomboxesPerZone; i++)
            Place(zone, Ariel, durationMs: 0);

        Assert.IsTrue(BoomboxHelper.CanPlace(zone, Ariel));
    }
}
