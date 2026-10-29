using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources.Definitions.Rewards;

namespace Sanctuary.Game.Minigames;

/// <summary>
/// What winning a Flash minigame pays, from the game's data (<c>Mimic.json</c>, <c>MatchThree.json</c>): coins, and
/// stars on one job. Every amount is a placeholder until the economy pass.
/// </summary>
/// <param name="Coins">Coins for the win.</param>
/// <param name="ProfileId">The job the stars go to (Profiles.json: 45 Chef, 14 Miner...); 0 for none.</param>
/// <param name="Experience">Stars on that job, paid only if the player has it.</param>
public readonly record struct MinigameReward(int Coins, int ProfileId, int Experience)
{
    /// <summary>
    /// Pays a win, the same way for every minigame: the coins, with the client's new coin count and a reward toast,
    /// then the stars on <see cref="ProfileId"/> with a toast of their own. <see cref="RewardHelper"/> saves each
    /// before telling the client, and pays no stars to a job the character doesn't have.
    /// </summary>
    /// <returns>Whether the coins and the stars were paid.</returns>
    public (bool Coins, bool Stars) Pay(IResourceManager resourceManager, DatabaseContext dbContext, ILogger logger, Player player)
    {
        var coins = RewardHelper.TryGrantCurrency(dbContext, logger, player, CurrencyType.Coins, Coins);

        var stars = ProfileId > 0
            && RewardHelper.TryGrantExperience(resourceManager, dbContext, logger, player, ProfileId, Experience);

        return (coins, stars);
    }
}
