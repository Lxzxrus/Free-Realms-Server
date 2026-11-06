using System;
using System.Threading;

using Sanctuary.Game.Zones;

namespace Sanctuary.Game.Entities;

public sealed class PetTreasureChest : Npc
{
    private int _opened;

    public ulong OwnerGuid { get; }
    public string RewardTableKey { get; }
    public string? CoinRewardTableKey { get; set; }
    public int OpenCompositeEffectId { get; set; }

    /// <summary>Evergrove: the pet that dug the chest up. Its treasure rest is saved as the chest pays out.</summary>
    public int PetId { get; init; }

    /// <summary>Evergrove: when the pet dug the chest up, the start of the player's treasure rest.</summary>
    public DateTimeOffset FoundAt { get; init; }

    public PetTreasureChest(IZone zone, ulong ownerGuid, string rewardTableKey)
        : base(zone)
    {
        OwnerGuid = ownerGuid;
        RewardTableKey = rewardTableKey;
    }

    public bool IsOwner(Player player) => OwnerGuid == player.Guid;

    public bool TryOpen()
    {
        return Interlocked.CompareExchange(ref _opened, 1, 0) == 0;
    }

    /// <summary>
    /// Evergrove: closes the chest again after it couldn't pay out (PetHelper.TryOpenTreasureChest), so it can be opened
    /// later and still goes when its time is up.
    /// </summary>
    public void Close()
    {
        Interlocked.Exchange(ref _opened, 0);
    }
}
