using System;
using System.Data;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Database.Entities;

namespace Sanctuary.Game.Housing;

public enum HouseGrantResult
{
    /// <summary>The house was added, and its cost, if any, taken.</summary>
    Added,

    /// <summary>A character on the account already has a house on that lot. Nothing changed.</summary>
    AlreadyOwned,

    /// <summary>The character has less Station Cash than the house costs. Nothing changed.</summary>
    NotEnoughStationCash,

    /// <summary>There's no such character. Nothing changed.</summary>
    NoCharacter,

    /// <summary>The database refused the change or couldn't be reached, even after retrying. Nothing changed.</summary>
    Failed
}

/// <summary>
/// Evergrove: the one way a house is added, whether a Marketplace order buys it (PacketInGamePurchasePlaceOrderPacketHandler)
/// or the account is given its free starter house (<see cref="IHouseManager.EnsureStarterHouse"/>). Nothing else adds a
/// house, and only deleting the character who holds one takes it away (<c>docs/design/evergrove-housing.md</c>, sections
/// 2 and 11).
/// </summary>
public static class HouseGrants
{
    /// <summary>
    /// Adds a house on the lot to the character, in a serializable transaction. It's refused if any character on the
    /// account has that lot, as houses belong to the account (<see cref="HouseOwnership"/>). A cost above 0 comes off the
    /// character's Station Cash in the same transaction, and only if they have enough; a free one changes no balance.
    /// Two of an account's characters adding the same lot at once can't both succeed: on MariaDB one deadlocks, and the
    /// execution strategy runs it again, when it finds the other's house; SQLite lets one writer in at a time.
    /// </summary>
    /// <param name="stationCashCost">The Station Cash it costs: 0 or more.</param>
    /// <param name="stationCash">The character's Station Cash once the house is added; 0 when it isn't.</param>
    public static HouseGrantResult Add(IDbContextFactory<DatabaseContext> dbContextFactory, ILogger logger, ulong characterId,
        int zoneDefinitionId, int stationCashCost, out int stationCash)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stationCashCost);

        stationCash = 0;
        var result = HouseGrantResult.Failed;

        using var strategyContext = dbContextFactory.CreateDbContext();
        var strategy = strategyContext.Database.CreateExecutionStrategy();

        try
        {
            strategy.ExecuteInTransaction(
                () =>
                {
                    strategyContext.ChangeTracker.Clear();

                    // Houses belong to the account, so a lot any of its characters has is already this character's.
                    if (HouseOwnership.OwnedByAccountOf(strategyContext, characterId).Any(house =>
                            house.ZoneDefinitionId == zoneDefinitionId))
                    {
                        result = HouseGrantResult.AlreadyOwned;
                        return;
                    }

                    // A free lot changes no balance, so only check the character is there.
                    var updated = stationCashCost == 0
                        ? strategyContext.Characters.Count(character => character.Id == characterId)
                        : strategyContext.Characters
                            .Where(character =>
                                character.Id == characterId &&
                                character.StationCash >= stationCashCost)
                            .ExecuteUpdate(setters => setters.SetProperty(
                                character => character.StationCash,
                                character => character.StationCash - stationCashCost));

                    if (updated != 1)
                    {
                        result = strategyContext.Characters.Any(character => character.Id == characterId)
                            ? HouseGrantResult.NotEnoughStationCash
                            : HouseGrantResult.NoCharacter;
                        return;
                    }

                    strategyContext.Houses.Add(new DbHouse
                    {
                        CharacterId = characterId,
                        ZoneDefinitionId = zoneDefinitionId
                    });

                    if (strategyContext.SaveChanges() <= 0)
                    {
                        result = HouseGrantResult.Failed;
                        return;
                    }

                    result = HouseGrantResult.Added;
                },
                () =>
                {
                    if (result != HouseGrantResult.Added)
                        return false;

                    using var verificationContext = dbContextFactory.CreateDbContext();
                    return verificationContext.Houses.AsNoTracking().Any(house =>
                        house.CharacterId == characterId &&
                        house.ZoneDefinitionId == zoneDefinitionId);
                },
                IsolationLevel.Serializable);

            if (result == HouseGrantResult.Added)
            {
                using var resultContext = dbContextFactory.CreateDbContext();
                stationCash = resultContext.Characters
                    .Where(character => character.Id == characterId)
                    .Select(character => character.StationCash)
                    .Single();
            }
        }
        catch (Exception exception) when (exception is DbUpdateException or RetryLimitExceededException)
        {
            logger.LogWarning(exception, "Unable to add house {ZoneDefinitionId} for character {CharacterId}.", zoneDefinitionId, characterId);
            result = HouseGrantResult.Failed;
        }

        return result;
    }
}
