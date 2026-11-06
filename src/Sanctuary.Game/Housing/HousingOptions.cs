namespace Sanctuary.Game.Housing;

/// <summary>
/// The "Housing" section of the Gateway's config. Read once at startup, so a change needs a restart.
/// See <c>docs/design/evergrove-housing.md</c>, section 1.
/// </summary>
public sealed class HousingOptions
{
    public const string Section = "Housing";

    /// <summary>
    /// Building is free and unlimited. The editor offers every placeable fixture from
    /// <see cref="HousingCreativeCatalog"/>; placing one takes nothing from inventory, and picking it up gives nothing
    /// back, even after this is turned off. A fixture placed from inventory before still goes back to inventory when
    /// picked up. Off: fixtures are inventory items, bought in the store and used up when placed.
    /// </summary>
    public bool CreativeMode { get; set; } = true;

    /// <summary>
    /// The most catalog entries the editor's fixture list gets in creative mode; 0 sends them all. The whole catalog is
    /// about 1,700 entries in a 137 KB packet. The client takes the 208 KB store list on every zone entry, so the size
    /// is no problem for the connection, but how the editor copes with that many entries is untested. If it struggles,
    /// set a limit here: entries go in item definition order, so a limit keeps the lowest ids.
    /// </summary>
    public int CreativeCatalogLimit { get; set; }

    /// <summary>
    /// Lots cost nothing, in the store's price list and when ordered.
    /// </summary>
    public bool FreeLots { get; set; } = true;

    /// <summary>
    /// Evergrove: the lot every account owns free, by its zone id; 1024 is the Apartment (<c>docs/design/evergrove-housing.md</c>,
    /// section 11). The first world login of each session gives one to an account none of whose characters has one, on
    /// the character logging in, free whatever <see cref="FreeLots"/> says (<see cref="IHouseManager.EnsureStarterHouse"/>),
    /// and the Marketplace doesn't offer that lot (<see cref="StarterHouse.BundleLeftOffTheShelf"/>). 0 turns this off,
    /// and so does an id that isn't a housing lot the server serves, which the Gateway warns about as it starts.
    /// Samantha's Housing quest (9100) sends every new player into their house, so turn this off only while
    /// <see cref="FreeLots"/> is on: otherwise a new player has no house, and can't finish that quest or any after it. The
    /// Gateway warns about that too (<see cref="StarterHouse.Problems"/>).
    /// </summary>
    public int StarterHouseZoneId { get; set; } = 1024;
}
