using System;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;

namespace Sanctuary.Game.Zones;

/// <summary>
/// A fishing hole's scene, shared by everyone fishing it so they see each other fish. Players arrive by starting the
/// hole's activity and leave through the minigame's end, the map, the stuck button or by logging out.
/// </summary>
public sealed class FishingZone : BaseZone
{
    public FishingZoneDefinition Definition { get; }

    public FishingZone(FishingZoneDefinition zoneDefinition, IServiceProvider serviceProvider)
        : base(zoneDefinition, serviceProvider)
    {
        Definition = zoneDefinition;
    }

    /// <summary>However the player left, their fishing ends here and their client leaves fishing.</summary>
    protected override void OnPlayerRemoved(Player player)
    {
        if (player.FishingSession?.ActivityId != DefinitionId)
            return;

        player.FishingSession = null;

        player.SendTunneled(new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = player.Guid,
            State = 0
        });
    }
}
