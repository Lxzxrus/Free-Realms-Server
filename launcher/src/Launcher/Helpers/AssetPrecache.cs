using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Helpers;

/// <summary>One file the launcher pre-caches: its name on the asset server, unpacked size, CRC-32 and SHA-256.</summary>
public sealed record PrecacheFile(string StoredName, int Size, uint Crc32, string Sha256)
{
    /// <summary>On the asset server a ".z" file is packed: a1b2c3d4, the unpacked length, then a zlib stream.</summary>
    public bool IsPacked => StoredName.EndsWith(".z", StringComparison.OrdinalIgnoreCase);

    /// <summary>The name the game looks the file up by, and stores it under in its packs.</summary>
    public string Name => IsPacked ? StoredName[..^2] : StoredName;
}

public enum PrecacheOutcome
{
    /// <summary>The server names no asset address, so nothing was checked.</summary>
    Skipped,

    /// <summary>Every file was already in the game's packs.</summary>
    UpToDate,

    /// <summary>The missing files were downloaded and added.</summary>
    Added
}

/// <summary>
/// Puts files the game loads on the spot, and that the official client lacks, into the game's own download cache before
/// it starts. The game reads a Flash minigame's pictures and a zone's <c>.gzne</c> synchronously, from its packs only:
/// when they exist only on the asset server it can't fetch them in time, so Mining's ore was invisible and every fishing
/// hole crashed the game on arrival (docs/research/mining.md). The list is pinned in the launcher
/// (<c>Mods/precache.txt</c>, made by tools/client-mods/precache), so the files are checked against their SHA-256
/// before anything is written: the asset server is plain HTTP, because the game can't do HTTPS.
/// </summary>
public static class AssetPrecache
{
    public const string ResourceName = "Launcher.Mods.precache.txt";

    /// <summary>The game reserves this much at the start of every pack block for the block's index, and adds its own
    /// downloads' entries there; a block's data may only start after it.</summary>
    public const int IndexAreaSize = 8192;

    /// <summary>A file larger than this is not one of ours: a guard against a server streaming forever.</summary>
    public const int MaxFileSize = 16 * 1024 * 1024;

    private static readonly byte[] PackedMagic = [0xA1, 0xB2, 0xC3, 0xD4];

    private static readonly Lazy<IReadOnlyList<PrecacheFile>> _files = new(() =>
    {
        using var stream = typeof(AssetPrecache).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException($"The launcher was built without {ResourceName}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Parse(reader.ReadToEnd());
    });

    /// <summary>The pinned list built into the launcher.</summary>
    public static IReadOnlyList<PrecacheFile> Files => _files.Value;

    /// <summary>Reads the list: <c>name,size,crc32,sha256</c> per line; blank lines and <c>#</c> comments skipped.</summary>
    public static IReadOnlyList<PrecacheFile> Parse(string text)
    {
        var files = new List<PrecacheFile>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var parts = line.Split(',');
            if (parts.Length != 4
                || !IsSafeName(parts[0])
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size <= 0 || size > MaxFileSize
                || !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var crc)
                || parts[3].Length != 64 || !parts[3].All(Uri.IsHexDigit))
            {
                throw new InvalidDataException($"Bad pre-cache line: {line}");
            }

            files.Add(new PrecacheFile(parts[0], size, crc, parts[3].ToLowerInvariant()));
        }

