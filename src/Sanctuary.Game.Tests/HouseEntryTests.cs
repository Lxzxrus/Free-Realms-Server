using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Tests;

/// <summary>
/// Going into a house with something to do on the way, as the Housing door completes its quest step
/// (<see cref="IHouseManager.EnterHouse"/>'s <c>beforeTeleport</c>), on the real house manager: it happens only once the
/// entry has passed every check, and before the player is moved.
/// </summary>
[TestClass]
public sealed class HouseEntryTests
{
    private const ulong Character = QuestWorld.CharacterId;
    private const ulong Stranger = 20;
    private const int UnservedLot = 4242;

    private HouseWorld _world = null!;
    private int _calls;

    [TestInitialize]
    public void Setup() => _world = new HouseWorld();

    [TestCleanup]
    public void Cleanup() => _world.Dispose();

    private Player PlayerInTheWorld() => _world.PlayerInTheWorld(Character, _world.Account);

    private EnterHouseResult Enter(Player player, ulong houseGuid) => _world.Houses.EnterHouse(player, houseGuid, () => _calls++);

    [TestMethod]
    public void ARealEntry_RunsIt_JustBeforeTheMove()
    {
        var apartment = _world.AddHouse(Character, HouseWorld.Apartment);
        var player = PlayerInTheWorld();

        var callsAtTheMove = -1;
        _world.OnMove = () => callsAtTheMove = _calls;

        Assert.AreEqual(EnterHouseResult.Success, Enter(player, GuidHelper.GetHouseGuid(apartment)));

        Assert.AreEqual(1, _calls);
        Assert.AreEqual(1, callsAtTheMove, "It ran before the player was moved, so its packets go out first.");
        CollectionAssert.AreEqual(new[] { (HouseWorld.Apartment, (ulong?)Character) }, _world.Moves);
        Assert.IsInstanceOfType<HousingZone>(player.Zone);
    }

    [TestMethod]
    public void AnUnknownHouse_IsRefused_AndRunsNothing()
    {
        var player = PlayerInTheWorld();

        Assert.AreEqual(EnterHouseResult.HouseNotFound, Enter(player, GuidHelper.GetHouseGuid(424242)));
        Assert.AreEqual(EnterHouseResult.HouseNotFound, Enter(player, GuidHelper.GetPlayerGuid(Character)), "Not a house's GUID.");

        Assert.AreEqual(0, _calls);
        Assert.AreEqual(0, _world.Moves.Count);
    }

    [TestMethod]
    public void AHouseOnALotTheServerDoesNotServe_IsRefused_AndRunsNothing()
    {
        var house = _world.AddHouse(Character, UnservedLot);

        Assert.AreEqual(EnterHouseResult.HouseNotFound, Enter(PlayerInTheWorld(), GuidHelper.GetHouseGuid(house)));

        Assert.AreEqual(0, _calls);
        Assert.AreEqual(0, _world.Moves.Count);
    }

    [TestMethod]
    public void AnotherAccountsLockedHouse_IsRefused_AndRunsNothing()
    {
        var neighbour = _world.AddAccount();
        _world.AddCharacter(neighbour, Stranger);
        var locked = _world.AddHouse(Stranger, HouseWorld.Seaside, house => house.IsLocked = true);

        Assert.AreEqual(EnterHouseResult.NotAuthorized, Enter(PlayerInTheWorld(), GuidHelper.GetHouseGuid(locked)));

        Assert.AreEqual(0, _calls);
        Assert.AreEqual(0, _world.Moves.Count);
    }

    /// <summary>The quest world's player stands in a zone that is neither the world nor a house.</summary>
    [TestMethod]
    public void APlayerOutsideTheWorld_IsRefused_AndRunsNothing()
    {
        var apartment = _world.AddHouse(Character, HouseWorld.Apartment);

        Assert.AreEqual(EnterHouseResult.UnsupportedSourceZone, Enter(_world.Player, GuidHelper.GetHouseGuid(apartment)));

        Assert.AreEqual(0, _calls);
    }

    /// <summary>
    /// The one case where it runs without the player going in: every check passed, and the move itself failed, which is
    /// rare (the house instance couldn't be made). The Housing door's quest step then stays completed.
    /// </summary>
    [TestMethod]
    public void AMoveThatFailsAfterTheChecks_HasRunIt()
    {
        var apartment = _world.AddHouse(Character, HouseWorld.Apartment);
        var player = PlayerInTheWorld();
        _world.MovesFail = true;

        Assert.AreEqual(EnterHouseResult.TransferFailed, Enter(player, GuidHelper.GetHouseGuid(apartment)));

        Assert.AreEqual(1, _calls);
        Assert.AreSame(_world.World, player.Zone);
    }

    /// <summary>Without anything to do, an entry is what it was: the visit is still saved.</summary>
    [TestMethod]
    public void AnEntryWithNothingToDo_StillGoesIn()
    {
        var apartment = _world.AddHouse(Character, HouseWorld.Apartment, house => house.LastVisited = System.DateTimeOffset.UnixEpoch);
        var player = PlayerInTheWorld();

        Assert.AreEqual(EnterHouseResult.Success, _world.Houses.EnterHouse(player, GuidHelper.GetHouseGuid(apartment)));

        Assert.IsInstanceOfType<HousingZone>(player.Zone);

        using var dbContext = _world.Quests.CreateDbContext();
        Assert.IsTrue(dbContext.Houses.AsNoTracking().Single(house => house.Id == apartment).LastVisited > System.DateTimeOffset.UnixEpoch);
    }
}
