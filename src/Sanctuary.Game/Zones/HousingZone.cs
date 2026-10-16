using System;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Game.Zones;

public sealed class HousingZone : BaseZone
{
    private bool _runtimeInitialized;

    public ulong HouseId { get; }

    /// <summary>
    /// The account that owns the house. Every character on it may do what the owner can. <see cref="BaseZone.OwnerId"/>
    /// is the character who bought the house, which with the zone definition identifies the house.
    /// </summary>
    public ulong OwnerUserId { get; }
    public HousingZoneDefinition HousingDefinition { get; }
    public HousingZoneRuntime Runtime { get; }

    public HousingZone(
        HousingZoneDefinition zoneDefinition,
        ulong houseId,
        ulong ownerUserId,
        IServiceProvider serviceProvider)
        : base(zoneDefinition, serviceProvider)
    {
        HouseId = houseId;
        OwnerUserId = ownerUserId;
        HousingDefinition = zoneDefinition;
        Runtime = new HousingZoneRuntime(this, serviceProvider);
    }

    /// <summary>Whether the player's account owns this house, so they may do whatever the owner can.</summary>
    public bool IsOwner(Player player) => HouseAccess.IsOwner(player.UserId, OwnerUserId);

    public override void OnStart()
    {
        base.OnStart();

        // The zone manager calls OnStart every time it hands out this instance,
        // so only load the fixtures from the database the first time.
        if (_runtimeInitialized)
            return;

        _runtimeInitialized = true;
        Runtime.Initialize();
    }

    public override void OnClientIsReady(Player player)
    {
        Runtime.SendInitialData(player);
        base.OnClientIsReady(player);

        // Quest targets live in the world, not in a house: without this the "Active Quest!" arrow keeps chasing a
        // target that isn't in the zone. Re-entering the world sends the target again.
        player.SendTunneled(new ObjectiveTargetUpdatePacket { Active = false });

        SendShopData(player);
    }

    public override void OnClientFinishedLoading(Player player)
    {
        base.OnClientFinishedLoading(player);
        Runtime.OnClientFinishedLoading(player);
    }

    protected override void OnPlayerRemoved(Player player)
    {
        Runtime.OnPlayerRemoved(player);
    }

    protected override void OnDisposing()
    {
        Runtime.Dispose();
    }
}
