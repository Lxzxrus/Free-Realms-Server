namespace Sanctuary.UdpLibrary.Configuration;

/// <summary>
/// Limits for a UdpManager that players connect to (the Login and Gateway servers' player ports), bound from the
/// <c>Udp</c> configuration section, so <c>Udp__MaxConnectionsPerIp=0</c> in the environment overrides one. The
/// defaults are for a public server. A load test that runs every bot from one address needs the per-IP limits off.
/// </summary>
public sealed class PlayerUdpOptions
{
    public const string Section = "Udp";

    /// <summary>Largest packet a client may send, after reassembly from fragments.</summary>
    public int IncomingLogicalPacketMax { get; set; } = 64 * 1024;

    /// <summary>Bytes of fragmented packets one connection may have in reassembly at once.</summary>
    public int IncomingFragmentBytesMax { get; set; } = 64 * 1024;

    public int MaxConnectionsPerIp { get; set; } = 10;

    public int ConnectRatePerIp { get; set; } = 5;

    public int ConnectRateGlobal { get; set; } = 200;

    public int ConnectRateWindow { get; set; } = 10000;

    public int HandshakeTimeout { get; set; } = 10000;

    public int FaultLimit { get; set; } = 5;

    public int FaultWindow { get; set; } = 60000;

    public void ApplyTo(UdpParams udpParams)
    {
        udpParams.IncomingLogicalPacketMax = IncomingLogicalPacketMax;
        udpParams.IncomingFragmentBytesMax = IncomingFragmentBytesMax;
        udpParams.MaxConnectionsPerIp = MaxConnectionsPerIp;
        udpParams.ConnectRatePerIp = ConnectRatePerIp;
        udpParams.ConnectRateGlobal = ConnectRateGlobal;
        udpParams.ConnectRateWindow = ConnectRateWindow;
        udpParams.HandshakeTimeout = HandshakeTimeout;
        udpParams.FaultLimit = FaultLimit;
        udpParams.FaultWindow = FaultWindow;
    }
}
