using System;

using Sanctuary.Game.Entities;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// How often a player's requests may take them to another zone: into or out of a house, to a point of interest or to
/// safety from a house or a fishing hole, or to a friend in another zone. Each move ends with the new zone's whole
/// welcome (about 4.2 MiB, PlayerUdpOptions), so a client sending them back to back made the server send one every
/// time. A move asked for sooner than <see cref="MinIntervalMs"/> after the last one isn't refused: it waits, and the
/// main loop makes it when its turn comes (<see cref="Tick"/>). The client waits for a move it asked for (up to a
/// minute for the move into a house, and it hides the house's exit bar as it asks to leave), so refusing one would
/// leave a real client stuck.
/// </summary>
/// <remarks>
/// Only the latest move waits: a client that asks again has changed its mind, and nothing waits for the earlier one.
/// Teleports within a zone aren't limited: they cost little, and the client's position updates can move it anywhere
/// anyway. Neither are the server's own moves, such as sending visitors out of a house its owner locked.
/// </remarks>
public static class ZoneTeleports
{
    /// <summary>The shortest time between two of a player's moves to another zone.</summary>
    public const int MinIntervalMs = 3000;

    /// <summary>
    /// Makes <paramref name="move"/> at once if the player's last move to another zone was at least
    /// <see cref="MinIntervalMs"/> ago, otherwise keeps it, in place of any move already waiting, until then.
    /// </summary>
    /// <param name="now"><see cref="Environment.TickCount64"/>.</param>
    /// <param name="move">Moves the player, or tells them why it can't: a move refused starts no wait.</param>
    public static void Request(Player player, long now, Action move)
    {
        if (now < player.NextZoneTeleportAt)
        {
            player.WaitingZoneMove = move;
            return;
        }

        player.WaitingZoneMove = null;
        Move(player, move, now);
    }

    /// <summary>
    /// Makes the player's waiting move once its turn has come. On the main loop, the thread that handles their packets.
    /// </summary>
    /// <param name="now"><see cref="Environment.TickCount64"/>.</param>
    public static void Tick(Player player, long now)
    {
        if (player.WaitingZoneMove is not { } move || now < player.NextZoneTeleportAt)
            return;

        player.WaitingZoneMove = null;
        Move(player, move, now);
    }

    private static void Move(Player player, Action move, long now)
    {
        var zone = player.Zone;

        try
        {
            move();
        }
        finally
        {
            // Only a move that happened starts the wait, even if something after it threw: one refused, or to where
            // the player already is, costs little, and must not hold up the next real one.
            if (!ReferenceEquals(player.Zone, zone))
                player.NextZoneTeleportAt = now + MinIntervalMs;
        }
    }
}
