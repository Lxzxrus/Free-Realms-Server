using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Housing;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class WeeklyStandingsTests
{
    private const double Tolerance = 1e-9;

    [TestMethod]
    public void Score_IsTheVoteWeightedAverage()
    {
        // (v·R + m·C) / (v + m) = (4 × 4.5 + 10 × 3) / 14
        Assert.AreEqual(48.0 / 14, WeeklyStandings.Score(votes: 4, average: 4.5, minimumVotes: 10, overallAverage: 3), Tolerance);

        // Many votes: the plot's own average dominates.
        Assert.AreEqual((1000 * 2.0 + 10 * 4.0) / 1010, WeeklyStandings.Score(1000, 2, 10, 4), Tolerance);
    }

    [TestMethod]
    public void Score_WithMZero_IsThePlainAverage_AndNeverDividesByZero()
    {
        Assert.AreEqual(4.25, WeeklyStandings.Score(votes: 4, average: 4.25, minimumVotes: 0, overallAverage: 3), Tolerance);
        Assert.AreEqual(0, WeeklyStandings.Score(votes: 0, average: 0, minimumVotes: 0, overallAverage: 3));
    }

    [TestMethod]
    public void Compute_CIsTheAverageOfEveryVoteThisWeek()
    {
        var standings = WeeklyStandings.Compute(
            new Dictionary<ulong, PlotTally>
            {
                [1] = new(Votes: 2, Average: 5),
                [2] = new(Votes: 6, Average: 3),
                [3] = new(Votes: 0, Average: 0)
            },
            minimumVotes: 10);

        // Every vote, not the plots' averages: (2 × 5 + 6 × 3) / 8. Plot 3 has none.
        Assert.AreEqual(3.5, standings.OverallAverage, Tolerance);

        Assert.AreEqual((2 * 5.0 + 10 * 3.5) / 12, standings.For(1).Score, Tolerance);
        Assert.AreEqual((6 * 3.0 + 10 * 3.5) / 16, standings.For(2).Score, Tolerance);
        Assert.AreEqual(2, standings.For(1).Votes);
        Assert.AreEqual(5.0, standings.For(1).Average, Tolerance);
    }

    /// <summary>
    /// One eligible account giving 1 star to a few plots nobody else voted for mustn't swing the ranking, which it
    /// would if <c>C</c> weighed each plot the same.
    /// </summary>
    [TestMethod]
    public void Compute_AFewOneStarVotesBarelyMoveC()
    {
        var field = new Dictionary<ulong, PlotTally>
        {
            [1] = new(Votes: 20, Average: 4.5),
            [2] = new(Votes: 5, Average: 5.0)
        };

        var before = WeeklyStandings.Compute(field, minimumVotes: 10);

        for (ulong houseId = 3; houseId <= 6; houseId++)
            field[houseId] = new(Votes: 1, Average: 1);

        var after = WeeklyStandings.Compute(field, minimumVotes: 10);

        Assert.IsTrue(before.For(2).Score > before.For(1).Score);
        Assert.IsTrue(after.For(2).Score > after.For(1).Score, $"{after.For(2).Score} against {after.For(1).Score}.");
        Assert.AreEqual((90 + 25 + 4) / 29.0, after.OverallAverage, Tolerance);
    }

    [TestMethod]
    public void Compute_APlotWithNoVotesHasNoScore_AndAHouseNotEnteredHasNoStanding()
    {
        var standings = WeeklyStandings.Compute(
            new Dictionary<ulong, PlotTally> { [1] = new(3, 4), [2] = new(0, 0) },
            minimumVotes: 10);

        Assert.AreEqual(PlotStanding.None, standings.For(2), "No votes: the client shows no rating, as it did before.");
        Assert.IsTrue(standings.Plots.ContainsKey(2), "Still entered.");
        Assert.AreEqual(PlotStanding.None, standings.For(99));
        Assert.IsFalse(standings.Plots.ContainsKey(99));

        Assert.AreEqual(0, WeeklyStandings.Compute(new Dictionary<ulong, PlotTally>(), 10).OverallAverage);
    }

    /// <summary>
    /// The design's reason for the formula: a plain average lets three 5-star votes beat two hundred votes averaging
    /// 4.8. Among a field of plots, the weighted score ranks the well-supported plot first.
    /// </summary>
    [TestMethod]
    public void Compute_ManyGoodVotesBeatAFewPerfectOnes()
    {
        var standings = WeeklyStandings.Compute(
            new Dictionary<ulong, PlotTally>
            {
                [1] = new(Votes: 3, Average: 5.0),
                [2] = new(Votes: 200, Average: 4.8),
                [3] = new(Votes: 40, Average: 3.2),
                [4] = new(Votes: 25, Average: 3.6)
            },
            minimumVotes: 10);

        Assert.IsTrue(standings.For(1).Average > standings.For(2).Average, "The plain average puts plot 1 first.");
        Assert.IsTrue(standings.For(2).Score > standings.For(1).Score, $"{standings.For(2).Score} against {standings.For(1).Score}.");
    }
}
