using System;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Interactions;

public sealed class NpcInteractionOption
{
    public required int IconId { get; init; }

    public required int ButtonTextId { get; init; }

    public int TooltipId { get; init; }

    /// <summary>
    /// Evergrove: the game the button opens (a ClientActivityDefinitions.json id), 0 for any other button. A world object's
    /// games (<see cref="Minigames.GameStationMenu"/>) leave out one that a quest button beside them already opens.
    /// </summary>
    public int ActivityId { get; init; }

    public required Action<Player> Invoke { get; init; }
}
