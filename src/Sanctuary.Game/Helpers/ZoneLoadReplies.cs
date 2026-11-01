using System;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// When to answer a message the client sends once for each zone it loads: "client is ready" or "finished loading".
/// The first for each of the player's entries into a zone is always answered, a repeat for the same entry only after
/// <see cref="RepeatAfterMs"/>. Each answer costs the server far more than the message, so a client repeating it
/// multiplied that cost. A player's packets are handled one at a time, so it isn't locked.
/// </summary>
/// <remarks>
/// Kept per entry, not per zone: a player the server moves out of a zone and back before their client reported the zone
/// in between (sent out of a house they were still loading, say) loads the first zone again, and is owed its answers.
/// </remarks>
public sealed class ZoneLoadReplies
{
    /// <summary>A real client sends each message once per zone it loads, and loading a zone takes seconds.</summary>
    public const int RepeatAfterMs = 2000;

    private int? _zoneEntry;
    private long _repeatAt;

    /// <summary>
    /// Whether to answer the message for the player's entry into a zone, <paramref name="zoneEntry"/>
    /// (<see cref="Entities.Player.ZoneEntry"/>), at <paramref name="now"/> (<see cref="Environment.TickCount64"/>). If
    /// so, a repeat for that entry waits <see cref="RepeatAfterMs"/>.
    /// </summary>
    public bool TryAnswer(int zoneEntry, long now)
    {
        if (zoneEntry == _zoneEntry && now < _repeatAt)
            return false;

        _zoneEntry = zoneEntry;
        _repeatAt = now + RepeatAfterMs;
        return true;
    }
}
