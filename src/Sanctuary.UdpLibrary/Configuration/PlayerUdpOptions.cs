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

    /// <summary>
    /// The smallest packet size a connect request may ask for; one asking for less is ignored. The servers send packets
    /// of up to 512 bytes. Half of that leaves room for a client that asks for less than the usual, while keeping what
    /// a send costs the server under three times the usual (<c>MinRawPacketSizeTests</c> measures it). 0 = no minimum.
    /// </summary>
    public int MinRawPacketSize { get; set; } = 256;

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

    /// <summary>
    /// Packets one connection may have waiting for their handlers. Only the Gateway queues them (it runs its handlers after
    /// the UDP library's locks are released). A real client sends a few packets a second, but the first value, 512,
    /// disconnected a player every time they entered their apartment (25 November 2026): more than 512 waited at once
    /// while the Gateway built the zone. Which packets they were wasn't recorded; the client wrote about 260 log lines in
    /// that second, and it can send log lines to the server. So this is set well above it, and
    /// <see cref="MaxQueuedBytesPerConnection"/> bounds the memory; the warning now lists the commonest sizes waiting.
    /// </summary>
    public int MaxQueuedPacketsPerConnection { get; set; } = 4096;

    /// <summary>
    /// Memory one connection's waiting packets may hold, counted as <c>UdpManager.QueuedPacketCost</c> does (at least 640
    /// bytes a packet with the Gateway's settings). 2 MiB holds about 3,200 small packets, or 32 of the largest.
    /// </summary>
    public int MaxQueuedBytesPerConnection { get; set; } = 2 * 1024 * 1024;

    /// <summary>
    /// <see cref="MaxQueuedPacketsPerConnection"/> until a player's login to the Gateway has succeeded. The limits above
    /// are sized for a player loading a zone; a connection that hasn't logged in has one packet to send, its login. 0 =
    /// the same limit from the start. The Login server doesn't queue its packets, so this does nothing there.
    /// </summary>
    public int PreLoginMaxQueuedPackets { get; set; } = 32;

    /// <summary>
    /// <see cref="MaxQueuedBytesPerConnection"/> until a player's login to the Gateway has succeeded. A real client's
    /// login packet is about 60 bytes and counts as 640 here, like any small packet, so
    /// <see cref="PreLoginMaxQueuedPackets"/> is the limit that small packets reach first; this one stops large ones.
    /// 0 = the same limit from the start; a value under 640 would refuse every packet, so the server won't start with one.
    /// </summary>
    public int PreLoginMaxQueuedBytes { get; set; } = 32 * 1024;

    /// <summary>
    /// How far out of order a reliable packet may arrive, and be kept, until the connection has logged in (on the Login
    /// server, signed in). The full window is 400 packets on each of four channels, each kept in a 512-byte buffer;
    /// a client that hasn't logged in sends one or two packets. Packets that arrive in order aren't affected, and one
    /// too far ahead is sent again by the client. 0 = the full window from the start.
    /// </summary>
    public int PreLoginReliableWindow { get; set; } = 16;

    /// <summary>
    /// <see cref="ReliableOverflowBytes"/> until the connection has logged in (on the Login server, signed in). Before
    /// then the servers only send small answers (a refusal, the server list), so a connection with this much waiting for
    /// it keeps asking and never takes the answers. 0 = the same limit from the start.
    /// </summary>
    public int PreLoginReliableOverflowBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// A connection that hasn't logged in (on the Login server, signed in) within this long of connecting is
    /// disconnected, whatever it has sent. A capture of the original game shows the client sending its login to the
    /// gateway a tenth of a second after connecting, and in local runs with the game client the whole stay on the Login
    /// server, character select included, took about 30 s; nothing records when in that stay it signs in, so this is
    /// twice that. A login still waiting in the Gateway's queue is not cut off. 0 = off.
    /// </summary>
    public int PreLoginTimeout { get; set; } = 60000;

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
        udpParams.MinRawPacketSize = MinRawPacketSize;
        udpParams.HandshakeTimeout = HandshakeTimeout;
        udpParams.UnverifiedReplyRatePerIp = UnverifiedReplyRatePerIp;
        udpParams.UnverifiedReplyWindow = UnverifiedReplyWindow;
        udpParams.ReliableOverflowBytes = ReliableOverflowBytes;
        udpParams.ReliableBacklogTimeout = ReliableBacklogTimeout;
        udpParams.ReliableBacklogBytes = ReliableBacklogBytes;
        udpParams.FaultLimit = FaultLimit;
        udpParams.FaultWindow = FaultWindow;
        udpParams.MaxQueuedPacketsPerConnection = MaxQueuedPacketsPerConnection;
        udpParams.MaxQueuedBytesPerConnection = MaxQueuedBytesPerConnection;
        udpParams.StartingMaxQueuedPacketsPerConnection = PreLoginMaxQueuedPackets;
        udpParams.StartingMaxQueuedBytesPerConnection = PreLoginMaxQueuedBytes;
        udpParams.StartingReliableWindow = PreLoginReliableWindow;
        udpParams.StartingReliableOverflowBytes = PreLoginReliableOverflowBytes;
        udpParams.StartingTimeout = PreLoginTimeout;
    }
}
