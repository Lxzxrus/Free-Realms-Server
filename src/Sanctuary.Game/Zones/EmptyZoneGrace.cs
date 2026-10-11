using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Sanctuary.Game.Zones;

/// <summary>
/// Keeps track of empty zones waiting out their grace period before they close. Each time a zone empties it gets a
/// new ticket; only the latest ticket may close it. So a zone that empties, fills and empties again gets a full grace
/// period from the last time, not from the first. Zones are told apart by reference.
/// </summary>
internal sealed class EmptyZoneGrace
{
    private readonly ConcurrentDictionary<object, long> _tickets = new(ReferenceEqualityComparer.Instance);
    private long _lastTicket;

    /// <summary>Starts (or restarts) the zone's grace period.</summary>
    public long Begin(object zone)
    {
        var ticket = Interlocked.Increment(ref _lastTicket);
        _tickets[zone] = ticket;
        return ticket;
    }

    /// <summary>Ends the grace period. True if <paramref name="ticket"/> is still the zone's latest one.</summary>
    public bool End(object zone, long ticket)
    {
        return _tickets.TryRemove(new KeyValuePair<object, long>(zone, ticket));
    }
}
