namespace Sanctuary.Game.Resources.Definitions.Zones;

/// <summary>
/// A fishing hole. Each hole is a client scene of its own (<c>sg_fishing_medpond</c> and the like) that a player
/// enters by starting the hole's fishing activity. The definition id is that activity's id in
/// <c>ClientActivityDefinitions.json</c> (560 to 565). After edenfps' fishing (upstream PR #31).
/// </summary>
public sealed class FishingZoneDefinition : BaseZoneDefinition
{
    /// <summary>
    /// X of the scene's <c>Underwater_Bed</c> area (from the scene's <c>Areas.xml</c> in the client). The client draws
    /// the underwater fish at a fixed Y of -8 and Z of 485 and takes only X from the server, so any other X puts the
    /// fish over the wrong water.
    /// </summary>
    public float UnderwaterBedX { get; set; }
}
