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
}
