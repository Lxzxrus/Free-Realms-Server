using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Launcher.Helpers;

/// <summary>
/// Evergrove's additions to the game client, applied after the client passes its check (see
/// <see cref="ClientVerifier"/>). Each is made from the player's own verified files plus our own code: no game file is
/// shipped. Source and build: tools/client-mods/housing-search.
/// </summary>
public static class ClientMods
{
    /// <summary>
    /// The housing Decorate panel gets search and colour buttons: our compiled frame script, inserted after the panel's
    /// own, so our AddItem, ResetItems, DisplayItems and onClick replace the panel's.
    /// </summary>
    private const string PanelName = "housingEditPanel.gfx";
    private const string PanelOriginalSha256 = "4bbe2a4ed39f6bd1446df1b42c9ec8bd29d471a27c2b87bc4c3c7a2339c1999b";
    private static readonly string[] PanelOutputs = ["UI/housingEditPanel.gfx", "UI/housingEditPanel.swf"];

    /// <summary>
    /// The UI scripts get our edits from scripts.patch (tools/client-mods/housing-search/lua_patch.py): the Decorate
    /// panel keeps keyboard focus, so its search box can be typed in, and a placed part's menu gets a Paint button.
    /// </summary>
    public const string ScriptsPath = "UI/ScriptsBase.bin";
    public const string ScriptsModdedSha256 = "280f351c294f81a8f573ac4d59cd59ac05ca610c906db7c2278199a4ea3c0825";

    private const int DoActionTag = 12;

    /// <summary>
    /// Hashes a pinned file may have besides its official one because a mod changed it. The client check accepts them,
    /// so a modded file isn't "repaired" on every launch.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AcceptedVariants { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [ScriptsPath] = ScriptsModdedSha256
    };

    /// <summary>Applies every mod. Returns false, with the reason, when one couldn't be applied; the game still runs without it.</summary>
    public static bool TryApply(string clientDirectory, ClientPinSet pins, out string error)
    {
        try
        {
            ApplyScriptsPatch(clientDirectory, pins);
            ApplyPanel(clientDirectory);

            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void ApplyScriptsPatch(string clientDirectory, ClientPinSet pins)
    {
        var path = Path.Combine(clientDirectory, ScriptsPath);
        var data = File.ReadAllBytes(path);
        var sha = Sha256(data);

        if (sha == ScriptsModdedSha256)
            return;

        if (!pins.TryGet(ScriptsPath, out var pin) || sha != pin.Sha256)
            throw new InvalidDataException($"{ScriptsPath} isn't the official file, so it wasn't modified.");

        var patched = ApplyPatch(data, LoadResource("Launcher.Mods.scripts.patch"));

        if (Sha256(patched) != ScriptsModdedSha256)
            throw new InvalidDataException($"Patching {ScriptsPath} gave an unexpected result.");

        WriteAtomically(path, patched);
    }

    /// <summary>
    /// <paramref name="original"/> with the edits of a patch made by lua_patch.py: "EVGP", a count, then per edit its
    /// offset in the original, the number of bytes it replaces there, and the bytes it puts in their place.
    /// </summary>
    public static byte[] ApplyPatch(byte[] original, byte[] patch)
    {
        if (patch.Length < 8 || Encoding.ASCII.GetString(patch, 0, 4) != "EVGP")
            throw new InvalidDataException("Not a script patch.");

        var count = BinaryPrimitives.ReadInt32LittleEndian(patch.AsSpan(4));
        var edits = new List<(int Offset, int Length, ReadOnlyMemory<byte> Inserted)>(count);
        var position = 8;

        for (var i = 0; i < count; i++)
        {
            if (position + 12 > patch.Length)
                throw new InvalidDataException("The script patch is cut short.");

            var offset = BinaryPrimitives.ReadInt32LittleEndian(patch.AsSpan(position));
            var length = BinaryPrimitives.ReadInt32LittleEndian(patch.AsSpan(position + 4));
            var insertedLength = BinaryPrimitives.ReadInt32LittleEndian(patch.AsSpan(position + 8));
            position += 12;

            if (offset < 0 || length < 0 || insertedLength < 0 || offset + length > original.Length ||
                position + insertedLength > patch.Length)
            {
                throw new InvalidDataException("The script patch doesn't fit the file.");
            }

            edits.Add((offset, length, patch.AsMemory(position, insertedLength)));
            position += insertedLength;
        }

        if (position != patch.Length)
            throw new InvalidDataException("The script patch has bytes left over.");

        // In file order, each edit after the previous one's end; at one offset an insertion comes before the
        // replacement that starts there.
        var ordered = edits.OrderBy(edit => edit.Offset).ThenBy(edit => edit.Length).ToList();
        using var output = new MemoryStream(original.Length + ordered.Sum(edit => edit.Inserted.Length));
        var copied = 0;

        foreach (var (offset, length, inserted) in ordered)
        {
            if (offset < copied)
                throw new InvalidDataException("The script patch's edits overlap.");

            output.Write(original, copied, offset - copied);
            output.Write(inserted.Span);
            copied = offset + length;
        }

        output.Write(original, copied, original.Length - copied);
        return output.ToArray();
    }

    private static void ApplyPanel(string clientDirectory)
    {
        var original = PackReader.ReadFile(clientDirectory, PanelName)
            ?? throw new InvalidDataException($"{PanelName} wasn't found in the game's packs.");

        if (Sha256(original) != PanelOriginalSha256)
            throw new InvalidDataException($"{PanelName} isn't the version the Decorate mod was made for.");

        var panel = InsertAfterFrameScript(original, LoadResource("Launcher.Mods.housing-search.tag"));

        foreach (var output in PanelOutputs)
        {
            var path = Path.Combine(clientDirectory, output);

            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(panel))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAtomically(path, panel);
        }
    }

    /// <summary>
    /// The panel (a zlib-compressed Scaleform "CFX" movie) with <paramref name="tag"/>, a DoAction tag, inserted straight
    /// after its first frame's DoAction. Both run in frame 1, ours second.
    /// </summary>
    public static byte[] InsertAfterFrameScript(byte[] gfx, byte[] tag)
    {
        if (gfx.Length < 8 || Encoding.ASCII.GetString(gfx, 0, 3) != "CFX")
            throw new InvalidDataException("Not a compressed Scaleform movie.");

        byte[] body;
        using (var input = new ZLibStream(new MemoryStream(gfx, 8, gfx.Length - 8), CompressionMode.Decompress))
        using (var decompressed = new MemoryStream())
        {
            input.CopyTo(decompressed);
            body = decompressed.ToArray();
        }

        var end = FrameScriptEnd(body);
        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(gfx.AsSpan(4));

        using var output = new MemoryStream();
        output.Write(gfx, 0, 4);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, declaredLength + (uint)tag.Length);
        output.Write(length);

        using (var compressor = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            compressor.Write(body, 0, end);
            compressor.Write(tag);
            compressor.Write(body, end, body.Length - end);
        }

        return output.ToArray();
    }

    private static int FrameScriptEnd(byte[] body)
    {
        var rectBits = body[0] >> 3;
        var offset = (5 + 4 * rectBits + 7) / 8 + 4;

        while (offset + 2 <= body.Length)
        {
            var header = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(offset));
            var code = header >> 6;
            long length = header & 0x3F;
            var headerLength = 2;

            if (length == 0x3F)
            {
                length = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(offset + 2));
                headerLength = 6;
            }

            var next = offset + headerLength + length;
            if (next > body.Length)
                break;

            if (code == DoActionTag)
                return (int)next;

            if (code == 0)
                break;

            offset = (int)next;
        }

        throw new InvalidDataException("The panel has no frame script.");
    }

    private static byte[] LoadResource(string name)
    {
        using var stream = typeof(ClientMods).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"The launcher was built without {name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void WriteAtomically(string path, byte[] data)
    {
        var temporary = path + ".launcher-mod";
        File.WriteAllBytes(temporary, data);
        File.Move(temporary, path, overwrite: true);
    }

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}

