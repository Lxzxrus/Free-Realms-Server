using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Launcher.Helpers;

public static class Constants
{
    public const string LogFile = "Launcher.log";
    public const string SettingsFile = "Launcher.xml";

    public const string ServersDirectory = "Servers";

    public const string ClientExecutableName = "FreeRealms.exe";
    public const string DirectXDownloadUrl = "https://www.microsoft.com/download/details.aspx?id=35";

    /// <summary>
    /// The official Open Source Free Realms client download. Missing or changed client files are repaired from here, and
    /// written only if they match <see cref="ClientPinSet.Official"/>.
    /// </summary>
    public const string OfficialClientUrl = "https://opensourcefreerealms.com/client/";

    public const string QuarantineDirectory = "Quarantine";

    // Set at build time in launcher/src/Directory.Build.props.
    public static readonly string Title = GetMetadata("LauncherTitle");
    public static readonly string Id = GetMetadata("LauncherId");
    public static readonly string DefaultServerUrl = GetMetadata("DefaultServerUrl");

    /// <summary>
    /// Launcher updates come from this repository's GitHub Releases (see <see cref="UpdateSource"/>), not from
    /// <see cref="DefaultServerUrl"/>.
    /// </summary>
    public static readonly string UpdateRepositoryUrl = GetMetadata("UpdateRepositoryUrl");

    /// <summary>
    /// The expected SHA-256 of <see cref="ClientExecutableName"/> (see <see cref="ClientPin"/>). Empty when the build
    /// didn't set it, and then no game is started.
    /// </summary>
    public static readonly string ClientExecutableSha256 = GetMetadata("ClientExecutableSha256", required: false);

    public static readonly string LogsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

    // Create: without it, a missing folder (a fresh Linux account has no ~/.local/share) comes back as "", and
    // settings and the client would land in whatever directory the launcher was started from.
    public static readonly string SavePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                Id);

    private static string GetMetadata(string key, bool required = true)
    {
        var value = typeof(Constants).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == key)?.Value;

        if (string.IsNullOrWhiteSpace(value) && required)
            throw new InvalidOperationException($"Build property '{key}' is not set.");

        return value ?? string.Empty;
    }
}
