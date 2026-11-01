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
    /// may fall a long way, and must still be able to press the stuck button. A client's height past this is held at it
    /// (<see cref="ClampHeight"/>), as not every finite float can be saved: MariaDB refuses float.MaxValue.
    /// </summary>
    public const float HeightLimit = 10_000_000f;

    /// <summary>
    /// How large a rotation's components can be, with plenty to spare: a real one is a heading (a direction) or a unit
    /// quaternion, whose components lie between -1 and 1. A finite rotation can still be absurd: MariaDB refuses to save
    /// float.MaxValue, and turning a point by a huge rotation gives infinities and NaN.
    /// </summary>
    public const float RotationLimit = 10f;

    /// <summary>
    /// How fast a jump can send a player up or down, with plenty to spare: a real jump or launch pad starts a player
    /// upwards at tens of units a second, and at this speed they would rise millions of units. A jump's vertical velocity
    /// is relayed to every player who can see the jumper, so NaN, infinities and absurd values such as float.MaxValue must
    /// not get through.
    /// </summary>
    public const float VerticalVelocityLimit = 10_000f;

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

    /// <summary>
    /// The position with a finite height past <see cref="HeightLimit"/> brought back to the limit, and any other as it
    /// is: NaN and infinities are left for <see cref="IsInWorld"/> to refuse. A player who falls through the world keeps
    /// falling, and their client keeps sending where they are. Counted as faults, those positions disconnected them
    /// within a second, and as the last one inside the limit had been saved, again soon after every login.
    /// </summary>
    public static Vector4 ClampHeight(Vector4 position)
    {
        if (float.IsFinite(position.Y))
            position.Y = Math.Clamp(position.Y, -HeightLimit, HeightLimit);

        return position;
    }

    /// <summary>Whether every component of the rotation is within <see cref="RotationLimit"/>; false for any NaN.</summary>
    public static bool IsWithinRotationLimit(Quaternion rotation) =>
        MathF.Abs(rotation.X) <= RotationLimit && MathF.Abs(rotation.Y) <= RotationLimit &&
        MathF.Abs(rotation.Z) <= RotationLimit && MathF.Abs(rotation.W) <= RotationLimit;

    /// <summary>Whether a jump's vertical velocity is within <see cref="VerticalVelocityLimit"/>; false for NaN.</summary>
    public static bool IsWithinVerticalVelocityLimit(float verticalVelocity) => MathF.Abs(verticalVelocity) <= VerticalVelocityLimit;

    /// <summary>Whether two points are within <paramref name="range"/> of each other; false for any NaN.</summary>
    public static bool IsWithinRange(Vector4 a, Vector4 b, float range)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;

        return dx * dx + dy * dy + dz * dz <= range * range;
    }
}
