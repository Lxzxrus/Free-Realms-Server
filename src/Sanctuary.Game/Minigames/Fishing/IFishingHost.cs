using System;

using Sanctuary.Core.IO;
using Sanctuary.Game.Entities;

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

/// <summary>A real player: catches are granted through the reward manager, which saves them first.</summary>
public sealed class PlayerFishingHost(Player player, IRewardManager rewardManager) : IFishingHost
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
            // Catches aren't tintable and have no icon tint, so their stack's tint is 0, as for quest rewards.
            return rewardManager.TryGrantItem(player, itemDefinitionId, tint: 0, quantity: 1)
                ? FishingGrantResult.Granted
                : FishingGrantResult.Failed;
        }
    }
}
