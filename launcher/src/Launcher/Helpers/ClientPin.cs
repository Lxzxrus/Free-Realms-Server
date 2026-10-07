using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Helpers;

public enum ClientPinResult
{
    Match,
    Mismatch,
    Missing,
    Unreadable,

    /// <summary>The launcher was built without a valid expected hash, so it can't vouch for any client.</summary>
    NotConfigured
}

/// <summary>
/// Pins the game's executable to one known SHA-256, set at build time (ClientExecutableSha256 in
/// Directory.Build.props). The launcher starts FreeRealms.exe on the player's PC, and a server's client manifest
/// can download files into the client folder, so without the pin a compromised server could ship its own program.
/// </summary>
public static class ClientPin
{
    public static bool IsValidHash(string? sha256)
        => sha256?.Trim() is { Length: 64 } hash && hash.All(Uri.IsHexDigit);

    public static async Task<ClientPinResult> CheckAsync(Stream stream, string expectedSha256, CancellationToken cancellationToken = default)
    {
        if (!IsValidHash(expectedSha256))
            return ClientPinResult.NotConfigured;

        var actual = await SHA256.HashDataAsync(stream, cancellationToken);

        return Convert.ToHexString(actual).Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase)
            ? ClientPinResult.Match
            : ClientPinResult.Mismatch;
    }

    public static async Task<ClientPinResult> CheckFileAsync(string path, string expectedSha256, CancellationToken cancellationToken = default)
    {
        if (!IsValidHash(expectedSha256))
            return ClientPinResult.NotConfigured;

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

            return await CheckAsync(stream, expectedSha256, cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ClientPinResult.Missing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ClientPinResult.Unreadable;
        }
    }

    /// <summary>
    /// True when <paramref name="filePath"/> is FreeRealms.exe and the copy on disk passes the pin. Such a copy is
    /// kept whatever the server's client manifest says about it.
    /// </summary>
    public static async Task<bool> IsPinnedClientInPlaceAsync(string clientDirectory, string filePath, string expectedSha256)
        => IsClientExecutable(clientDirectory, filePath)
           && await CheckFileAsync(filePath, expectedSha256) == ClientPinResult.Match;

    /// <summary>
    /// Saves a file the server's client manifest sent. The server may send any game file, but never a
    /// FreeRealms.exe that fails the pin: that one is checked before anything is written, and refused (false) without
    /// touching the copy on disk.
    /// </summary>
    public static async Task<bool> TrySaveClientFileAsync(string clientDirectory, string filePath, Stream content, string expectedSha256, CancellationToken cancellationToken = default)
    {
        if (!IsClientExecutable(clientDirectory, filePath))
        {
            await using var writeStream = File.Create(filePath);

            await content.CopyToAsync(writeStream, cancellationToken);

            return true;
        }

        using var executable = new MemoryStream();

        await content.CopyToAsync(executable, cancellationToken);

        executable.Position = 0;

        if (await CheckAsync(executable, expectedSha256, cancellationToken) != ClientPinResult.Match)
            return false;

        await File.WriteAllBytesAsync(filePath, executable.ToArray(), cancellationToken);

        return true;
    }

    /// <summary>
    /// True when <paramref name="fullPath"/> is the executable the launcher starts. Case is ignored on every
    /// platform: Windows and Wine both open "freerealms.exe" for "FreeRealms.exe".
    /// </summary>
    public static bool IsClientExecutable(string clientDirectory, string fullPath)
    {
        var executablePath = Path.GetFullPath(Path.Combine(clientDirectory, Constants.ClientExecutableName));

        return Path.GetFullPath(fullPath).Equals(executablePath, StringComparison.OrdinalIgnoreCase);
    }
}
