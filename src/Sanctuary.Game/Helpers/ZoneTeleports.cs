using System;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// How often a player's teleport requests may take them to another zone: to a point of interest or to safety from a
/// house or a fishing hole, or to a friend in another zone. Each move ends with the new zone's whole welcome (about
/// 4.2 MiB, PlayerUdpOptions), so a client sending them back to back made the server send one every time. A real
/// client loads the new zone in between, behind its loading screen, and waits for nothing after asking: a refused
/// request just does nothing.
/// </summary>
/// <remarks>
/// Teleports within a zone aren't limited: they cost little, and the client's position updates can move it anywhere
/// anyway. Neither are entering and leaving a house: the client acts on those before the server answers (it waits up
/// to a minute for the move into a house, and hides the house's exit bar as it asks to leave), so refusing one would
/// leave a real client stuck.
/// </remarks>
public static class ZoneTeleports
{
    /// <summary>The shortest time between two of a player's teleports to another zone.</summary>
    public const int MinIntervalMs = 3000;

    /// <summary>
    /// Whether the player may teleport to another zone now. If so, the next must wait <see cref="MinIntervalMs"/>.
    /// </summary>
    public static bool TryStart(Player player)
    {
        var now = Environment.TickCount64;

        if (now < player.NextZoneTeleportAt)
            return false;

        player.NextZoneTeleportAt = now + MinIntervalMs;
        return true;
    }
}
