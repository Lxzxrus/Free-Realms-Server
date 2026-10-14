using System.Numerics;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// Positions come from the client. A real client sends finite numbers; NaN compares false with everything, so a
/// NaN position passed every "too far away" check written as <c>distance &gt; range</c> (task 30).
/// </summary>
public static class Positions
{
    public static bool IsFinite(Vector4 position) =>
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) && float.IsFinite(position.W);

    public static bool IsFinite(Quaternion rotation) =>
        float.IsFinite(rotation.X) && float.IsFinite(rotation.Y) && float.IsFinite(rotation.Z) && float.IsFinite(rotation.W);

    /// <summary>Whether two points are within <paramref name="range"/> of each other; false for any NaN.</summary>
    public static bool IsWithinRange(Vector4 a, Vector4 b, float range)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;

        return dx * dx + dy * dy + dz * dz <= range * range;
    }
}
