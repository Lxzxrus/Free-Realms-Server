using Microsoft.Extensions.Logging;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Minigames.Fishing;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Gateway.Helpers.Abilities;

// A fishing lure, used from the tackle box on the action bar: one is used up and it stays on until the player leaves
// the hole. Outside a fishing hole it does nothing and isn't used up. From edenfps' fishing (upstream PR #31).
public sealed class FishingLureAbility(AbilityServices services) : ConsumableAbility(services)
{
    public override bool Matches(ClientItemDefinition itemDefinition) => FishingGear.IsLure(itemDefinition.Id);

    public override bool HandleAbility(Player player, AbilityPacketClientRequestStartAbility packet, int slot, ClientItem clientItem, ClientItemDefinition itemDefinition)
    {
        // Not fishing, or this lure is on already (a double click): nothing to use one up for.
        if (player.FishingSession is not { } session || session.ActiveLureItemId == itemDefinition.Id)
            return SendFailure(player);

        var countBefore = clientItem.Count;

        FinishActivation(player, clientItem, itemDefinition, slot, MinCooldownMs);

        // ConsumeItem answers true even when the database refused, so look at the item itself.
        var usedUp = !player.Items.Contains(clientItem) || clientItem.Count < countBefore;

        if (!usedUp)
            return true;

        session.SetActiveLure(itemDefinition.Id);

        _logger.LogInformation("Fishing: {player} put on lure {item}.", player.Guid, itemDefinition.Id);

        return true;
    }
}
