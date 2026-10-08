using System;
using System.IO;

using Launcher.Extensions;

namespace Launcher.Helpers;

/// <summary>
/// The launcher's settings, servers and game folders used to live in the folder Velopack installs the launcher into
/// (<c>%LocalAppData%\&lt;Id&gt;</c>). The installer then took a launcher that had only been run, never installed, for
/// an existing install, and uninstalling would have deleted every game folder with it. They now have a folder of
/// their own, and the first start moves them over.
/// </summary>
public static class DataFolder
{
    /// <summary>
    /// Moves the settings file and the servers folder from <paramref name="oldDirectory"/> into
    /// <paramref name="newDirectory"/>, once: only when the new folder has no settings yet. Velopack's own files are
    /// left where they are. Returns what was done, for the log, or null when there was nothing to move.
    /// </summary>
    public static string? MoveFromOldFolder(string oldDirectory, string newDirectory)
    {
        var oldSettings = Path.Combine(oldDirectory, Constants.SettingsFile);
        var newSettings = Path.Combine(newDirectory, Constants.SettingsFile);

        if (!File.Exists(oldSettings) || File.Exists(newSettings))
            return null;

        try
        {
            Directory.CreateDirectory(newDirectory);

            // The servers first: if that fails, the settings stay behind with them and the next start tries again.
            var oldServers = Path.Combine(oldDirectory, Constants.ServersDirectory);
            var newServers = Path.Combine(newDirectory, Constants.ServersDirectory);

            if (Directory.Exists(oldServers) && !Directory.Exists(newServers))
                Directory.Move(oldServers, newServers);

            File.Move(oldSettings, newSettings);

            return $"Moved the launcher's settings and servers from {oldDirectory} to {newDirectory}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't move the launcher's settings and servers from {oldDirectory} to {newDirectory}: {ex.Message}";
        }
    }

    /// <summary>
    /// A server's folder as kept in the settings: relative to the data folder (<c>Servers\&lt;name&gt;</c>), and
    /// always inside its servers folder. Settings used to keep full paths, which still pointed into
    /// <paramref name="oldDirectory"/> after the move; those, and anything that would leave the servers folder (the
    /// launcher deletes and quarantines files in it), become a folder of that name in the servers folder.
    /// </summary>
    public static string ToServerPath(string savePath, string dataDirectory, string oldDirectory)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var serversDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(dataDirectory, Constants.ServersDirectory)));

        var candidate = savePath;

        if (Path.IsPathRooted(savePath))
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(savePath));

            foreach (var root in new[] { dataDirectory, oldDirectory })
            {
                var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;

                if (full.StartsWith(fullRoot, comparison))
                {
                    candidate = full[fullRoot.Length..];
                    break;
                }
            }
        }

        var resolved = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(dataDirectory, candidate)));
        var parent = Path.GetDirectoryName(resolved);

        if (parent is not null && string.Equals(parent, serversDirectory, comparison))
            return Path.Combine(Constants.ServersDirectory, Path.GetFileName(resolved));

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(savePath)).ToValidDirectoryName();

        return Path.Combine(Constants.ServersDirectory, string.IsNullOrWhiteSpace(name) || name is "." or ".." ? "Server" : name);
    }
}
