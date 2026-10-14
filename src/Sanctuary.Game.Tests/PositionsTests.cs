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
}
