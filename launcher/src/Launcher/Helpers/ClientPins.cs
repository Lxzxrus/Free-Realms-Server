using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Launcher.Helpers;

/// <summary>One file of the official game client: where it goes, its size and its SHA-256.</summary>
public sealed record ClientFilePin(string Path, long Size, string Sha256);

/// <summary>
/// Every file of the official Open Source Free Realms client, with its SHA-256 (ClientPins.txt, made by
/// launcher/tools/make-client-pins.cs and embedded in the launcher). The launcher checks the player's client against
/// it before each launch (see <see cref="ClientVerifier"/>), because many players reuse a client that came from
/// someone else's distribution.
/// </summary>
public sealed class ClientPinSet
{
    private static readonly Lazy<ClientPinSet> _official = new(LoadOfficial);

    private readonly Dictionary<string, ClientFilePin> _byPath;

    public ClientPinSet(IEnumerable<ClientFilePin> pins)
    {
        _byPath = pins.ToDictionary(pin => Normalize(pin.Path), StringComparer.OrdinalIgnoreCase);
    }

    public static ClientPinSet Official => _official.Value;

    public IReadOnlyCollection<ClientFilePin> All => _byPath.Values;

    public bool TryGet(string relativePath, out ClientFilePin pin)
        => _byPath.TryGetValue(Normalize(relativePath), out pin!);

    /// <summary>Parses "sha256 size path" lines; '#' starts a comment line.</summary>
    public static ClientPinSet Parse(TextReader reader)
    {
        var pins = new List<ClientFilePin>();

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var parts = line.Split(' ', 3);
            if (parts.Length != 3 || !ClientPin.IsValidHash(parts[0]) || !long.TryParse(parts[1], out var size))
                throw new InvalidDataException($"Bad client pin line: {line}");

            pins.Add(new ClientFilePin(parts[2], size, parts[0].ToLowerInvariant()));
        }

        return new ClientPinSet(pins);
    }

    /// <summary>Paths use '/' in the pin list and are compared without regard to case, as Windows and Wine open them.</summary>
    public static string Normalize(string relativePath) => relativePath.Replace('\\', '/').TrimStart('/');

    private static ClientPinSet LoadOfficial()
    {
        using var stream = typeof(ClientPinSet).Assembly.GetManifestResourceStream("Launcher.ClientPins.txt")
            ?? throw new InvalidOperationException("The launcher was built without ClientPins.txt.");
        using var reader = new StreamReader(stream);

        return Parse(reader);
    }
}

/// <summary>Files Windows (or Wine) can run or load as code. Only the pinned official copies of these are allowed.</summary>
public static class ClientCodeFiles
{
    private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Programs and libraries, including the Miles sound plugins (.asi, .flt, .m3d) mss32.dll loads.
        ".exe", ".dll", ".asi", ".flt", ".m3d", ".ocx", ".sys", ".cpl", ".scr", ".com", ".drv",
        // Things Windows runs when opened.
        ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".wsf", ".wsh", ".msi", ".msp", ".lnk", ".pif", ".hta", ".jar"
    };

    public static bool IsCode(string path) => CodeExtensions.Contains(Path.GetExtension(path));
}
