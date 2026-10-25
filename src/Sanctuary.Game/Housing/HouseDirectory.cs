namespace Sanctuary.Game.Housing;

/// <summary>The house directory: which houses visitors can find and rate.</summary>
public static class HouseDirectory
{
    /// <summary>
    /// Whether a house is in the directory: published and unlocked. A published house that's locked stays out until it's
    /// unlocked, and the client shows it as not submitted, with no rating panel for visitors.
    /// </summary>
    public static bool IsListed(bool isPublished, bool isLocked) => isPublished && !isLocked;
}
