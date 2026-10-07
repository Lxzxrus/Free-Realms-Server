namespace Sanctuary.UdpLibrary.Configuration;

/// <summary>
/// Limits for a UdpManager that players connect to (the Login and Gateway servers' player ports), bound from the
/// <c>Udp</c> configuration section, so <c>Udp__MaxConnectionsPerIp=0</c> in the environment overrides one. The
/// defaults are for a public server. A load test that runs every bot from one address needs the per-IP limits off.
/// </summary>
public sealed class PlayerUdpOptions
{
    public const string Section = "Udp";

    /// <summary>Kernel receive buffer for the socket. Linux caps it at <c>net.core.rmem_max</c>.</summary>
    public int IncomingBufferSize { get; set; } = 4 * 1024 * 1024;

    /// <summary>Kernel send buffer for the socket. Linux caps it at <c>net.core.wmem_max</c>.</summary>
    public int OutgoingBufferSize { get; set; } = 4 * 1024 * 1024;

    /// <summary>Largest packet a client may send, after reassembly from fragments.</summary>
    public int IncomingLogicalPacketMax { get; set; } = 64 * 1024;

    /// <summary>Bytes of fragmented packets one connection may have in reassembly at once.</summary>
    public int IncomingFragmentBytesMax { get; set; } = 64 * 1024;

    public int MaxConnectionsPerIp { get; set; } = 10;

    public int ConnectRatePerIp { get; set; } = 5;

    public int ConnectRateGlobal { get; set; } = 200;

    public int ConnectRateWindow { get; set; } = 10000;

    public int HandshakeTimeout { get; set; } = 10000;

    /// <summary>
    /// Replies per <see cref="UnverifiedReplyWindow"/> to an address that hasn't proven it owns it: the launcher's status
    /// ping, the confirm answering a connect request, and the unreachable reply. Players behind one router share it.
    /// </summary>
    public int UnverifiedReplyRatePerIp { get; set; } = 20;

    public int UnverifiedReplyWindow { get; set; } = 10000;

    /// <summary>
    /// Reliable data that may wait for one connection, sent or not yet sent, before it is disconnected for falling behind.
    /// Entering a zone queues about 4.2 MiB at once (3.5 MiB of it the coin store's item definitions), so this is a hard
    /// ceiling on memory per player, well above that, not the limit that catches a connection falling behind.
    /// </summary>
    public int ReliableOverflowBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>
    /// A connection with more than <see cref="ReliableBacklogBytes"/> waiting for it for this long without a break is
    /// disconnected. A healthy connection drains a zone entry in seconds, a slow one in well under a minute.
    /// </summary>
    public int ReliableBacklogTimeout { get; set; } = 60000;

    public int ReliableBacklogBytes { get; set; } = 512 * 1024;

    public int FaultLimit { get; set; } = 5;

    public int FaultWindow { get; set; } = 60000;

    public void ApplyTo(UdpParams udpParams)
    {
        udpParams.IncomingBufferSize = IncomingBufferSize;
        udpParams.OutgoingBufferSize = OutgoingBufferSize;
        udpParams.IncomingLogicalPacketMax = IncomingLogicalPacketMax;
        udpParams.IncomingFragmentBytesMax = IncomingFragmentBytesMax;
        udpParams.MaxConnectionsPerIp = MaxConnectionsPerIp;
        udpParams.ConnectRatePerIp = ConnectRatePerIp;
        udpParams.ConnectRateGlobal = ConnectRateGlobal;
        udpParams.ConnectRateWindow = ConnectRateWindow;
        udpParams.HandshakeTimeout = HandshakeTimeout;
        udpParams.UnverifiedReplyRatePerIp = UnverifiedReplyRatePerIp;
        udpParams.UnverifiedReplyWindow = UnverifiedReplyWindow;
        udpParams.ReliableOverflowBytes = ReliableOverflowBytes;
        udpParams.ReliableBacklogTimeout = ReliableBacklogTimeout;
        udpParams.ReliableBacklogBytes = ReliableBacklogBytes;
        udpParams.FaultLimit = FaultLimit;
        udpParams.FaultWindow = FaultWindow;
    }
}