        return files;
    }

    /// <summary>A plain file name: no folders, no characters a URL or a path would treat specially.</summary>
    public static bool IsSafeName(string name) =>
        name.Length is > 0 and <= 255 && name != "." && name != ".."
        && name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.');

    /// <summary>
    /// The asset server's folder for a file: the game's hash of the upper-cased name, modulo 1000 (FreeRealms.exe
    /// 0x705b00, a one-at-a-time hash with signed shifts; tools/asset-mirror's <c>folder_of</c>).
    /// </summary>
    public static int FolderOf(string storedName)
    {
        var h = 0;

        foreach (var c in storedName.ToUpperInvariant())
        {
            h = unchecked(c + h);
            h = unchecked(h * 0x401);
            h ^= h >> 6;
        }

        var x = unchecked(h * 9);
        x ^= x >> 11;
        return (int)(unchecked((uint)(x * 0x8001)) % 1000);
    }

    /// <summary>Where the asset server serves a file, as the game asks for it.</summary>
    public static Uri UrlOf(Uri assetUrl, PrecacheFile file)
    {
        var baseText = assetUrl.AbsoluteUri.TrimEnd('/');
        return new Uri($"{baseText}/{FolderOf(file.StoredName):D3}/{file.StoredName}?{file.Crc32}");
    }

    /// <summary>
    /// The list's files that the game's packs don't hold intact. A copy in the download cache that isn't exactly the
    /// pinned file (an older version, or data the game overwrote) is renamed in place, so the game can't find it before
    /// the fresh copy (see <see cref="PackIndex.Retire"/>). The official packs are never touched: the list holds no file
    /// they contain, and they are pinned.
    /// </summary>
    public static IReadOnlyList<PrecacheFile> FindMissing(string clientDirectory, IReadOnlyList<PrecacheFile> files)
    {
        var wanted = files.ToDictionary(file => file.Name, StringComparer.OrdinalIgnoreCase);
        var intact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pack in PackIndex.CachePacks(clientDirectory))
        {
            foreach (var entry in PackIndex.Read(pack).Where(entry => wanted.ContainsKey(entry.Name)))
            {
                var file = wanted[entry.Name];

                if (entry.Crc32 == file.Crc32 && entry.Length == file.Size && PackIndex.DataMatches(pack, entry))
                    intact.Add(entry.Name);
                else
                    PackIndex.Retire(pack, entry);
            }
        }

        return files.Where(file => !intact.Contains(file.Name)).ToList();
    }

    /// <summary>Downloads one file and returns its unpacked data, or throws if it isn't exactly the pinned file.</summary>
    public static async Task<byte[]> DownloadAsync(HttpClient http, Uri assetUrl, PrecacheFile file, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(UrlOf(assetUrl, file), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxFileSize)
                throw new InvalidDataException($"{file.Name} is larger than any pre-cached file.");
            buffer.Write(chunk, 0, read);
        }

        var data = file.IsPacked ? Unpack(buffer.ToArray(), file.Name) : buffer.ToArray();

        if (data.Length != file.Size || Crc32(data) != file.Crc32 || Convert.ToHexStringLower(SHA256.HashData(data)) != file.Sha256)
            throw new InvalidDataException($"{file.Name} from the asset server isn't the expected file.");

        return data;
    }

    private static byte[] Unpack(byte[] packed, string name)
    {
        if (packed.Length < 8 || !packed.AsSpan(0, 4).SequenceEqual(PackedMagic))
            throw new InvalidDataException($"{name} isn't a packed asset.");

        var length = BinaryPrimitives.ReadUInt32BigEndian(packed.AsSpan(4));
        if (length > MaxFileSize)
            throw new InvalidDataException($"{name} unpacks to more than any pre-cached file.");

        using var input = new MemoryStream(packed, 8, packed.Length - 8);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        var data = new byte[length];
        zlib.ReadExactly(data);

        if (zlib.ReadByte() != -1)
            throw new InvalidDataException($"{name} unpacks to more than its header says.");

        return data;
    }

    /// <summary>
    /// Adds the files to the game's download cache as a new pack after the existing ones, and returns its path. The game
    /// then finds them as current and keeps appending its own downloads after them.
    /// </summary>
    public static string WritePack(string clientDirectory, IReadOnlyList<(PrecacheFile File, byte[] Data)> files)
    {
        var path = PackIndex.NextCachePackPath(clientDirectory);
        var temporary = path + ".launcher-precache";

        File.WriteAllBytes(temporary, BuildPack(files));
        File.Move(temporary, path);

        return path;
    }

    /// <summary>
    /// A pack in the game's own cache layout: blocks of a header (next block, entry count) and entries (name length,
    /// name, data offset, length, CRC-32), each block's index in the first <see cref="IndexAreaSize"/> bytes with
    /// room to spare, its data after that, and the next block after the data.
    /// </summary>
    public static byte[] BuildPack(IReadOnlyList<(PrecacheFile File, byte[] Data)> files)
    {
        var blocks = new List<List<(PrecacheFile File, byte[] Data)>>();
        var current = new List<(PrecacheFile File, byte[] Data)>();
        var indexBytes = 8;

        foreach (var file in files)
        {
            var entryBytes = 4 + Encoding.Latin1.GetByteCount(file.File.Name) + 12;
            if (current.Count > 0 && indexBytes + entryBytes > IndexAreaSize)
            {
                blocks.Add(current);
                current = [];
                indexBytes = 8;
            }

            current.Add(file);
            indexBytes += entryBytes;
        }

        if (current.Count > 0)
            blocks.Add(current);

        using var output = new MemoryStream();
        Span<byte> word = stackalloc byte[4];

        for (var b = 0; b < blocks.Count; b++)
        {
            var start = output.Length;
            var dataStart = start + IndexAreaSize;
            var next = b + 1 < blocks.Count ? dataStart + blocks[b].Sum(file => (long)file.Data.Length) : 0;

            WriteUInt32(output, word, checked((uint)next));
            WriteUInt32(output, word, (uint)blocks[b].Count);

            var offset = dataStart;
            foreach (var (file, data) in blocks[b])
            {
                var name = Encoding.Latin1.GetBytes(file.Name);
                WriteUInt32(output, word, (uint)name.Length);
                output.Write(name);
                WriteUInt32(output, word, checked((uint)offset));
                WriteUInt32(output, word, (uint)data.Length);
                WriteUInt32(output, word, file.Crc32);
                offset += data.Length;
            }

            output.SetLength(dataStart);
            output.Position = dataStart;

            foreach (var (_, data) in blocks[b])
                output.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Checks the cache and adds what's missing. Throws when a file can't be fetched or isn't the pinned one.</summary>
    public static async Task<PrecacheOutcome> EnsureAsync(string clientDirectory, string? assetUrl, HttpClient http,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default,
        IReadOnlyList<PrecacheFile>? files = null)
    {
        if (!TryParseAssetUrl(assetUrl, out var baseUrl))
            return PrecacheOutcome.Skipped;

        var missing = FindMissing(clientDirectory, files ?? Files);
        if (missing.Count == 0)
            return PrecacheOutcome.UpToDate;

        var downloaded = new List<(PrecacheFile, byte[])>(missing.Count);
        foreach (var file in missing)
        {
            progress?.Report((downloaded.Count, missing.Count));
            downloaded.Add((file, await DownloadAsync(http, baseUrl, file, cancellationToken)));
        }

        progress?.Report((downloaded.Count, missing.Count));
        WritePack(clientDirectory, downloaded);

        return PrecacheOutcome.Added;
    }

    /// <summary>An absolute http(s) address with no user name or password in it.</summary>
    public static bool TryParseAssetUrl(string? text, out Uri url)
    {
        url = null!;

        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text, UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps || parsed.UserInfo.Length > 0
            || parsed.Query.Length > 0 || parsed.Fragment.Length > 0)
        {
            return false;
        }

        url = parsed;
        return true;
    }

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static void WriteUInt32(Stream stream, Span<byte> buffer, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}

/// <summary>Reads the index of the game's packs (ForgeLight: big-endian chained blocks).</summary>
public static class PackIndex
{
    public sealed record Entry(string Name, long NameOffset, uint Offset, uint Length, uint Crc32);

    /// <summary>The game's download cache packs, in name order. The official Assets_*.pack files are never read here.</summary>
    public static IEnumerable<string> CachePacks(string clientDirectory) =>
        Directory.EnumerateFiles(clientDirectory, "AssetsW_*.pack").Order(StringComparer.OrdinalIgnoreCase);

    /// <summary>The cache pack after the last one: AssetsW_000.pack when there is none.</summary>
    public static string NextCachePackPath(string clientDirectory)
    {
        var last = CachePacks(clientDirectory)
            .Select(path => Path.GetFileNameWithoutExtension(path)["AssetsW_".Length..])
            .Select(number => int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1)
            .DefaultIfEmpty(-1)
            .Max();

        return Path.Combine(clientDirectory, $"AssetsW_{last + 1:D3}.pack");
    }

    /// <summary>Every entry of a pack. A pack the game is part-way through writing ends where its index stops making sense.</summary>
    public static List<Entry> Read(string packPath)
    {
        var entries = new List<Entry>();
        using var pack = File.OpenRead(packPath);
        Span<byte> word = stackalloc byte[4];
        long block = 0;

        while (block + 8 <= pack.Length)
        {
            pack.Position = block;
            var next = ReadUInt32(pack, word);
            var count = ReadUInt32(pack, word);

            for (uint i = 0; i < count; i++)
            {
                if (pack.Position + 4 > pack.Length)
                    return entries;

                var nameLength = ReadUInt32(pack, word);
                if (nameLength is 0 or > 4096 || pack.Position + nameLength + 12 > pack.Length)
                    return entries;

                var nameOffset = pack.Position;
                var name = new byte[nameLength];
                pack.ReadExactly(name);
                var offset = ReadUInt32(pack, word);
                var length = ReadUInt32(pack, word);
                var crc = ReadUInt32(pack, word);

                entries.Add(new Entry(Encoding.Latin1.GetString(name), nameOffset, offset, length, crc));
            }

            if (next == 0 || next <= block)
                break;

            block = next;
        }

        return entries;
    }

    public static bool DataMatches(string packPath, Entry entry)
    {
        using var pack = File.OpenRead(packPath);
        if ((long)entry.Offset + entry.Length > pack.Length)
            return false;

        var data = new byte[entry.Length];
        pack.Position = entry.Offset;
        pack.ReadExactly(data);
        return AssetPrecache.Crc32(data) == entry.Crc32;
    }

    /// <summary>
    /// Hides a damaged copy from the game: its name's first character becomes '~', which no asset name starts with,
    /// so the game no longer finds it and uses the fresh copy instead. Same length, so nothing else in the pack moves.
    /// </summary>
    public static void Retire(string packPath, Entry entry)
    {
        using var pack = File.Open(packPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        pack.Position = entry.NameOffset;
        pack.WriteByte((byte)'~');
    }

    private static uint ReadUInt32(Stream stream, Span<byte> buffer)
    {
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }
}