/// <summary>Reads one file out of the game's Assets_*.pack archives (ForgeLight packs: big-endian chained chunks).</summary>
public static class PackReader
{
    public static byte[]? ReadFile(string clientDirectory, string name)
    {
        foreach (var packPath in Directory.EnumerateFiles(clientDirectory, "Assets_*.pack").Order(StringComparer.OrdinalIgnoreCase))
        {
            using var pack = File.OpenRead(packPath);
            var found = Find(pack, name);

            if (found is { } entry)
            {
                var data = new byte[entry.Length];
                pack.Position = entry.Offset;
                pack.ReadExactly(data);
                return data;
            }
        }

        return null;
    }

    private static (long Offset, int Length)? Find(Stream pack, string name)
    {
        Span<byte> word = stackalloc byte[4];
        long chunk = 0;

        while (chunk + 8 <= pack.Length)
        {
            pack.Position = chunk;
            var next = ReadUInt32(pack, word);
            var count = ReadUInt32(pack, word);

            for (uint i = 0; i < count; i++)
            {
                var nameLength = (int)ReadUInt32(pack, word);
                if (nameLength is < 0 or > 4096)
                    throw new InvalidDataException("Unexpected pack layout.");

                var nameBytes = new byte[nameLength];
                pack.ReadExactly(nameBytes);
                var offset = ReadUInt32(pack, word);
                var length = ReadUInt32(pack, word);
                ReadUInt32(pack, word); // CRC

                if (Encoding.Latin1.GetString(nameBytes) == name)
                    return (offset, (int)length);
            }

            if (next == 0 || next <= chunk)
                break;

            chunk = next;
        }

        return null;
    }

    private static uint ReadUInt32(Stream stream, Span<byte> buffer)
    {
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }
}
