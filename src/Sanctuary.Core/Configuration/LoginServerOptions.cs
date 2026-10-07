using System.ComponentModel.DataAnnotations;

namespace Sanctuary.Core.Configuration;

public sealed class LoginServerOptions : ServerOptions
{
    [Required]
    public required int LoginGatewayPort { get; set; }

    /// <summary>
    /// IPv4 address the LoginGateway listener binds. Only Gateways may reach it, so by default it is only
    /// reachable from this machine. Use <c>0.0.0.0</c> only when the Gateway runs elsewhere and the port is
    /// firewalled to it, as in Docker Compose.
    /// </summary>
    public string LoginGatewayBindAddress { get; set; } = "127.0.0.1";

    [Required]
    public required string LoginGatewayChallenge { get; set; }

    /// <summary>
    /// Locks the server and only allows admins to login.
    /// </summary>
    public bool IsLocked { get; set; }

    public int DefaultTitleId { get; set; }

    [Required]
    public required int DefaultProfileId { get; set; }

    public int StartingCoins { get; set; }
    public int StartingStationCash { get; set; }

    public bool UnlockAllTitles { get; set; }
    public bool UnlockAllProfiles { get; set; }
}