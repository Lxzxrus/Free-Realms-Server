using System;

namespace Sanctuary.Game.Helpers;

/// <summary>
/// When to answer a message the client sends once for each zone it loads: "client is ready" or "finished loading".
/// The first in a new zone is always answered, a repeat in the same zone only after <see cref="RepeatAfterMs"/>. Each
/// answer costs the server far more than the message, so a client repeating it multiplied that cost. A player's
/// packets are handled one at a time, so it isn't locked.
/// </summary>
public sealed class ZoneLoadReplies
{
    /// <summary>A real client sends each message once per zone it loads, and loading a zone takes seconds.</summary>
    public const int RepeatAfterMs = 2000;

    private object? _zone;
    private long _repeatAt;

    /// <summary>
    /// Whether to answer the message for <paramref name="zone"/> at <paramref name="now"/>
    /// (<see cref="Environment.TickCount64"/>). If so, a repeat in that zone waits <see cref="RepeatAfterMs"/>.
    /// </summary>
    public bool TryAnswer(object zone, long now)
    {
        if (ReferenceEquals(zone, _zone) && now < _repeatAt)
            return false;

        _zone = zone;
        _repeatAt = now + RepeatAfterMs;
        return true;
    }
}
