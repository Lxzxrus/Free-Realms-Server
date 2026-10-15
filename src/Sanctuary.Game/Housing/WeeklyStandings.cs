using System.Collections.Generic;
using System.Linq;

namespace Sanctuary.Game.Housing;

/// <summary>One ranked plot's votes in a competition week: how many, and their average in stars.</summary>
public readonly record struct PlotTally(int Votes, double Average);

/// <summary>
/// One ranked plot's standing in a competition week. <see cref="Score"/> is the vote-weighted average; a plot nobody
/// has voted for this week has no score yet (0), rather than the average of all plots.
/// </summary>
public readonly record struct PlotStanding(int Votes, double Average, double Score)
{
    public static readonly PlotStanding None = new(0, 0, 0);
}

/// <summary>
/// The week's score for every ranked plot in the competition (design section 3, Scoring):
/// <c>score = (v·R + m·C) / (v + m)</c>, where <c>R</c> is the plot's average this week, <c>v</c> its votes this week,
/// <c>C</c> the average of all ranked plots this week and <c>m</c> <see cref="CompetitionOptions.MinimumVotes"/>.
/// <c>C</c> is the average of every vote counted this week, so each plot weighs in by its votes: an account giving
/// 1 star to a few plots nobody else has voted for barely moves it.
/// </summary>
public sealed class WeeklyStandings
{
    private readonly IReadOnlyDictionary<ulong, PlotStanding> _plots;

    /// <summary><c>C</c>: the average of every vote counted this week across the competition, or 0 if there are none.</summary>
    public double OverallAverage { get; }

    /// <summary>Every ranked plot in the competition this week, by house id.</summary>
    public IReadOnlyDictionary<ulong, PlotStanding> Plots => _plots;

    private WeeklyStandings(IReadOnlyDictionary<ulong, PlotStanding> plots, double overallAverage)
    {
        _plots = plots;
        OverallAverage = overallAverage;
    }

    public static WeeklyStandings Empty { get; } = new(new Dictionary<ulong, PlotStanding>(), 0);

    /// <summary>A house's standing, or <see cref="PlotStanding.None"/> if it isn't in this week's competition.</summary>
    public PlotStanding For(ulong houseId)
    {
        return _plots.TryGetValue(houseId, out var standing) ? standing : PlotStanding.None;
    }

    /// <summary>
    /// <c>(v·R + m·C) / (v + m)</c>. 0 when <c>v + m</c> is 0, which only happens with no votes and <c>m</c> = 0.
    /// </summary>
    public static double Score(int votes, double average, int minimumVotes, double overallAverage)
    {
        var weight = votes + minimumVotes;

        return weight <= 0
            ? 0
            : (votes * average + minimumVotes * overallAverage) / weight;
    }

    /// <summary>
    /// Scores every plot in <paramref name="tallies"/>: all the ranked plots in this week's competition, including those
    /// with no votes yet.
    /// </summary>
    public static WeeklyStandings Compute(IReadOnlyDictionary<ulong, PlotTally> tallies, int minimumVotes)
    {
        var votes = tallies.Values.Sum(tally => (long)tally.Votes);
        var overallAverage = votes == 0 ? 0 : tallies.Values.Sum(tally => tally.Votes * tally.Average) / votes;

        var plots = tallies.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Votes > 0
                ? new PlotStanding(entry.Value.Votes, entry.Value.Average, Score(entry.Value.Votes, entry.Value.Average, minimumVotes, overallAverage))
                : PlotStanding.None);

        return new WeeklyStandings(plots, overallAverage);
    }
}
