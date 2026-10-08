using System;
using System.Linq;

using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Housing;
using Sanctuary.Game.Zones;

namespace Sanctuary.Game.ChatCommands;

/// <summary>
/// Searches and dyes the creative building tray, which has no search box of its own.
/// </summary>
public sealed class BuildChatCommand : IChatCommand
{
    public string KeyWord => "build";
    public string Usage => "<words> [colour] | colors | all";
    public string Description => "Show only building parts matching the words, optionally in a dye colour.";
    public ChatCommandRole RequiredRole => ChatCommandRole.Player;

    public bool Handle(Player invoker, string[] args)
    {
        if (args.Length == 0)
            return false;

        if (args.Length == 1 && args[0].ToLowerInvariant() is "colors" or "colours")
        {
            ChatHelper.SendSystemMessage(invoker, "Colours: " + string.Join(", ", HousingDyeTints.Names.Values));
            return true;
        }

        if (invoker.Zone is not HousingZone house)
        {
            ChatHelper.SendSystemMessage(invoker, "Use !build inside your own lot.");
            return true;
        }

        var terms = args.ToList();
        var dye = 0;
        if (terms.Count == 1 && terms[0].Equals("all", StringComparison.OrdinalIgnoreCase))
            terms.Clear();
        else if (terms.Count > 0 && HousingDyeTints.TryGetId(terms[^1], out dye))
            terms.RemoveAt(terms.Count - 1);

        var matches = house.Runtime.SetCreativeView(invoker, [.. terms], dye);
        if (matches < 0)
        {
            ChatHelper.SendSystemMessage(invoker, "Only the owner can change the building parts here.");
            return true;
        }

        var what = terms.Count == 0 ? "all parts" : $"parts matching \"{string.Join(' ', terms)}\"";
        var colour = dye == 0 ? string.Empty : $", dyeable ones in {HousingDyeTints.Names[dye]}";
        ChatHelper.SendSystemMessage(invoker, $"Showing {matches} {what}{colour}. Open Decorate to see them; !build all resets.");
        return true;
    }
}
