namespace Sanctuary.Database.Entities;

/// <summary>
/// The two plots every account has (<c>docs/design/evergrove-housing.md</c>, section 2).
/// </summary>
public enum HousePlot
{
    /// <summary>The account's home: private unless the owner publishes it, and never in the competition.</summary>
    Personal = 0,

    /// <summary>Built to be visited and voted on: in the weekly competition while it's published.</summary>
    Ranked = 1
}
