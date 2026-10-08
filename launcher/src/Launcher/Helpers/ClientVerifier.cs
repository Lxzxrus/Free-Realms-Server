using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Helpers;

/// <summary>What a client check found.</summary>
/// <param name="Bad">Pinned files that are missing or don't match their pin.</param>
/// <param name="UnknownCode">Code files (see <see cref="ClientCodeFiles"/>) that aren't pinned, relative to the client folder.</param>
public sealed record ClientCheckResult(IReadOnlyList<ClientFilePin> Bad, IReadOnlyList<string> UnknownCode)
{
    public bool IsClean => Bad.Count == 0 && UnknownCode.Count == 0;
}

/// <summary>
/// Checks a client folder against the official pins, repairs it from the official client download, and moves code
/// files that aren't part of the official client out of the folder. Code files are hashed on every check; the large
/// data files only when their size or write time changed since they last passed (a cache kept outside the client
/// folder).
/// </summary>
public sealed class ClientVerifier
{
    /// <summary>Fetches one official client file by its pinned path.</summary>
    public delegate Task<Stream> FetchOfficialFile(string relativePath, CancellationToken cancellationToken);

    private const string DownloadSuffix = ".launcher-download";

    private readonly ClientPinSet _pins;
    private readonly string _clientDirectory;
    private readonly string _cachePath;
    private readonly FetchOfficialFile _fetch;
    private readonly IReadOnlyDictionary<string, string> _acceptedVariants;

    /// <param name="acceptedVariants">
    /// By pinned path, one more hash the file may have because one of our client mods changed it (see
    /// <see cref="ClientMods.AcceptedVariants"/>). Never a code file.
    /// </param>
    public ClientVerifier(ClientPinSet pins, string clientDirectory, string cachePath, FetchOfficialFile fetch,
        IReadOnlyDictionary<string, string>? acceptedVariants = null)
    {
        _pins = pins;
        _clientDirectory = Path.GetFullPath(clientDirectory);
        _cachePath = cachePath;
        _fetch = fetch;
        _acceptedVariants = acceptedVariants ?? new Dictionary<string, string>();

        if (_acceptedVariants.Keys.Any(ClientCodeFiles.IsCode))
            throw new ArgumentException("A code file can't have an accepted variant.", nameof(acceptedVariants));
    }

    private bool IsAccepted(ClientFilePin pin, string sha256)
        => sha256 == pin.Sha256 ||
           _acceptedVariants.TryGetValue(ClientPinSet.Normalize(pin.Path), out var variant) && sha256 == variant;

    /// <summary>Checks every pinned file and looks for code files that aren't pinned.</summary>
    public Task<ClientCheckResult> CheckAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
        => CheckAsync(_pins.All, progress, cancellationToken);

    /// <summary>Checks only the pinned code files, and looks for unpinned ones: fast enough for every launch.</summary>
    public Task<ClientCheckResult> CheckCodeAsync(CancellationToken cancellationToken = default)
        => CheckAsync(_pins.All.Where(pin => ClientCodeFiles.IsCode(pin.Path)), null, cancellationToken);

    private async Task<ClientCheckResult> CheckAsync(IEnumerable<ClientFilePin> pins, IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken)
    {
        var cache = LoadCache();
        var toCheck = pins.ToList();
        var bad = new List<ClientFilePin>();
        var done = 0;

        foreach (var pin in toCheck)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await IsIntactAsync(pin, cache, cancellationToken))
                bad.Add(pin);

