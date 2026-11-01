using System.Numerics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;

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

        // A player who fell through the world is still in it, however far down.
        Assert.IsTrue(Positions.IsInWorld(new Vector4(-1904.883f, -1e7f, 412.6024f, 1f)));

        Assert.IsFalse(Positions.IsInWorld(new Vector4(float.NaN, 0f, 0f, 1f)));
        Assert.IsFalse(Positions.IsInWorld(new Vector4(0f, float.PositiveInfinity, 0f, 1f)));
        Assert.IsFalse(Positions.IsInWorld(new Vector4(0f, 0f, float.NegativeInfinity, 1f)));

        // W isn't a coordinate, so it isn't checked: a path request's points carry whatever W the client sends.
        Assert.IsTrue(Positions.IsInWorld(new Vector4(0f, 0f, 0f, float.NaN)));
    }
}
