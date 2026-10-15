using System;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Resources;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The one level curve every job shares (<c>RankLevels.json</c>, from upstream OSFR PR #120): what loads, and how
/// stars turn into levels.
/// </summary>
[TestClass]
public sealed class RankLevelDefinitionCollectionTests
{
    private static string FindSourceFile(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);

            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"src/{string.Join('/', relativePath)} not found above the test output.");
    }

    private static RankLevelDefinitionCollection LoadShippedCurve()
    {
        var curve = new RankLevelDefinitionCollection(NullLogger.Instance);

        Assert.IsTrue(curve.Load(FindSourceFile("Resources", "RankLevels.json")));

        return curve;
    }

    private static string CurveJson(params int[] starsToNextLevel)
    {
        return "[" + string.Join(",", starsToNextLevel.Select((stars, index) =>
            $"{{\"Level\":{index + 1},\"StarsToNextLevel\":{stars}}}")) + "]";
    }

    /// <summary>Loads a curve from a temporary file: one entry per level, starting at 1.</summary>
    private static RankLevelDefinitionCollection Curve(params int[] starsToNextLevel)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sanctuary-rank-levels-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, CurveJson(starsToNextLevel));

        try
        {
            var curve = new RankLevelDefinitionCollection(NullLogger.Instance);

            Assert.IsTrue(curve.Load(path));

            return curve;
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ShippedCurve_RunsFromOneToTwenty()
    {
        var curve = LoadShippedCurve();

        Assert.AreEqual(20, curve.MaxLevel);
        Assert.AreEqual(100, curve[1].StarsToNextLevel);
        Assert.AreEqual(1000, curve[19].StarsToNextLevel);
        Assert.AreEqual(0, curve[20].StarsToNextLevel, "Nothing comes after the top level.");

        // 100, 150, ... 1000: 10,450 stars take a new job from level 1 to the top.
        Assert.AreEqual(10450, Enumerable.Range(1, 19).Sum(level => curve[level].StarsToNextLevel));
        Assert.AreEqual((20, 0), curve.Accrue(1, 0, 10450));
        Assert.AreEqual((19, 999), curve.Accrue(1, 0, 10449));
    }

    [TestMethod]
    [DataRow("[]", DisplayName = "No levels")]
    [DataRow("[{\"Level\":1,\"StarsToNextLevel\":100},{\"Level\":3,\"StarsToNextLevel\":0}]", DisplayName = "A gap")]
    [DataRow("[{\"Level\":2,\"StarsToNextLevel\":100},{\"Level\":3,\"StarsToNextLevel\":0}]", DisplayName = "Not starting at 1")]
    [DataRow("[{\"Level\":1,\"StarsToNextLevel\":100},{\"Level\":1,\"StarsToNextLevel\":0}]", DisplayName = "A level twice")]
    [DataRow("[{\"Level\":1,\"StarsToNextLevel\":0},{\"Level\":2,\"StarsToNextLevel\":0}]", DisplayName = "A level that takes nothing")]
    [DataRow("[{\"Level\":1,\"StarsToNextLevel\":-5},{\"Level\":2,\"StarsToNextLevel\":0}]", DisplayName = "A level that takes less than nothing")]
    [DataRow("[{\"Level\":1,\"StarsToNextLevel\":100},{\"Level\":2,\"StarsToNextLevel\":50}]", DisplayName = "A top level that leads somewhere")]
    [DataRow("{\"Level\":1}", DisplayName = "Not a list")]
    public void Load_RefusesABrokenCurve(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sanctuary-rank-levels-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);

        try
        {
            var curve = new RankLevelDefinitionCollection(NullLogger.Instance);

            Assert.IsFalse(curve.Load(path));
            Assert.AreEqual(0, curve.MaxLevel, "A refused curve pays nothing.");
            Assert.AreEqual((1, 0), curve.Accrue(1, 0, 500));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The resource watcher reloads the file in place when it's edited. Upstream's version added each level again on
    /// top of the old ones, so every reload failed and edits never applied.
    /// </summary>
    [TestMethod]
    public void Reload_AppliesAnEditedCurveAndKeepsTheOldOneIfTheEditIsBroken()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sanctuary-rank-levels-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, CurveJson(100, 200, 0));
            var curve = new RankLevelDefinitionCollection(NullLogger.Instance);
            Assert.IsTrue(curve.Load(path));
            Assert.AreEqual(3, curve.MaxLevel);

            File.WriteAllText(path, CurveJson(50, 0));
            Assert.IsTrue(curve.Load(path));
            Assert.AreEqual(2, curve.MaxLevel);
            Assert.AreEqual(50, curve[1].StarsToNextLevel);
            Assert.IsFalse(curve.ContainsKey(3), "A level taken out of the file is gone after the reload.");

            File.WriteAllText(path, CurveJson(50, 25));
            Assert.IsFalse(curve.Load(path));
            Assert.AreEqual(2, curve.MaxLevel);
            Assert.AreEqual(0, curve[2].StarsToNextLevel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Accrue_StaysInTheLevelUntilItsStarsAreAllEarned()
    {
        var curve = Curve(100, 150, 200, 0);

        Assert.AreEqual((1, 60), curve.Accrue(1, 0, 60));
        Assert.AreEqual((1, 99), curve.Accrue(1, 60, 39));
        Assert.AreEqual((2, 0), curve.Accrue(1, 60, 40), "Exactly enough finishes the level.");
    }

    [TestMethod]
    public void Accrue_CarriesWhatsLeftIntoTheNextLevels()
    {
        var curve = Curve(100, 150, 200, 0);

        Assert.AreEqual((2, 20), curve.Accrue(1, 90, 30));
        Assert.AreEqual((3, 10), curve.Accrue(1, 0, 260), "One grant can finish more than one level.");
    }

    [TestMethod]
    public void Accrue_StopsAtTheTopLevel()
    {
        var curve = Curve(100, 150, 200, 0);

        Assert.AreEqual((4, 0), curve.Accrue(3, 150, 5000), "Stars past the top level have nothing to fill.");
        Assert.AreEqual((4, 0), curve.Accrue(4, 0, 100));
        Assert.AreEqual((4, 0), curve.Accrue(1, 0, int.MaxValue));
        Assert.AreEqual((4, 0), curve.Accrue(1, int.MaxValue, int.MaxValue), "Huge grants don't overflow.");
    }

    [TestMethod]
    public void Accrue_IgnoresNothingAndLess()
    {
        var curve = Curve(100, 0);

        Assert.AreEqual((1, 40), curve.Accrue(1, 40, 0));
        Assert.AreEqual((1, 40), curve.Accrue(1, 40, -500));
    }

    [TestMethod]
    public void RankPercent_IsHowFarThroughTheLevel()
    {
        var curve = Curve(200, 300, 0);

        Assert.AreEqual(0, curve.GetRankPercent(1, 0));
        Assert.AreEqual(50, curve.GetRankPercent(1, 100));
        Assert.AreEqual(99, curve.GetRankPercent(1, 199), "Rounded down, so the bar never shows full before the level-up.");
        Assert.AreEqual(33, curve.GetRankPercent(2, 100));
        Assert.AreEqual(0, curve.GetRankPercent(3, 0), "The top level has no bar to fill.");
        Assert.AreEqual(100, curve.GetRankPercent(1, 900), "Stars over the level, after the curve shrinks, show as full.");
        Assert.AreEqual(0, curve.GetRankPercent(9, 10), "A level off the curve shows an empty bar.");
    }
}
