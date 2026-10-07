using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sanctuary.Core.Configuration;

/// <summary>
/// Settings every server checks after reading its configuration and before it opens a port.
/// See <c>docs/security/threat-model.md</c>.
/// </summary>
public static class StartupChecks
{
    /// <summary>
    /// Top-level setting that lets a Debug build start (F1).
    /// </summary>
    public const string AllowDebugBuildKey = "AllowDebugBuild";

    /// <summary>
    /// OSFR's <c>LoginGatewayChallenge</c>. It is in OSFR's repository and in ours, so it is never accepted.
    /// </summary>
    public const string PublicLoginGatewayChallenge = "OSFR-EDITz@2024";

    /// <summary>
    /// Returns why the server must not start, or <c>null</c> if the build is allowed to run.
    /// A Debug build skips the login ticket and session checks and makes every player an Admin, so it only
    /// runs when <see cref="AllowDebugBuildKey"/> is explicitly <c>true</c>.
    /// </summary>
    public static string? CheckBuild(bool isDebugBuild, IConfiguration configuration)
    {
        if (!isDebugBuild)
            return null;

        if (bool.TryParse(configuration[AllowDebugBuildKey], out var allowDebugBuild) && allowDebugBuild)
            return null;

        return $"this is a Debug build, which skips login checks and makes every player an Admin. "
            + $"Build with -c Release, or set {AllowDebugBuildKey}=true for a private test server.";
    }

    /// <summary>
    /// Returns why the server must not start, or <c>null</c> if the Login↔Gateway challenge is usable (F2).
    /// The challenge is the only thing the Login server checks before it trusts a Gateway, so it must be set,
    /// and it must not be OSFR's default, which is public.
    /// </summary>
    public static string? CheckLoginGatewayChallenge(string? challenge)
    {
        const string howToSet = "Set the same random value for Login and Gateway in login.local.json and "
            + "gateway.local.json, or in the environment variable Server__LoginGatewayChallenge "
            + "(for example: openssl rand -base64 32).";

        if (string.IsNullOrWhiteSpace(challenge))
            return $"Server:LoginGatewayChallenge is not set. {howToSet}";

        if (string.Equals(challenge.Trim(), PublicLoginGatewayChallenge, StringComparison.OrdinalIgnoreCase))
            return $"Server:LoginGatewayChallenge is OSFR's public default, which anyone can use. {howToSet}";

        return null;
    }

    /// <summary>
    /// Returns why the server must not start, or <c>null</c> if the LoginGateway listener can bind this
    /// address (F3). The socket is IPv4 only.
    /// </summary>
    public static string? CheckLoginGatewayBindAddress(string? address)
    {
        if (IPAddress.TryParse(address, out var ipAddress) && ipAddress.AddressFamily == AddressFamily.InterNetwork)
            return null;

        return $"Server:LoginGatewayBindAddress \"{address}\" is not an IPv4 address. "
            + "Use 127.0.0.1 when the Gateway runs on this machine.";
    }

    /// <summary>
    /// Logs every failure and returns <c>false</c> if there is any. Otherwise returns <c>true</c>, and logs a
    /// warning first if this is an allowed Debug build.
    /// </summary>
    public static bool Passes(ILogger logger, bool isDebugBuild, params string?[] failures)
    {
        var reasons = failures.OfType<string>().ToList();

        foreach (var reason in reasons)
            logger.LogCritical("Refusing to start: {Reason}", reason);

        if (reasons.Count > 0)
            return false;

        if (isDebugBuild)
        {
            logger.LogWarning("""

                ************************************************************
                * DEBUG BUILD. Login checks are off and every player is an *
                * Admin. Never let anyone outside your home network in.    *
                ************************************************************
                """);
        }

        return true;
    }
}
