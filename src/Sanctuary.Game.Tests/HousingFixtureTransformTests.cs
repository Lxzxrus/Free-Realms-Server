using System;
using System.Numerics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A placed part's rotation and its position's W are saved as the client sends them, and only NaN and the infinities
/// were refused. MariaDB refuses to store float.MaxValue too, so a modified client's save of one threw.
/// </summary>
[TestClass]
public sealed class HousingFixtureTransformTests
{
    private static readonly Vector4 Position = new(400f, 5f, 430f, 1f);

    [TestMethod]
    public void RealRotations_AreWithinTheLimit()
    {
        Assert.IsTrue(HousingZoneRuntime.IsWithinTransformLimit(Position, default));
        Assert.IsTrue(HousingZoneRuntime.IsWithinTransformLimit(Position, new Quaternion(MathF.PI, 0f, 0f, 0f)), "an angle");
        Assert.IsTrue(HousingZoneRuntime.IsWithinTransformLimit(Position, new Quaternion(0.6f, 0f, 0.8f, 0f)), "a heading");
        Assert.IsTrue(HousingZoneRuntime.IsWithinTransformLimit(Position, Quaternion.Identity));
        Assert.IsTrue(HousingZoneRuntime.IsWithinTransformLimit(Position, new Quaternion(3600f, 0f, 0f, 0f)), "ten turns in degrees");
    }

    [TestMethod]
    [DataRow(float.MaxValue)]
    [DataRow(-float.MaxValue)]
    [DataRow(1_000_000f)]
    [DataRow(float.NaN)]
    public void ARotationComponentOrW_PastTheLimit_IsRefused(float value)
    {
        Assert.IsFalse(HousingZoneRuntime.IsWithinTransformLimit(Position with { W = value }, default), "W");
        Assert.IsFalse(HousingZoneRuntime.IsWithinTransformLimit(Position, new Quaternion(value, 0f, 0f, 0f)), "X");
        Assert.IsFalse(HousingZoneRuntime.IsWithinTransformLimit(Position, new Quaternion(0f, value, 0f, 0f)), "Y");
        Assert.IsFalse(HousingZoneRuntime.IsWithinTransformLimit(Position, new Quaternion(0f, 0f, value, 0f)), "Z");
        Assert.IsFalse(HousingZoneRuntime.IsWithinTransformLimit(Position, new Quaternion(0f, 0f, 0f, value)), "rotation W");
    }
}
