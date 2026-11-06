using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Options;

using Sanctuary.Core.Helpers;
using Sanctuary.Database.Entities;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Interactions;
using Sanctuary.Game.Quests;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Game.Housing;

public enum HouseDoorResult
{
    /// <summary>The door isn't in the player's zone, or is too far to click: nothing happened.</summary>
    OutOfReach,

    /// <summary>The player was too far to go in, so the client walks them up to the door and clicks it again.</summary>
    WalkingUp,

    /// <summary>The door answered the player's last click too recently (<see cref="HouseDoors.MinUseIntervalMs"/>): nothing happened.</summary>
    TooSoon,

    /// <summary>The account has no house to go into (no starter house is given, or giving it failed). The player was told.</summary>
    NoHouse,

    /// <summary>The player is on their way into the account's one house, or is told why they can't go in.</summary>
    GoingIn,

    /// <summary>The account has several houses: the door's menu offers them, and a pick goes into that one.</summary>
    Menu
}

/// <summary>
/// Evergrove: a Housing door (<see cref="HouseDoors"/>) takes whoever clicks it into their account's own house
/// (<c>docs/design/evergrove-housing.md</c>, section 10). It never shows the door's quest buttons: a quest step at the
/// door is completed just before the move into the house (<see cref="IQuestManager.AdvanceTalkStepsAt"/>), so only a
/// player who really goes in completes it.
/// </summary>
public sealed class HouseDoorService
{
    private const string NoHouseFreeLots =
        "Your account has no house yet. Open the Marketplace on the game dock: the lots under Housing are free.";

    private const string NoHouse = "Your account has no house yet. Open the Marketplace on the game dock to find a lot under Housing.";

    private readonly IHouseManager _houseManager;
    private readonly IQuestManager _questManager;
    private readonly IResourceManager _resourceManager;
    private readonly HousingOptions _housingOptions;

    public HouseDoorService(IHouseManager houseManager, IQuestManager questManager, IResourceManager resourceManager,
        IOptions<HousingOptions> housingOptions)
    {
        _houseManager = houseManager;
        _questManager = questManager;
        _resourceManager = resourceManager;
        _housingOptions = housingOptions.Value;
    }

    /// <summary>
    /// The player clicked the door. From further than <see cref="HouseDoors.WalkInRange"/> the client walks them up to it
    /// first. A click within <see cref="HouseDoors.MinUseIntervalMs"/> of the last one answered does nothing. An account
    /// without its starter house is given it here, as at login, in case that failed, and <paramref name="sendHouseList"/>
    /// tells the client, as My Houses doesn't ask. Then one house is gone into straight away, and several are offered on
    /// the door's menu (<see cref="HouseDoors.Order"/>).
    /// </summary>
    /// <param name="now">
    /// <see cref="Environment.TickCount64"/>, for the limit on the door's clicks and on moves to another zone
    /// (<see cref="ZoneTeleports"/>).
    /// </param>
    public HouseDoorResult Use(Player player, Npc door, Action sendHouseList, long now)
    {
        if (!InteractionMenuHelper.CanInteract(door, player))
            return HouseDoorResult.OutOfReach;

        if (!Positions.IsWithinRange(player.Position, door.Position, HouseDoors.WalkInRange))
        {
            player.SendTunneled(new CommandPacketMoveAndInteract
            {
                Position = door.Position,
                Guid = door.Guid
            });

            return HouseDoorResult.WalkingUp;
        }

        // What follows reads the account's houses from the database, on the main loop, so a click too soon after the
        // last one answered does nothing (a walk-up sends only the packet above).
        if (now < player.NextHouseDoorAt)
            return HouseDoorResult.TooSoon;

        player.NextHouseDoorAt = now + HouseDoors.MinUseIntervalMs;

        if (_houseManager.EnsureStarterHouse(player) == StarterHouseResult.Granted)
            sendHouseList();

        var houses = HouseDoors.Order(_houseManager.GetOwnedHouses(GuidHelper.GetPlayerId(player.Guid)));

        if (houses.Count == 0)
        {
            ChatHelper.SendSystemMessage(player, _housingOptions.FreeLots ? NoHouseFreeLots : NoHouse);
            return HouseDoorResult.NoHouse;
        }

        if (houses.Count == 1)
        {
            GoInto(player, door, houses[0].Id, now);
            return HouseDoorResult.GoingIn;
        }

        // The pick is checked again as it comes back: the door must still be in reach (CommandPacketInteractionSelect).
        player.SendInteractionMenu(door, MenuFor(door, houses));
        return HouseDoorResult.Menu;
    }

    /// <summary>
    /// The door's menu: a button for each house, in the order given, showing its lot's icon and name. A pick goes into
    /// that house.
    /// </summary>
    internal List<NpcInteractionOption> MenuFor(Npc door, IEnumerable<DbHouse> houses) =>
        houses
            .Select(house => (house.Id, Lot: _resourceManager.Zones.TryGetValue(house.ZoneDefinitionId, out var definition)
                ? definition as HousingZoneDefinition
                : null))
            .Where(entry => entry.Lot is not null)
            .Select(entry => new NpcInteractionOption
            {
                IconId = entry.Lot!.IconId,
                ButtonTextId = entry.Lot.NameId,
                Invoke = picker => GoInto(picker, door, entry.Id, Environment.TickCount64)
            })
            .ToList();

    private void GoInto(Player player, Npc door, ulong houseId, long now)
    {
        var houseGuid = GuidHelper.GetHouseGuid(houseId);

        // A move to another zone: one that comes too soon after the last waits for its turn (ZoneTeleports), and the
        // latest replaces it. The door's quest step is completed as the move happens, once the entry has passed every
        // check, so repeated clicks or a refused entry complete nothing.
        ZoneTeleports.Request(player, now, () =>
        {
            var result = _houseManager.EnterHouse(player, houseGuid, () => _questManager.AdvanceTalkStepsAt(player, door));

            if (HouseEntryMessages.For(result) is { } message)
                ChatHelper.SendSystemMessage(player, message);
        });
    }
}
