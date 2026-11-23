using System.Numerics;
using System.Text.Json.Serialization;

using Sanctuary.Core.Extensions;
using Sanctuary.Core.IO;
using Sanctuary.Game.Quests;

namespace Sanctuary.Game.Resources.Definitions.QuestGoals;

public sealed class ReachLocationGoal : QuestGoal
{
    // [x, y, z]. The proximity check is 2D, but Y must be the ground height: Take Me There looks the point up on the
    // navmesh, which only searches a few metres up and down from it.
    [JsonConverter(typeof(Vector3XzJsonConverter))]
    public Vector3 ReachPosition { get; set; }

    public float ReachRadius { get; set; } = 12f;

    public const int FabledRealmsZoneDefinitionId = 1;

    public int ZoneDefinitionId { get; set; } = FabledRealmsZoneDefinitionId;

    public override bool Matches(QuestDefinition quest, QuestEvent questEvent)
        => ReachPosition != Vector3.Zero
            && questEvent is PlayerMovedEvent { Position: var position, ZoneDefinitionId: var zoneDefinitionId }
            && zoneDefinitionId == ZoneDefinitionId
            && position.IsInCircle(ReachPosition, ReachRadius);
}
