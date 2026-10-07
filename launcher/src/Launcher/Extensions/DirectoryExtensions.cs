using System.IO;

namespace Launcher.Extensions;

public static class DirectoryExtensions
{
    /// <summary>
    /// Turns a server's name (from its manifest, so not trusted) into one folder name: no separators, no "..".
    /// </summary>
    public static string ToValidDirectoryName(this string name)
    {
        var validName = string.Join('_', name.Split(Path.GetInvalidFileNameChars())).Trim();

        // Also replace what Windows forbids, so a name gives the same folder on every platform.
        validName = string.Join('_', validName.Split(['\\', '/', ':', '*', '?', '"', '<', '>', '|']));

        if (validName.Trim('.').Length == 0)
            return "Server";

        return validName;
    }
}
