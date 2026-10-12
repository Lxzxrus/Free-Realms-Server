using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Resources.Definitions.Zones;

public sealed class HousingZoneDefinition : BaseZoneDefinition
{
    /// <summary>
    /// How far inside the build area's edge a clamped arrival lands, in metres.
    /// </summary>
    public const float ArrivalMargin = 2f;

    public required string DisplayName { get; set; }
    public required string CommandName { get; set; }

    public int NameId { get; set; }
    public int IconId { get; set; }

    public int StoreBundleId { get; set; }
    public int ItemDefinitionId { get; set; }

    public required string DirectorySnapshot { get; set; }
    public List<BoundingBox> BuildAreas { get; set; } = [];

    /// <summary>
    /// Whether the position falls inside any of the lot's build areas. Authored corners aren't consistently ordered,
    /// so each pair is treated as unordered.
    /// </summary>
    public bool IsInsideBuildArea(Vector4 position)
    {
        return BuildAreas.Any(area =>
            position.X >= MathF.Min(area.Min.X, area.Max.X) &&
            position.X <= MathF.Max(area.Min.X, area.Max.X) &&
            position.Y >= MathF.Min(area.Min.Y, area.Max.Y) &&
            position.Y <= MathF.Max(area.Min.Y, area.Max.Y) &&
            position.Z >= MathF.Min(area.Min.Z, area.Max.Z) &&
            position.Z <= MathF.Max(area.Min.Z, area.Max.Z));
    }

    /// <summary>
    /// Where a player arrives in the lot. Many lots' spawn points stand just outside their own build area (lot 1's is
    /// at X 444.3 against a build area ending at 443.3), and the client refuses to place fixtures there, so a spawn
    /// outside is pulled into the first build area, <see cref="ArrivalMargin"/> from its edge. Height is kept.
    /// After AlchemyDevelopment/Sanctuary's <c>ClampToBuildArea</c> (Jacob Plunkett, AGPL-3.0).
    /// </summary>
    public Vector4 GetArrivalPosition()
    {
        if (BuildAreas.Count == 0 || IsInsideBuildArea(SpawnPosition))
            return SpawnPosition;

        var area = BuildAreas[0];

        return new Vector4(
            ClampInside(SpawnPosition.X, area.Min.X, area.Max.X),
            SpawnPosition.Y,
            ClampInside(SpawnPosition.Z, area.Min.Z, area.Max.Z),
            SpawnPosition.W);
    }

    private static float ClampInside(float value, float first, float second)
    {
        var min = MathF.Min(first, second);
        var max = MathF.Max(first, second);
        var margin = MathF.Min(ArrivalMargin, (max - min) / 2f);

        return Math.Clamp(value, min + margin, max - margin);
    }
}
