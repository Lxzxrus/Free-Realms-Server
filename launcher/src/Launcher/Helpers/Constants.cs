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

    // Set at build time in launcher/src/Directory.Build.props.
    public static readonly string Title = GetMetadata("LauncherTitle");
    public static readonly string Id = GetMetadata("LauncherId");
    public static readonly string DefaultServerUrl = GetMetadata("DefaultServerUrl");

    /// <summary>
    /// Velopack update feed: our default server hosts launcher releases under /launcher/.
    /// </summary>
    public static readonly string UpdateUrl = UriHelper.JoinUriPaths(DefaultServerUrl, "launcher");

    public static readonly string LogsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");

    // Create: without it, a missing folder (a fresh Linux account has no ~/.local/share) comes back as "", and
    // settings and the client would land in whatever directory the launcher was started from.
    public static readonly string SavePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
                Id);

    private static string GetMetadata(string key)
    {
        var value = typeof(Constants).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == key)?.Value;

        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Build property '{key}' is not set.");

        return value;
    }
}
