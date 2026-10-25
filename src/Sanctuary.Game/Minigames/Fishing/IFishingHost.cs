using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Minigames.Fishing;

public enum FishingGrantResult
{
    Granted,

    /// <summary>Not now (a trade is being saved): try again on the next tick.</summary>
    Busy,

    Failed
}

/// <summary>The player a <see cref="FishingSession"/> fishes for: where its packets go and how a catch is paid.</summary>
public interface IFishingHost
{
    ulong Guid { get; }

    /// <summary>Sends to this player only.</summary>
    void Send(ISerializablePacket packet);

    /// <summary>Sends to this player and to the players who can see them, who then see them fish.</summary>
    void SendToSelfAndVisible(ISerializablePacket packet);

    /// <summary>Grants the caught item: saved, in the bag, and announced.</summary>
    FishingGrantResult Grant(int itemDefinitionId);
}

/// <summary>A real player: catches are granted through <see cref="RewardHelper"/>, which saves them first.</summary>
public sealed class PlayerFishingHost(Player player, IResourceManager resourceManager,
    IDbContextFactory<DatabaseContext> dbContextFactory, ILogger logger) : IFishingHost
{
    public ulong Guid => player.Guid;

    public void Send(ISerializablePacket packet) => player.SendTunneled(packet);

    public void SendToSelfAndVisible(ISerializablePacket packet) => player.SendTunneledToVisible(packet, sendToSelf: true);

    /// <remarks>
    /// The catch lands on a timer, not in a packet handler, so it takes the guard a packet handler would hold: a trade
    /// saves both players' items on another thread under it, and the two must not interleave.
    /// </remarks>
    public FishingGrantResult Grant(int itemDefinitionId)
    {
        if (!player.TryAcquireTradeMutationGuard(TimeSpan.Zero, out var lease))
            return FishingGrantResult.Busy;

        using (lease)
        {
            using var dbContext = dbContextFactory.CreateDbContext();

            // A catch takes its own tint, as every reward does.
            return RewardHelper.TryGrantItem(resourceManager, dbContext, logger, player, itemDefinitionId, tint: null, quantity: 1)
                ? FishingGrantResult.Granted
                : FishingGrantResult.Failed;
        }
    }
}
