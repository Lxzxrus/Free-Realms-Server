using System.Collections.Generic;
using System.Net;

using Sanctuary.UdpLibrary.Configuration;

namespace Sanctuary.UdpLibrary.Internal;

/// <summary>
/// Counts the replies sent to each IP address that hasn't proven it owns that address, within a time window (see
/// <see cref="UdpParams.UnverifiedReplyRatePerIp"/>). Callers hold the manager's reply guard.
/// </summary>
internal sealed class ReplyLimiter
{
    // Bounds the per-address table when requests arrive from many (forged) addresses within one window. When it's full
    // and nothing can be pruned, the request goes unanswered: refusing a reply is always safe.
    internal const int MaxTrackedAddresses = 65536;

    private readonly UdpParams _params;
    private readonly Dictionary<long, Entry> _entries = new();

    private struct Entry
    {
        public int Replies;
        public UdpClockStamp WindowStart;
    }

    public ReplyLimiter(UdpParams udpParams)
    {
        _params = udpParams;
    }

    public int TrackedAddresses => _entries.Count;

    public bool TryAdmit(SocketAddress socketAddress, UdpClockStamp now)
    {
        if (_params.UnverifiedReplyRatePerIp <= 0)
            return true;

        var key = ConnectLimiter.AddressKey(socketAddress);

        if (!_entries.TryGetValue(key, out var entry))
        {
            if (_entries.Count >= MaxTrackedAddresses)
            {
                Prune(now);

                if (_entries.Count >= MaxTrackedAddresses)
                    return false;
            }

            entry.WindowStart = now;
        }

        if (UdpMisc.ClockDiff(entry.WindowStart, now) >= _params.UnverifiedReplyWindow)
        {
            entry.WindowStart = now;
            entry.Replies = 0;
        }

        if (entry.Replies >= _params.UnverifiedReplyRatePerIp)
            return false;

        entry.Replies++;
        _entries[key] = entry;

        return true;
    }

    /// <summary>
    /// Forgets addresses whose window has passed.
    /// </summary>
    public void Prune(UdpClockStamp now)
    {
        List<long>? stale = null;

        foreach (var (key, entry) in _entries)
        {
            if (UdpMisc.ClockDiff(entry.WindowStart, now) >= _params.UnverifiedReplyWindow)
                (stale ??= []).Add(key);
        }

        if (stale is null)
            return;

        foreach (var key in stale)
            _entries.Remove(key);
    }
}
