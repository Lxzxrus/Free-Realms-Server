using System;

namespace Sanctuary.Core.Helpers;

/// <summary>
/// The ticket the Login server gives a character to enter the Gateway with. It is a version 7 Guid, which carries the
/// time it was made (to the millisecond) beside 74 random bits, so the Gateway can refuse an old one without a database
/// column of its own. A ticket was valid until the next login replaced it, however long that took (audit triage,
/// SEC-15). The client keeps it as 32 hex digits and connects to the Gateway right after it gets it.
/// </summary>
public static class GatewayTicket
{
    /// <summary>How long a ticket may wait to be used. The client uses it within seconds; this leaves room for a slow PC.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>How far ahead of the Gateway's clock a ticket's time may be. Login and Gateway share one host clock.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    public static Guid Create(DateTimeOffset now) => Guid.CreateVersion7(now);

    /// <summary>
    /// Whether <paramref name="ticket"/> was made by <see cref="Create"/> no longer than <see cref="Lifetime"/> before
    /// <paramref name="now"/>. A ticket of any other version (from before this change, say) is not.
    /// </summary>
    public static bool IsFresh(Guid ticket, DateTimeOffset now)
    {
        if (ticket.Version != 7)
            return false;

        Span<byte> bytes = stackalloc byte[16];

        if (!ticket.TryWriteBytes(bytes, bigEndian: true, out _))
            return false;

        long unixMilliseconds = 0;

        for (var i = 0; i < 6; i++)
            unixMilliseconds = unixMilliseconds << 8 | bytes[i];

        var issued = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);

        return issued <= now + ClockSkew && now - issued <= Lifetime;
    }
}
