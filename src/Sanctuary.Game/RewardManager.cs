using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Resources.Definitions.Rewards;

namespace Sanctuary.Game;

/// <summary>
/// Upstream OSFR's reward service, kept so upstream code that asks for <see cref="IRewardManager"/> (pets, abilities,
/// quests, gathering) keeps working. Evergrove replaced the original with <see cref="RewardHelper"/> (task 31, from
/// OSFR PR #120), so every grant here goes through it: the same saves, item tints and reward toasts as the rest of
/// Evergrove.
/// </summary>
public sealed class RewardManager : IRewardManager
{
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;
    private readonly IDbContextFactory<DatabaseContext> _dbContextFactory;

    public RewardManager(ILogger<RewardManager> logger, IResourceManager resourceManager,
        IDbContextFactory<DatabaseContext> dbContextFactory)
    {
        _logger = logger;
        _resourceManager = resourceManager;
        _dbContextFactory = dbContextFactory;
    }

    public bool TryRollReward(string rewardTableKey, out RewardDropDefinition? drop)
    {
        if (_resourceManager.RewardTables.TryRoll(rewardTableKey, out var rolled))
        {
            drop = rolled;
            return true;
        }

        _logger.LogError("Unknown reward table {key}.", rewardTableKey);
        drop = null;
        return false;
    }

    public bool TryGrantReward(Player player, RewardDropDefinition drop, ulong sourceGuid = 0)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        return RewardHelper.TryGrantReward(_resourceManager, dbContext, _logger, player, drop, sourceGuid);
    }

    public bool TryGrantItem(Player player, int itemDefinitionId, int? tint, int quantity = 1, ulong sourceGuid = 0)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        return RewardHelper.TryGrantItem(_resourceManager, dbContext, _logger, player, itemDefinitionId, tint, quantity,
            sourceGuid);
    }

    public bool TryGrantCurrency(Player player, CurrencyType currencyType, int amount)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        return RewardHelper.TryGrantCurrency(dbContext, _logger, player, currencyType, amount);
    }
}