            progress?.Report((++done, toCheck.Count));
        }

        SaveCache(cache);

        return new ClientCheckResult(bad, FindUnknownCode());
    }

    /// <summary>
    /// Downloads each file from the official client and writes it only if it matches its pin. Returns the files that
    /// couldn't be repaired.
    /// </summary>
    public async Task<IReadOnlyList<ClientFilePin>> RepairAsync(IEnumerable<ClientFilePin> files, IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        var toRepair = files.ToList();
        var failed = new List<ClientFilePin>();
        var done = 0;

        foreach (var pin in toRepair)
        {
            if (!await TryRepairAsync(pin, cancellationToken))
                failed.Add(pin);

            progress?.Report((++done, toRepair.Count));
        }

        return failed;
    }

    /// <summary>
    /// Moves code files that aren't part of the official client into <paramref name="quarantineDirectory"/>, keeping
    /// their relative paths, so the game can't load them and the player can still get them back. Returns the files
    /// that couldn't be moved.
    /// </summary>
    public IReadOnlyList<string> Quarantine(IEnumerable<string> unknownCode, string quarantineDirectory)
    {
        var failed = new List<string>();

        foreach (var relativePath in unknownCode)
        {
            try
            {
                var source = Path.Combine(_clientDirectory, relativePath);
                var target = Path.Combine(quarantineDirectory, relativePath);

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(source, target, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(relativePath);
            }
        }

        return failed;
    }

    private async Task<bool> IsIntactAsync(ClientFilePin pin, Dictionary<string, CacheEntry> cache, CancellationToken cancellationToken)
    {
        var path = FullPath(pin.Path);
        var info = new FileInfo(path);

        // A variant has the official size: our mods change bytes, never lengths.
        if (!info.Exists || info.Length != pin.Size)
            return false;

        var key = ClientPinSet.Normalize(pin.Path);
        var stamp = info.LastWriteTimeUtc.Ticks;

        // Code is always hashed: a cache entry is only as trustworthy as the file times, which anyone can set.
        if (!ClientCodeFiles.IsCode(pin.Path) &&
            cache.TryGetValue(key, out var entry) &&
            entry.Size == info.Length && entry.WriteTicks == stamp && IsAccepted(pin, entry.Sha256))
        {
            return true;
        }

        string actual;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (!IsAccepted(pin, actual))
        {
            cache.Remove(key);
            return false;
        }

        cache[key] = new CacheEntry(info.Length, stamp, actual);
        return true;
    }

    private async Task<bool> TryRepairAsync(ClientFilePin pin, CancellationToken cancellationToken)
    {
        var path = FullPath(pin.Path);
        var download = path + DownloadSuffix;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Written beside the target and moved into place only once it matches, so a bad download never replaces
            // anything.
            await using (var source = await _fetch(pin.Path, cancellationToken))
            await using (var target = new FileStream(download, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[1 << 16];
                long length = 0;
                int read;

                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    length += read;
                    if (length > pin.Size)
                        break;

                    sha.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                if (length != pin.Size || Convert.ToHexStringLower(sha.GetHashAndReset()) != pin.Sha256)
                    return false;
            }

            File.Move(download, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            return false;
        }
        finally
        {
            try
            {
                File.Delete(download);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private List<string> FindUnknownCode()
    {
        if (!Directory.Exists(_clientDirectory))
            return [];

        return Directory.EnumerateFiles(_clientDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_clientDirectory, path))
            .Where(relative => ClientCodeFiles.IsCode(relative) && !_pins.TryGet(relative, out _))
            .OrderBy(relative => relative, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private string FullPath(string relativePath)
    {
        var parts = ClientPinSet.Normalize(relativePath).Split('/');

        if (!PathHelper.TryGetPathInside(_clientDirectory, Path.Combine(parts[..^1]), parts[^1], out var path))
            throw new InvalidDataException($"Pinned path {relativePath} is outside the client folder.");

        return path;
    }

    private sealed record CacheEntry(long Size, long WriteTicks, string Sha256);

    private Dictionary<string, CacheEntry> LoadCache()
    {
        var cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var line in File.ReadLines(_cachePath))
            {
                var parts = line.Split('\t');
                if (parts.Length == 4 &&
                    long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var size) &&
                    long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
                {
                    cache[parts[0]] = new CacheEntry(size, ticks, parts[3]);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return cache;
    }

    private void SaveCache(Dictionary<string, CacheEntry> cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_cachePath))!);
            File.WriteAllLines(_cachePath, cache.Select(pair =>
                string.Join('\t', pair.Key, pair.Value.Size.ToString(CultureInfo.InvariantCulture),
                    pair.Value.WriteTicks.ToString(CultureInfo.InvariantCulture), pair.Value.Sha256)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
