using System;
using System.Numerics;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// Positions come from the client. A real client sends finite numbers; NaN compares false with everything, so a
/// NaN position passed every "too far away" check written as <c>distance &gt; range</c> (task 30).
/// </summary>
public static class Positions
{
    /// <summary>
    /// How far across the ground from its zone's origin a real client's point can be, with plenty to spare:
    /// FabledRealms, the largest zone, ends about 6,000 units out. A point far beyond that could make a navmesh query
    /// run forever, freezing the server (<c>NavMeshPathfinder.TryFindPath</c>).
    /// </summary>
    public const float WorldLimit = 100_000f;

    /// <summary>
    /// How far above or below its zone's origin a real client's point can be, with a wide margin: FabledRealms' terrain
    /// lies between about -260 and +410 and its lowest collision mesh reaches about -1,970, while the house lots and
    /// fishing holes stay within a few hundred units of 0. The margin is for a player who falls through the world: they
    /// may fall a long way, and must still be able to press the stuck button. Past this a height is no real one, and
    /// not every finite float can be saved: MariaDB refuses float.MaxValue.
    /// </summary>
    public const float HeightLimit = 10_000_000f;

    public static bool IsFinite(Vector4 position) =>
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) && float.IsFinite(position.W);

    public static bool IsFinite(Quaternion rotation) =>
        float.IsFinite(rotation.X) && float.IsFinite(rotation.Y) && float.IsFinite(rotation.Z) && float.IsFinite(rotation.W);

    /// <summary>
    /// Whether X and Z are within <see cref="WorldLimit"/> of the origin and Y within <see cref="HeightLimit"/>; false
    /// for any NaN.
    /// </summary>
    public static bool IsInWorld(Vector4 position) =>
        MathF.Abs(position.X) <= WorldLimit && MathF.Abs(position.Z) <= WorldLimit && MathF.Abs(position.Y) <= HeightLimit;

    /// <summary>Whether two points are within <paramref name="range"/> of each other; false for any NaN.</summary>
    public static bool IsWithinRange(Vector4 a, Vector4 b, float range)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;

        return dx * dx + dy * dy + dz * dz <= range * range;
    }
}
