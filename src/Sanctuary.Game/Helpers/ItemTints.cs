using System;
using System.Collections.Generic;
using System.Linq;

using Sanctuary.Database.Entities;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// The tint an item is saved with when nothing chose one (a reward, a drop, a starter kit).
/// </summary>
public static class ItemTints
{
    /// <summary>
    /// The item's own tint: the one its icon shows, as starter kits have always used. Tintable clothes too: the
    /// client draws tint 0 as a colour of its own, so a white Striped T-Shirt saved with 0 was neon red.
    /// </summary>
    public static int Default(ClientItemDefinition itemDefinition) => itemDefinition.Icon.TintId;

    /// <summary>
    /// The character's rows saved with tint 0 although their item has a tint of its own, as rewards and pickups were
    /// until the Striped T-Shirt showed it, each with the tint it should have. A row is left as it is when the
    /// character already holds that item at that tint, since a character holds one row per item and tint, and when
    /// <paramref name="keep"/> says to keep the item's tint as it is.
    /// </summary>
    public static List<(DbItem Item, int Tint)> FindMistinted(IEnumerable<DbItem> items,
        IDictionary<int, ClientItemDefinition> itemDefinitions, Func<ClientItemDefinition, bool>? keep = null)
    {
        var rows = items.ToList();
        var held = rows.Select(item => (item.Definition, item.Tint)).ToHashSet();

        var mistinted = new List<(DbItem Item, int Tint)>();

        foreach (var item in rows)
        {
            if (item.Tint != 0 || !itemDefinitions.TryGetValue(item.Definition, out var itemDefinition) ||
                keep?.Invoke(itemDefinition) == true)
            {
                continue;
            }

            var tint = Default(itemDefinition);

            if (tint == 0 || !held.Add((item.Definition, tint)))
                continue;

            mistinted.Add((item, tint));
        }

        return mistinted;
    }
}
