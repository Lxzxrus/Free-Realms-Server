namespace Sanctuary.Game.Housing;

/// <summary>
/// A decorator's colour choice in creative mode: the dye new building parts come in, and that the Paint button of a
/// placed part's menu gives it (0 for each part's own colour).
/// </summary>
/// <remarks>
/// The colour bar of the Decorate panel (client mod, tools/client-mods/housing-search) sets it. A panel can only tell the
/// game which tray item was clicked, so each swatch sends a <see cref="CommandId"/> as if it were an item, and the
/// server reads the click back as the palette. The panel learns the current palette from the creative tray, whose
/// record ids carry it (<see cref="HousingCreativeCatalog.Entry.RecordId"/>).
/// </remarks>
public readonly record struct HousingPalette(int Dye)
{
    /// <summary>
    /// Command ids sit below <see cref="HousingCreativeCatalog.RecordIdBase"/> and far above any inventory record id.
    /// The client mod has the same numbers.
    /// </summary>
    public const int CommandIdBase = 0x3F00_0000;

    // Dye tint ids fit in 9 bits. Launchers 1.0.2 and 1.0.3 also sent a retired paint brush switch as bit 9; those ids
    // aren't commands, so the switch does nothing.
    private const int MaxDye = (1 << 9) - 1;

    public static HousingPalette None => default;

    public int CommandId => CommandIdBase | Dye;

    /// <summary>The palette a colour bar click asks for, if <paramref name="id"/> is one. Only the game's dye colours count.</summary>
    public static bool TryParseCommand(int id, out HousingPalette palette)
    {
        palette = None;

        var dye = id - CommandIdBase;
        if (dye is < 0 or > MaxDye || (dye != 0 && !HousingDyeTints.Names.ContainsKey(dye)))
            return false;

        palette = new HousingPalette(dye);
        return true;
    }

    /// <summary>
    /// The placed part a Paint click asks to paint, if <paramref name="id"/> is one. A placed part's menu has a Paint
    /// button (client mod, lua_patch.py) that sends the selected part's fixture guid, negated, as a placement request,
    /// which no item or catalog id can be.
    /// </summary>
    public static bool TryParsePaintCommand(int id, out ulong fixtureGuid)
    {
        fixtureGuid = id < 0 ? (ulong)-(long)id : 0;
        return id < 0;
    }

    /// <summary>The colour's name as players see it: "Rubyburst", or "each part's own colour" for none.</summary>
    public string ColourName => HousingDyeTints.Names.TryGetValue(Dye, out var name)
        ? char.ToUpperInvariant(name[0]) + name[1..]
        : "each part's own colour";
}
