using System.Numerics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Extensions;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class PositionsTests
{
    [TestMethod]
    public void NaNOrInfinitePositions_AreNotFinite()
    {
        Assert.IsTrue(Positions.IsFinite(new Vector4(1f, 2f, 3f, 1f)));
        Assert.IsFalse(Positions.IsFinite(new Vector4(float.NaN, 2f, 3f, 1f)));
        Assert.IsFalse(Positions.IsFinite(new Vector4(1f, float.PositiveInfinity, 3f, 1f)));
        Assert.IsTrue(Positions.IsFinite(Quaternion.Identity));
        Assert.IsFalse(Positions.IsFinite(new Quaternion(0f, float.NaN, 0f, 1f)));
    }

    [TestMethod]
    public void NaNPosition_IsOutOfRange()
    {
        var node = new Vector4(10f, 0f, 10f, 1f);

        Assert.IsTrue(Positions.IsWithinRange(new Vector4(12f, 0f, 10f, 1f), node, 5f));
        Assert.IsFalse(Positions.IsWithinRange(new Vector4(100f, 0f, 10f, 1f), node, 5f));

        // The old check was "distance > range, so refuse": NaN compares false with everything and passed it.
        Assert.IsFalse(Positions.IsWithinRange(new Vector4(float.NaN, 0f, 10f, 1f), node, 5f));
    }

    [TestMethod]
    public void PositionsFarOutsideTheWorld_AreNotInIt()
    {
        // FabledRealms' spawn and the far corners of its zone grid and navmesh.
        Assert.IsTrue(Positions.IsInWorld(new Vector4(-1904.883f, -39.7098f, 412.6024f, 1f)));
        Assert.IsTrue(Positions.IsInWorld(new Vector4(5120f, 411f, 6144f, 1f)));
        Assert.IsTrue(Positions.IsInWorld(new Vector4(-3072f, -1966.7f, -2048f, 1f)));

        // From about 1.4e11 out, a navmesh query used to never return.
        Assert.IsFalse(Positions.IsInWorld(new Vector4(1.4e11f, 0f, 0f, 1f)));
        Assert.IsFalse(Positions.IsInWorld(new Vector4(0f, 0f, 1e20f, 1f)));
        Assert.IsFalse(Positions.IsInWorld(new Vector4(-Positions.WorldLimit - 1f, 0f, 0f, 1f)));

        Assert.IsFalse(Positions.IsInWorld(new Vector4(float.NaN, 0f, 0f, 1f)));
        Assert.IsFalse(Positions.IsInWorld(new Vector4(0f, float.PositiveInfinity, 0f, 1f)));
        Assert.IsFalse(Positions.IsInWorld(new Vector4(0f, 0f, float.NegativeInfinity, 1f)));

        // W isn't a coordinate, so it isn't checked: a path request's points carry whatever W the client sends.
        Assert.IsTrue(Positions.IsInWorld(new Vector4(0f, 0f, 0f, float.NaN)));
    }

    [TestMethod]
    public void HeightsFarAboveOrBelowTheWorld_AreNotInIt()
    {
        // A player who fell through the world is still in it a long way down.
        Assert.IsTrue(Positions.IsInWorld(new Vector4(-1904.883f, -1e6f, 412.6024f, 1f)));
        Assert.IsTrue(Positions.IsInWorld(new Vector4(-1904.883f, -Positions.HeightLimit, 412.6024f, 1f)));

        Assert.IsFalse(Positions.IsInWorld(new Vector4(-1904.883f, -Positions.HeightLimit - 1f, 412.6024f, 1f)));
        Assert.IsFalse(Positions.IsInWorld(new Vector4(-1904.883f, Positions.HeightLimit + 1f, 412.6024f, 1f)));

        // Finite, yet MariaDB refuses to save it: the character's save on disconnect failed and stopped the Gateway.
        Assert.IsFalse(Positions.IsInWorld(new Vector4(0f, float.MaxValue, 0f, 1f)));
        Assert.IsFalse(Positions.IsInWorld(new Vector4(0f, -float.MaxValue, 0f, 1f)));
    }

    [TestMethod]
    public void HeightsPastTheLimit_AreHeldAtIt()
    {
        // A player who has fallen through the world and kept falling: held at the limit, so in the world (no fault) and
        // saved as that.
        var fallen = Positions.ClampHeight(new Vector4(-1904.883f, -3f * Positions.HeightLimit, 412.6024f, 1f));

        Assert.AreEqual(new Vector4(-1904.883f, -Positions.HeightLimit, 412.6024f, 1f), fallen);
        Assert.IsTrue(Positions.IsInWorld(fallen));
        Assert.AreEqual(Positions.HeightLimit, Positions.ClampHeight(new Vector4(0f, float.MaxValue, 0f, 1f)).Y);

        // A height within the limit is left as it is, and nothing but the height is ever changed: far out across the
        // ground is still outside the world.
        var spawn = new Vector4(-1904.883f, -39.7098f, 412.6024f, 1f);
        var farOut = Positions.ClampHeight(new Vector4(1e20f, -1e20f, 5f, 7f));

        Assert.AreEqual(spawn, Positions.ClampHeight(spawn));
        Assert.AreEqual(new Vector4(1e20f, -Positions.HeightLimit, 5f, 7f), farOut);
        Assert.IsFalse(Positions.IsInWorld(farOut));

        // NaN and infinities are no height a fall reaches: they are left for IsInWorld to refuse.
        Assert.IsTrue(float.IsNaN(Positions.ClampHeight(new Vector4(0f, float.NaN, 0f, 1f)).Y));
        Assert.AreEqual(float.NegativeInfinity, Positions.ClampHeight(new Vector4(0f, float.NegativeInfinity, 0f, 1f)).Y);
        Assert.IsFalse(Positions.IsInWorld(Positions.ClampHeight(new Vector4(0f, float.PositiveInfinity, 0f, 1f))));
    }

    [TestMethod]
    public void RotationsBeyondAnyRealOne_AreNotWithinTheLimit()
    {
        // FabledRealms' spawn heading, a heading made from an angle, a unit quaternion and no rotation at all.
        Assert.IsTrue(Positions.IsWithinRotationLimit(new Quaternion(0.5531712f, 0f, 0.8330676f, 0f)));
        Assert.IsTrue(Positions.IsWithinRotationLimit(QuaternionExtensions.FromHeading(-2.5f)));
        Assert.IsTrue(Positions.IsWithinRotationLimit(Quaternion.Identity));
        Assert.IsTrue(Positions.IsWithinRotationLimit(default));

        Assert.IsFalse(Positions.IsWithinRotationLimit(new Quaternion(0f, 0f, Positions.RotationLimit + 1f, 0f)));

        // Finite, yet MariaDB refuses to save it.
        Assert.IsFalse(Positions.IsWithinRotationLimit(new Quaternion(float.MaxValue, 0f, 0f, 0f)));
        Assert.IsFalse(Positions.IsWithinRotationLimit(new Quaternion(0f, 0f, -float.MaxValue, 0f)));

        Assert.IsFalse(Positions.IsWithinRotationLimit(new Quaternion(float.NaN, 0f, 0f, 0f)));
        Assert.IsFalse(Positions.IsWithinRotationLimit(new Quaternion(0f, float.PositiveInfinity, 0f, 0f)));
        Assert.IsFalse(Positions.IsWithinRotationLimit(new Quaternion(0f, 0f, 0f, float.NegativeInfinity)));
    }

    [TestMethod]
    public void JumpVelocitiesBeyondAnyRealOne_AreNotWithinTheLimit()
    {
        // A jump from the ground (the load-test bot sends 8), a launch far harder than any, a fall and none at all.
        Assert.IsTrue(Positions.IsWithinVerticalVelocityLimit(8f));
        Assert.IsTrue(Positions.IsWithinVerticalVelocityLimit(1000f));
        Assert.IsTrue(Positions.IsWithinVerticalVelocityLimit(-50f));
        Assert.IsTrue(Positions.IsWithinVerticalVelocityLimit(0f));
        Assert.IsTrue(Positions.IsWithinVerticalVelocityLimit(Positions.VerticalVelocityLimit));

        Assert.IsFalse(Positions.IsWithinVerticalVelocityLimit(Positions.VerticalVelocityLimit + 1f));
        Assert.IsFalse(Positions.IsWithinVerticalVelocityLimit(float.MaxValue));
        Assert.IsFalse(Positions.IsWithinVerticalVelocityLimit(-float.MaxValue));

        // Relayed to every player who can see the jumper.
        Assert.IsFalse(Positions.IsWithinVerticalVelocityLimit(float.NaN));
        Assert.IsFalse(Positions.IsWithinVerticalVelocityLimit(float.PositiveInfinity));
        Assert.IsFalse(Positions.IsWithinVerticalVelocityLimit(float.NegativeInfinity));
    }

    [TestMethod]
    public void JumpsAndPositionUpdates_CarryNoW()
    {
        // The client sends X, Y and Z, and the server reads W as 1, so no client sets the W that a friend teleporting to
        // the player, or a boombox or cake the player sets down, copies. The relay leaves W out too.
        var jump = new PlayerUpdatePacketJump { Position = new Vector4(1f, 2f, 3f, float.NaN), VerticalVelocity = 8f };

        Assert.IsTrue(PlayerUpdatePacketJump.TryDeserialize(jump.Serialize(), out var readJump));
        Assert.AreEqual(new Vector4(1f, 2f, 3f, 1f), readJump.Position);

        var update = new PlayerUpdatePacketUpdatePosition { Position = new Vector4(1f, 2f, 3f, float.MaxValue) };

        Assert.IsTrue(PlayerUpdatePacketUpdatePosition.TryDeserialize(update.Serialize(), out var readUpdate));
        Assert.AreEqual(new Vector4(1f, 2f, 3f, 1f), readUpdate.Position);
    }
}
