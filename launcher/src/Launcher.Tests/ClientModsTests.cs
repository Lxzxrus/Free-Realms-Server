using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Launcher.Helpers;

namespace Launcher.Tests;

[TestClass]
public class ClientModsTests
{
    // A minimal SWF body: a RECT with 1-bit fields (2 bytes), frame rate and count, then the tags.
    private static readonly byte[] BodyStart = [0x08, 0x00, 0x00, 0x18, 0x01, 0x00];

    private static byte[] Tag(int code, params byte[] data)
    {
        var header = (ushort)(code << 6 | data.Length);
        return [(byte)header, (byte)(header >> 8), .. data];
    }

    private static byte[] Cfx(byte[] body)
    {
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("CFX"));
        output.WriteByte(9);
        output.Write(BitConverter.GetBytes((uint)(body.Length + 8)));
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(body);
        return output.ToArray();
    }

    private static byte[] Body(byte[] cfx)
    {
        using var z = new ZLibStream(new MemoryStream(cfx, 8, cfx.Length - 8), CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        z.CopyTo(buffer);
        return buffer.ToArray();
    }

    [TestMethod]
    public void OurScript_IsInsertedRightAfterThePanelsFrameScript()
    {
        var theirs = Tag(12, 1, 2, 3);
        var init = Tag(59, 4, 5);
        var showFrame = Tag(1);
        var end = Tag(0);
        var original = Cfx([.. BodyStart, .. theirs, .. init, .. showFrame, .. end]);
        var ours = Tag(12, 9, 9, 9, 9);

        var modded = ClientMods.InsertAfterFrameScript(original, ours);

        byte[] expected = [.. BodyStart, .. theirs, .. ours, .. init, .. showFrame, .. end];
        CollectionAssert.AreEqual(expected, Body(modded));
        Assert.AreEqual(BitConverter.ToUInt32(original, 4) + (uint)ours.Length, BitConverter.ToUInt32(modded, 4));
        CollectionAssert.AreEqual(original.Take(4).ToArray(), modded.Take(4).ToArray());
    }

    [TestMethod]
    public void NotACompressedMovie_IsRefused()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => ClientMods.InsertAfterFrameScript(Encoding.ASCII.GetBytes("FWS-not-cfx"), Tag(12)));
    }

    [TestMethod]
    public void EmbeddedScriptBlock_IsOneDoActionTag()
    {
        using var stream = typeof(ClientMods).Assembly.GetManifestResourceStream("Launcher.Mods.housing-search.tag");
        Assert.IsNotNull(stream);

        var header = new byte[2];
        stream.ReadExactly(header);
        Assert.AreEqual(12, BitConverter.ToUInt16(header) >> 6);
    }

    [TestMethod]
    public async Task ModdedVariant_PassesTheCheck_OtherChangesDont()
    {
        var root = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        var client = Path.Combine(root, "Client");
        Directory.CreateDirectory(Path.Combine(client, "UI"));

        try
        {
            var official = Encoding.ASCII.GetBytes("official scripts");
            var modded = Encoding.ASCII.GetBytes("Official scripts");
            var other = Encoding.ASCII.GetBytes("official scriptZ");
            static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

            var pins = new ClientPinSet([new ClientFilePin("UI/ScriptsBase.bin", official.Length, Sha(official))]);
            var variants = new Dictionary<string, string> { ["UI/ScriptsBase.bin"] = Sha(modded) };
            var verifier = new ClientVerifier(pins, client, Path.Combine(root, "cache"), (_, _) => throw new InvalidOperationException(), variants);
            var path = Path.Combine(client, "UI", "ScriptsBase.bin");

            File.WriteAllBytes(path, modded);
            Assert.IsTrue((await verifier.CheckAsync()).IsClean);

            File.WriteAllBytes(path, other);
            Assert.AreEqual(1, (await verifier.CheckAsync()).Bad.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void CodeFiles_CantHaveVariants()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ClientVerifier(
            new ClientPinSet([]), Path.GetTempPath(), "cache", (_, _) => throw new InvalidOperationException(),
            new Dictionary<string, string> { ["FreeRealms.exe"] = new string('0', 64) }));
    }

    private static byte[] Patch(params (int Offset, int Length, string Inserted)[] edits)
    {
        using var stream = new MemoryStream();
        stream.Write(Encoding.ASCII.GetBytes("EVGP"));
        stream.Write(BitConverter.GetBytes(edits.Length));
        foreach (var (offset, length, inserted) in edits)
        {
            stream.Write(BitConverter.GetBytes(offset));
            stream.Write(BitConverter.GetBytes(length));
            stream.Write(BitConverter.GetBytes(inserted.Length));
            stream.Write(Encoding.ASCII.GetBytes(inserted));
        }

        return stream.ToArray();
    }

    [TestMethod]
    public void ScriptPatch_InsertsAndReplacesAtOriginalOffsets()
    {
        var original = Encoding.ASCII.GetBytes("0123456789");

        // As lua_patch.py writes them: an insertion where code ends and a count replaced where the constants begin,
        // at the same offset, listed in either order.
        var patched = ClientMods.ApplyPatch(original, Patch((4, 2, "AB"), (4, 0, "xyz"), (9, 1, "!"), (0, 0, "<")));

        Assert.AreEqual("<0123xyzAB678!", Encoding.ASCII.GetString(patched));
    }

    [TestMethod]
    public void ScriptPatch_ThatDoesntFit_IsRefused()
    {
        var original = Encoding.ASCII.GetBytes("0123456789");

        Assert.ThrowsExactly<InvalidDataException>(() => ClientMods.ApplyPatch(original, Patch((8, 4, "x"))));
        Assert.ThrowsExactly<InvalidDataException>(() => ClientMods.ApplyPatch(original, Patch((2, 4, "x"), (4, 1, "y"))));
        Assert.ThrowsExactly<InvalidDataException>(() => ClientMods.ApplyPatch(original, Encoding.ASCII.GetBytes("EVGX\0\0\0\0")));
        Assert.ThrowsExactly<InvalidDataException>(() => ClientMods.ApplyPatch(original, Patch((1, 0, "x"))[..^1]));
    }

    [TestMethod]
    public void EmbeddedScriptPatch_IsReadable()
    {
        using var stream = typeof(ClientMods).Assembly.GetManifestResourceStream("Launcher.Mods.scripts.patch");
        Assert.IsNotNull(stream);

        var header = new byte[4];
        stream.ReadExactly(header);
        Assert.AreEqual("EVGP", Encoding.ASCII.GetString(header));
    }

    [TestMethod]
    public void ModdedScripts_IsAnAcceptedVariantOfTheOfficialPin()
    {
        Assert.IsTrue(ClientPinSet.Official.TryGet(ClientMods.ScriptsPath, out _));
        Assert.AreEqual(ClientMods.ScriptsModdedSha256, ClientMods.AcceptedVariants[ClientMods.ScriptsPath]);
    }
}
