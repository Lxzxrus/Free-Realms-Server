using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.UdpLibrary.Internal;

/// <summary>
/// Decides whether a connect request may create a connection: connections held per IP address, and new connections
/// per IP and in total within a time window. Only the manager's GiveTime thread calls it, under the connection guard.
/// </summary>
internal sealed class ConnectLimiter
{
    // Bounds the per-address table when many addresses (spoofed or not) connect within one window.
    internal const int MaxTrackedAddresses = 65536;

    private readonly UdpParams _params;
    private readonly Dictionary<long, Entry> _entries = new();

    private UdpClockStamp _globalWindowStart;
    private int _globalCount;

    private struct Entry
    {
        public int Connections;
        public int RecentConnects;
        public UdpClockStamp WindowStart;
    }

    public ConnectLimiter(UdpParams udpParams)
    {
        _params = udpParams;
    }

    public bool Enabled => _params.MaxConnectionsPerIp > 0 || _params.ConnectRatePerIp > 0 || _params.ConnectRateGlobal > 0;

    public int TrackedAddresses => _entries.Count;

    public ConnectRefusal TryAdmit(SocketAddress socketAddress, UdpClockStamp now)
    {
        if (!Enabled)
            return ConnectRefusal.None;

        var window = _params.ConnectRateWindow;

        if (_params.ConnectRateGlobal > 0)
        {
            if (UdpMisc.ClockDiff(_globalWindowStart, now) >= window)
            {
                _globalWindowStart = now;
                _globalCount = 0;
            }

            if (_globalCount >= _params.ConnectRateGlobal)
                return ConnectRefusal.GlobalRate;
        }

        var key = AddressKey(socketAddress);

        if (!_entries.TryGetValue(key, out var entry))
        {
            if (_entries.Count >= MaxTrackedAddresses)
            {
                Prune(now);

                if (_entries.Count >= MaxTrackedAddresses)
                    return ConnectRefusal.TooManyAddresses;
            }

            entry.WindowStart = now;
        }

        if (UdpMisc.ClockDiff(entry.WindowStart, now) >= window)
        {
            entry.WindowStart = now;
            entry.RecentConnects = 0;
        }

        if (_params.ConnectRatePerIp > 0 && entry.RecentConnects >= _params.ConnectRatePerIp)
            return ConnectRefusal.AddressRate;

        if (_params.MaxConnectionsPerIp > 0 && entry.Connections >= _params.MaxConnectionsPerIp)
            return ConnectRefusal.AddressConnections;

        entry.RecentConnects++;
        _entries[key] = entry;

        _globalCount++;

        return ConnectRefusal.None;
    }

    public void ConnectionAdded(SocketAddress socketAddress, UdpClockStamp now)
    {
        if (!Enabled)
            return;

        var key = AddressKey(socketAddress);

        if (!_entries.TryGetValue(key, out var entry))
            entry.WindowStart = now;

        entry.Connections++;
        _entries[key] = entry;
    }

    public void ConnectionRemoved(SocketAddress socketAddress, UdpClockStamp now)
    {
        if (!Enabled)
            return;

        var key = AddressKey(socketAddress);

        if (!_entries.TryGetValue(key, out var entry))
            return;

        entry.Connections--;

        if (entry.Connections <= 0 && UdpMisc.ClockDiff(entry.WindowStart, now) >= _params.ConnectRateWindow)
            _entries.Remove(key);
        else
            _entries[key] = entry;
    }

    public int ConnectionsFrom(SocketAddress socketAddress)
    {
        return _entries.TryGetValue(AddressKey(socketAddress), out var entry) ? entry.Connections : 0;
    }

    /// <summary>
    /// Forgets addresses with no connections whose rate window has passed.
    /// </summary>
    public void Prune(UdpClockStamp now)
    {
        List<long>? stale = null;

        foreach (var (key, entry) in _entries)
        {
            if (entry.Connections <= 0 && UdpMisc.ClockDiff(entry.WindowStart, now) >= _params.ConnectRateWindow)
                (stale ??= []).Add(key);
        }

        if (stale is null)
            return;

        foreach (var key in stale)
            _entries.Remove(key);
    }

    /// <summary>
    /// The IP address without the port. IPv4 sockaddr: family (2 bytes), port (2), address (4).
    /// </summary>
    internal static long AddressKey(SocketAddress socketAddress)
    {
        var buffer = socketAddress.Buffer.Span;

        if (socketAddress.Family == AddressFamily.InterNetwork && socketAddress.Size >= 8)
            return BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4));

        // Not used by the platform drivers (they open IPv4 sockets only); key by the whole address, so the limits
        // apply per address and port instead.
        return (1L << 32) | (uint)socketAddress.GetHashCode();
    }
}
