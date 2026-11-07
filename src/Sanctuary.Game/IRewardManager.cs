using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions.Rewards;

namespace Sanctuary.Game;

public interface IRewardManager
{
    bool TryRollReward(string rewardTableKey, out RewardDropDefinition? drop);

    bool TryGrantReward(Player player, RewardDropDefinition drop, ulong sourceGuid = 0);

    /// <param name="tint">The tint to pay the item in; <c>null</c> pays it in the item's own tint (Evergrove,
    /// <c>ItemTints.Default</c>: reward clothes come in their set's colour, not the dye-able red of tint 0).</param>
    bool TryGrantItem(Player player, int itemDefinitionId, int? tint, int quantity = 1, ulong sourceGuid = 0);

    bool TryGrantCurrency(Player player, CurrencyType currencyType, int amount);
}
