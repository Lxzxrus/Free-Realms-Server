namespace Sanctuary.Game.Housing;

/// <summary>
/// Evergrove: what a player who couldn't go into a house is told, whichever way they tried (<c>!house enter</c> and
/// <c>!house visit</c>, a Housing door), so each says the same.
/// </summary>
public static class HouseEntryMessages
{
    /// <summary>The chat line for <paramref name="result"/>, or <c>null</c> when the player went in.</summary>
    public static string? For(EnterHouseResult result) => result switch
    {
        EnterHouseResult.Success => null,
        EnterHouseResult.HouseNotFound => "That house is not available.",
        EnterHouseResult.NotAuthorized => "Only the owner and their friends can enter that house.",
        EnterHouseResult.UnsupportedSourceZone => "Return to the main world before entering a house.",
        EnterHouseResult.ZoneUnavailable => "The house instance could not be created.",
        EnterHouseResult.TransferFailed => "The house transfer failed.",
        _ => "The house could not be entered."
    };
}
