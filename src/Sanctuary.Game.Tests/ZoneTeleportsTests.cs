using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class ZoneTeleportsTests
{
    private const long Start = 10_000;

    // Neither a Player nor a zone can be built in a test. The limit needs only the player's zone, compared by
    // reference, and its times.
    private static Player NewPlayer(IZone zone)
    {
        var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
        player.Zone = zone;
        return player;
    }

    private static IZone NewWorld() => (IZone)RuntimeHelpers.GetUninitializedObject(typeof(WorldZone));

    private static IZone NewHouse() => (IZone)RuntimeHelpers.GetUninitializedObject(typeof(HousingZone));

    [TestMethod]
    public void AMoveTooSoonAfterTheLast_WaitsForItsTurn()
    {
        var world = NewWorld();
        var house = NewHouse();
        var player = NewPlayer(world);

        ZoneTeleports.Request(player, Start, () => player.Zone = house);

        Assert.AreSame(house, player.Zone, "The first move is made at once.");

        ZoneTeleports.Request(player, Start + 1, () => player.Zone = world);

        Assert.AreSame(house, player.Zone, "Out again at once: that waits, as the client does.");

        ZoneTeleports.Tick(player, Start + ZoneTeleports.MinIntervalMs - 1);

        Assert.AreSame(house, player.Zone);

        ZoneTeleports.Tick(player, Start + ZoneTeleports.MinIntervalMs);

        Assert.AreSame(world, player.Zone, "Made when its turn comes.");
        Assert.IsNull(player.WaitingZoneMove);
    }

    [TestMethod]
    public void OnlyTheLatestMoveWaits()
    {
        var player = NewPlayer(NewWorld());
        var house = NewHouse();
        var otherHouse = NewHouse();
        var made = new List<string>();

        ZoneTeleports.Request(player, Start, () => player.Zone = house);
        ZoneTeleports.Request(player, Start + 100, () => made.Add("world"));
        ZoneTeleports.Request(player, Start + 200, () =>
        {
            made.Add("other house");
            player.Zone = otherHouse;
        });

        ZoneTeleports.Tick(player, Start + ZoneTeleports.MinIntervalMs);
        ZoneTeleports.Tick(player, Start + 10 * ZoneTeleports.MinIntervalMs);

        CollectionAssert.AreEqual(new[] { "other house" }, made, "One move waits, the latest, and it is made once.");
        Assert.AreSame(otherHouse, player.Zone);
    }

    [TestMethod]
    public void AMoveThatWaited_StartsTheNextWait()
    {
        var world = NewWorld();
        var house = NewHouse();
        var player = NewPlayer(world);
        var madeAt = Start + ZoneTeleports.MinIntervalMs + 40;

        ZoneTeleports.Request(player, Start, () => player.Zone = house);
        ZoneTeleports.Request(player, Start + 1, () => player.Zone = world);
        ZoneTeleports.Tick(player, madeAt);
        ZoneTeleports.Request(player, madeAt + 1, () => player.Zone = house);

        Assert.AreSame(world, player.Zone, "Back into the house waits from when the move out was made.");

        ZoneTeleports.Tick(player, madeAt + ZoneTeleports.MinIntervalMs);

        Assert.AreSame(house, player.Zone);
    }

    [TestMethod]
    public void AMoveAskedForOnceItsTurnHasCome_IsMadeAtOnce_InPlaceOfTheOneWaiting()
    {
        var world = NewWorld();
        var house = NewHouse();
        var player = NewPlayer(world);
        var made = new List<string>();

        ZoneTeleports.Request(player, Start, () => player.Zone = house);
        ZoneTeleports.Request(player, Start + 1, () => made.Add("waiting"));

        // Its turn has come, but the main loop hasn't made it yet.
        ZoneTeleports.Request(player, Start + ZoneTeleports.MinIntervalMs, () =>
        {
            made.Add("latest");
            player.Zone = world;
        });
        ZoneTeleports.Tick(player, Start + 10 * ZoneTeleports.MinIntervalMs);

        CollectionAssert.AreEqual(new[] { "latest" }, made);
        Assert.AreSame(world, player.Zone);
    }
}
