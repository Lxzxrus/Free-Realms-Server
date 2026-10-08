using System;
using System.IO;

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
}
