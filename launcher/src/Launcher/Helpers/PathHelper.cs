using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Launcher.Helpers;

public static class PathHelper
{
    private static readonly StringComparison PathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>
    /// Joins a folder and file name from a client manifest onto the client folder, and refuses any result outside
    /// it. Manifests come from the server, and a name like "..\..\x" or "C:\x" would otherwise write anywhere.
    /// </summary>
    public static bool TryGetPathInside(string root, string relativeDirectory, string fileName, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativeDirectory, fileName));

        if (!candidate.StartsWith(fullRoot, PathComparison) || candidate.Length == fullRoot.Length)
            return false;

        fullPath = candidate;
        return true;
    }
}
