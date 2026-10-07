namespace Sanctuary.UdpLibrary.Enumerations;

/// <summary>
/// Why a UdpManager ignored a connect request.
/// </summary>
public enum ConnectRefusal
{
    None,

    /// <summary>The manager already holds <c>MaxConnections</c>.</summary>
    ServerFull,

    /// <summary>The address already holds <c>MaxConnectionsPerIp</c>.</summary>
    AddressConnections,

    /// <summary>The address opened <c>ConnectRatePerIp</c> connections within the window.</summary>
    AddressRate,

    /// <summary>The manager accepted <c>ConnectRateGlobal</c> connections within the window.</summary>
    GlobalRate,

    /// <summary>The per-address table is full of addresses still inside their window.</summary>
    TooManyAddresses
}
