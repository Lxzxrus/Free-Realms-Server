using System.Collections.Generic;
using System.Linq;

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
