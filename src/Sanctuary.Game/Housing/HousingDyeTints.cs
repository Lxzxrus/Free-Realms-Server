using System;
using System.Collections.Generic;
using System.Linq;

using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Housing;

/// <summary>
/// The game's dye palette ("dyetint", the tint set the Marketplace offers for dyeable items), by tint id. Names are
/// the game's own tint names without the "dyetint-" prefix.
/// </summary>
public static class HousingDyeTints
{
    public static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [227] = "rubyburst",
        [228] = "thunderbird",
        [229] = "mahogany",
        [230] = "macaroni",
        [231] = "sunrise",
        [232] = "allspice",
        [233] = "sandrift",
        [234] = "commoner",
        [235] = "toasty",
        [236] = "honeydew",
        [237] = "turbo",
        [238] = "lucky",
        [239] = "electrolime",
        [240] = "jungle",
        [241] = "woodland",
        [242] = "mantis",
        [243] = "cloverleaf",
        [244] = "forest",
        [245] = "aqua",
        [246] = "azure",
        [247] = "sapphire",
        [248] = "icy",
        [249] = "ocean",
        [250] = "midnight",
        [251] = "skylight",
        [252] = "raindrop",
        [253] = "blizzard",
        [254] = "amethyst",
        [255] = "berrybright",
        [256] = "twilight",
        [257] = "blossom",
        [258] = "bubblegum",
        [259] = "rosepetal",
        [260] = "stonesurf",
        [261] = "shadestone",
        [262] = "stormcloud",
        [263] = "snowfall",
        [264] = "darkmatter"
    };

    public static bool TryGetId(string name, out int tintId)
    {
        tintId = Names.FirstOrDefault(pair => string.Equals(pair.Value, name, StringComparison.OrdinalIgnoreCase)).Key;
        return tintId != 0;
    }

    public static bool IsDyeable(ClientItemDefinition definition)
    {
        return definition.IsTintable && string.Equals(definition.TintAlias, "dyetint", StringComparison.OrdinalIgnoreCase);
    }
}
