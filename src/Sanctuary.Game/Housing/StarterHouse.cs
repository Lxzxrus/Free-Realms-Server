using System.Collections.Generic;
using System.Linq;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources;
using Sanctuary.Game.Resources.Definitions.Zones;

namespace Sanctuary.Game.Housing;

public enum StarterHouseResult
{
    /// <summary>The account had no starter house, and the player's character was just given one.</summary>
    Granted,

    /// <summary>A character on the account has one already.</summary>
    AlreadyOwned,

    /// <summary>No account is given one (<see cref="HousingOptions.StarterHouseZoneId"/>).</summary>
    Off,

    /// <summary>The database refused the house or couldn't be reached. The next login tries again.</summary>
    Failed
}

/// <summary>
/// Evergrove: the free house every account owns, the Apartment unless <see cref="HousingOptions.StarterHouseZoneId"/>
/// says otherwise (<c>docs/design/evergrove-housing.md</c>, section 11). <see cref="IHouseManager.EnsureStarterHouse"/>
/// gives it. It's a bare house, worth nothing, so nothing records that an account was given one: owning it is the record.
/// </summary>
public static class StarterHouse
{
    /// <summary>
    /// The lot every account is given, or <c>null</c> while that's off: the setting is 0, or names no housing lot the
    /// server serves (zone 1 is the world, for one).
    /// </summary>
    public static HousingZoneDefinition? Lot(HousingOptions options, ZoneDefinitionCollection zones) =>
        options.StarterHouseZoneId != 0 &&
        zones.TryGetValue(options.StarterHouseZoneId, out var definition) &&
        definition is HousingZoneDefinition lot
            ? lot
            : null;

    /// <summary>
    /// The Marketplace bundle the store leaves off its shelf, and refuses an order for at no charge: the starter house's,
    /// while every account is given it, as an order for it could only fail. <c>null</c> while no account is given one,
    /// when the lot is back on the shelf like any other.
    /// </summary>
    public static int? BundleLeftOffTheShelf(HousingOptions options, ZoneDefinitionCollection zones) =>
        Lot(options, zones) is { StoreBundleId: not 0 } lot ? lot.StoreBundleId : null;

    /// <summary>The starter house as the Gateway's startup line names it: the lot and its zone id, or "off".</summary>
    public static string Describe(HousingOptions options, ZoneDefinitionCollection zones) =>
        Lot(options, zones) is { } lot ? $"{lot.DisplayName} (zone {lot.Id})" : "off";

    /// <summary>
    /// What's wrong with the starter house setting, for the Gateway to warn about as it starts, before anyone logs in.
    /// A zone id that names no housing lot the server serves turns the grant off (a typo would, with nothing said until
    /// someone looked). And with no grant while <see cref="HousingOptions.FreeLots"/> is off, a new player has no house
    /// unless they can pay for a lot, so a quest that sends players through a Housing door can't be finished, nor any
    /// quest after it in its chain.
    /// </summary>
    public static IEnumerable<string> Problems(HousingOptions options, ZoneDefinitionCollection zones, QuestDefinitionCollection quests)
    {
        if (Lot(options, zones) is not null)
            yield break;

        if (options.StarterHouseZoneId != 0)
        {
            yield return $"Housing:StarterHouseZoneId is {options.StarterHouseZoneId}, which isn't a housing lot the server " +
                "serves, so no account is given a starter house.";
        }

        if (options.FreeLots)
            yield break;

        var throughADoor = quests.Quests.Values
            .Where(HouseDoors.SendsThroughADoor)
            .Select(quest => quest.QuestId)
            .Order()
            .ToList();

        if (throughADoor.Count > 0)
        {
            var which = throughADoor.Count == 1 ? $"quest {throughADoor[0]}" : $"quests {string.Join(", ", throughADoor)}";

            yield return "No account is given a starter house and lots aren't free (Housing:FreeLots), so a new player has " +
                $"no house unless they can pay for a lot. Without one, {which} can't be finished, nor any quest that needs " +
                "it: a step sends players into their house through a Housing door.";
        }
    }

    /// <summary>
    /// What the player is told in chat when their account has just been given one at login, or <c>null</c> for a
    /// character with no quests yet, whom the welcome line sends to Samantha instead.
    /// </summary>
    public static string? LineOnGrant(Player player, HousingZoneDefinition lot) =>
        player.Quests.All.Count == 0
            ? null
            : $"Every account on Evergrove gets a free {lot.DisplayName}, and yours is ready: open My Houses (the house button " +
              $"on the game dock) and click {lot.DisplayName}, or type !house enter {lot.CommandName}.";
}
