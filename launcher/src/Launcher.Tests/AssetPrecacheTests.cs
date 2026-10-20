using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Launcher.Helpers;

namespace Launcher.Tests;

[TestClass]
public sealed class AssetPrecacheTests
{
    private string _client = null!;

    [TestInitialize]
    public void CreateClientFolder()
    {
        _client = Path.Combine(Path.GetTempPath(), "precache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_client);
    }

    [TestCleanup]
    public void DeleteClientFolder()
    {
        try { Directory.Delete(_client, recursive: true); } catch (IOException) { }
    }

    private static (PrecacheFile File, byte[] Data) MakeFile(string storedName, string content)
    {
        var data = Encoding.ASCII.GetBytes(content);
        return (new PrecacheFile(storedName, data.Length, AssetPrecache.Crc32(data), Convert.ToHexStringLower(SHA256.HashData(data))), data);
    }

    /// <summary>A file as the asset server stores it: a1b2c3d4, the unpacked length, then a zlib stream.</summary>
    private static byte[] Packed(byte[] data)
    {
        using var output = new MemoryStream();
        output.Write([0xA1, 0xB2, 0xC3, 0xD4]);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data);
        return output.ToArray();
    }

    /// <summary>An asset server that serves the given files at the game's paths and counts requests.</summary>
    private static (HttpClient Http, StubHandler Handler) Server(IEnumerable<(PrecacheFile File, byte[] Data)> files, Func<byte[], byte[]>? tamper = null)
    {
        var byPath = files.ToDictionary(
            file => $"/assets/{AssetPrecache.FolderOf(file.File.StoredName):D3}/{file.File.StoredName}",
            file => file.File.IsPacked ? Packed(file.Data) : file.Data);

        var handler = new StubHandler(request =>
        {
            if (!byPath.TryGetValue(request.RequestUri!.AbsolutePath, out var body))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(tamper?.Invoke(body) ?? body) };
        });

        return (new HttpClient(handler), handler);
    }

    [TestMethod]
    [DataRow("hsg_block_01_ice_tcg.cdt.z", 97)]
    [DataRow("hsg_int_hum_bed_01_lrg_bbe.dme.z", 764)]
    [DataRow("sg_fishing_medpond.gzne.z", 215)]
    [DataRow("img10036336393259552362.dds.z", 686)]
    [DataRow("dragon_m_lava_glow_tint.dds.z", 999)]
    [DataRow("dragon_m_lava.dds.z", 66)]
    [DataRow("FabledRealms.odd", 220)]
    public void FolderOf_MatchesTheAssetServer(string name, int folder)
    {
        // Folders seen on play.evergrove.fyi and in the game's own requests (Client\Logs\FreeRealms.log).
        Assert.AreEqual(folder, AssetPrecache.FolderOf(name));
    }

    [TestMethod]
    public void ShippedList_ParsesAndHoldsTheMiningAndFishingFiles()
    {
        var files = AssetPrecache.Files;

        Assert.IsTrue(files.Count > 50);
        Assert.IsTrue(files.All(file => AssetPrecache.IsSafeName(file.StoredName)));
        Assert.AreEqual(files.Count, files.Select(file => file.Name.ToLowerInvariant()).Distinct().Count(), "A file is listed twice.");

        var names = files.Select(file => file.StoredName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.IsTrue(names.Contains("sg_fishing_medpond.gzne.z"));
        Assert.IsTrue(names.Contains("game_mining.gfx"));
        Assert.IsTrue(names.Contains("mining_tutorial.gfx"));
    }

    [TestMethod]
    [DataRow("../evil.dds.z,10,1,0000000000000000000000000000000000000000000000000000000000000000")]
    [DataRow("a b.dds,10,1,0000000000000000000000000000000000000000000000000000000000000000")]
    [DataRow("ok.dds,10,1,notahash")]
    [DataRow("ok.dds,0,1,0000000000000000000000000000000000000000000000000000000000000000")]
    [DataRow("ok.dds,10,1")]
    public void Parse_RefusesABadLine(string line)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => AssetPrecache.Parse(line));
    }

    [TestMethod]
    public void BuildPack_LeavesTheGamesIndexArea_AndReadsBack()
    {
        var files = new[] { MakeFile("a.dds.z", "first"), MakeFile("b.gfx", "second file") };
        var path = Path.Combine(_client, "AssetsW_000.pack");
        File.WriteAllBytes(path, AssetPrecache.BuildPack(files));

        var entries = PackIndex.Read(path);

        CollectionAssert.AreEqual(new[] { "a.dds", "b.gfx" }, entries.Select(entry => entry.Name).ToArray());
        Assert.IsTrue(entries.All(entry => entry.Offset >= AssetPrecache.IndexAreaSize),
            "The game adds its own entries to the first 8192 bytes; data there would be overwritten.");
        Assert.IsTrue(entries.All(entry => PackIndex.DataMatches(path, entry)));
    }

    [TestMethod]
    public void BuildPack_StartsANewBlockWhenTheIndexAreaIsFull()
    {
        var files = Enumerable.Range(0, 400).Select(i => MakeFile($"img{i:D20}.dds.z", $"picture {i}")).ToArray();
        var path = Path.Combine(_client, "AssetsW_000.pack");
        var pack = AssetPrecache.BuildPack(files);
        File.WriteAllBytes(path, pack);

        var entries = PackIndex.Read(path);

        Assert.AreEqual(400, entries.Count);
        Assert.IsTrue(entries.All(entry => PackIndex.DataMatches(path, entry)));
        Assert.AreNotEqual(0u, BinaryPrimitives.ReadUInt32BigEndian(pack), "400 entries don't fit one block's index area.");
    }

    [TestMethod]
    public async Task Ensure_DownloadsWhatsMissing_ThenFindsItCached()
    {
        var files = new[] { MakeFile("img1.dds.z", "a picture"), MakeFile("zone.gzne.z", "a zone"), MakeFile("game.gfx", "flash") };
        var (http, handler) = Server(files);
        var list = files.Select(file => file.File).ToList();

        Assert.AreEqual(PrecacheOutcome.Added, await AssetPrecache.EnsureAsync(_client, "http://assets.example/assets", http, files: list));
        Assert.IsTrue(File.Exists(Path.Combine(_client, "AssetsW_000.pack")));
        Assert.AreEqual(3, handler.Calls);

        Assert.AreEqual(PrecacheOutcome.UpToDate, await AssetPrecache.EnsureAsync(_client, "http://assets.example/assets", http, files: list));
        Assert.AreEqual(3, handler.Calls, "Nothing is downloaded again.");
        Assert.IsFalse(File.Exists(Path.Combine(_client, "AssetsW_001.pack")));
    }

    [TestMethod]
    public async Task Ensure_GoesAfterTheGamesOwnCachePacks()
    {
        File.WriteAllBytes(Path.Combine(_client, "AssetsW_000.pack"), AssetPrecache.BuildPack([MakeFile("other.dds", "the game's")]));
        File.WriteAllBytes(Path.Combine(_client, "AssetsW_001.pack"), AssetPrecache.BuildPack([MakeFile("other2.dds", "the game's")]));
        var files = new[] { MakeFile("img1.dds.z", "a picture") };
        var (http, _) = Server(files);

        await AssetPrecache.EnsureAsync(_client, "http://assets.example/assets", http, files: files.Select(f => f.File).ToList());

        Assert.IsTrue(File.Exists(Path.Combine(_client, "AssetsW_002.pack")));
    }

    [TestMethod]
    public async Task Ensure_RefusesAFileThatIsntThePinnedOne_AndWritesNothing()
    {
        var files = new[] { MakeFile("img1.dds.z", "a picture") };
        var (http, _) = Server(files, tamper: _ => Packed(Encoding.ASCII.GetBytes("a pictureX")));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            AssetPrecache.EnsureAsync(_client, "http://assets.example/assets", http, files: files.Select(f => f.File).ToList()));

        Assert.AreEqual(0, Directory.GetFiles(_client).Length);
    }

    [TestMethod]
    public async Task Ensure_RefusesAFileWithTheRightLengthAndCrcButTheWrongSha()
    {
        // CRC-32 is easy to forge; the SHA-256 is what keeps a tampered download out.
        var (file, data) = MakeFile("img1.dds", "a picture");
        var pinned = file with { Sha256 = new string('0', 64) };
        var (http, _) = Server([(pinned, data)]);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            AssetPrecache.EnsureAsync(_client, "http://assets.example/assets", http, files: [pinned]));
    }

    [TestMethod]
    public async Task Ensure_ReplacesACopyTheGameOverwrote_AndHidesTheDamagedOne()
    {
        // A cached copy whose index says the right CRC but whose data was overwritten (what happened to the first test pack).
        var good = MakeFile("zone.gzne.z", "the zone file");
        var damaged = AssetPrecache.BuildPack([good]);
        damaged[AssetPrecache.IndexAreaSize] ^= 0xFF;
        var cachePath = Path.Combine(_client, "AssetsW_000.pack");
        File.WriteAllBytes(cachePath, damaged);
        var (http, _) = Server([good]);

        Assert.AreEqual(PrecacheOutcome.Added, await AssetPrecache.EnsureAsync(_client, "http://assets.example/assets", http, files: [good.File]));

        Assert.AreEqual("~one.gzne", PackIndex.Read(cachePath).Single().Name);
        var fresh = PackIndex.Read(Path.Combine(_client, "AssetsW_001.pack")).Single();
        Assert.AreEqual("zone.gzne", fresh.Name);
    }

    [TestMethod]
    public async Task Ensure_NeverTouchesTheOfficialPacks()
    {
        var file = MakeFile("zone.gzne.z", "the zone file");
        var official = AssetPrecache.BuildPack([MakeFile("zone.gzne.z", "an older zone file")]);
        var officialPath = Path.Combine(_client, "Assets_000.pack");
        File.WriteAllBytes(officialPath, official);
        var (http, _) = Server([file]);

        await AssetPrecache.EnsureAsync(_client, "http://assets.example/assets", http, files: [file.File]);

        CollectionAssert.AreEqual(official, File.ReadAllBytes(officialPath));
    }

    [TestMethod]
    public async Task Ensure_DoesNothingWithoutAnAssetAddress()
    {
        var (http, handler) = Server([]);

        Assert.AreEqual(PrecacheOutcome.Skipped, await AssetPrecache.EnsureAsync(_client, null, http));
        Assert.AreEqual(PrecacheOutcome.Skipped, await AssetPrecache.EnsureAsync(_client, "", http));
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow("ftp://assets.example/assets")]
    [DataRow("http://user:pass@assets.example/assets")]
    [DataRow("http://assets.example/assets?x=1")]
    [DataRow("assets.example/assets")]
    public void AssetUrl_IsRefusedWhenItIsntAPlainHttpAddress(string url)
    {
        Assert.IsFalse(AssetPrecache.TryParseAssetUrl(url, out _));
    }

    [TestMethod]
    [DataRow("https://play.evergrove.fyi", true)]
    [DataRow("https://play.evergrove.fyi/", true)]
    [DataRow("https://PLAY.evergrove.fyi/", true)]
    [DataRow("http://play.evergrove.fyi", false)]
    [DataRow("https://play.evergrove.fyi:8443", false)]
    [DataRow("https://play.evergrove.fyi.example", false)]
    [DataRow("https://other.example", false)]
    [DataRow(null, false)]
    public void AssetUrlFor_OnlyTheDefaultServer(string? serverUrl, bool preCaches)
    {
        Assert.AreEqual(preCaches ? "http://play.evergrove.fyi:8080/assets" : null,
            AssetPrecache.AssetUrlFor(serverUrl, "https://play.evergrove.fyi", "http://play.evergrove.fyi:8080/assets"));
    }

    [TestMethod]
    public void AssetUrlFor_NothingWhenTheBuildSetsNoAddress()
    {
        Assert.IsNull(AssetPrecache.AssetUrlFor("https://play.evergrove.fyi", "https://play.evergrove.fyi", ""));
    }

    [TestMethod]
    public void TheShippedLauncherPreCachesForItsDefaultServer()
    {
        Assert.IsTrue(AssetPrecache.TryParseAssetUrl(
            AssetPrecache.AssetUrlFor(Constants.DefaultServerUrl, Constants.DefaultServerUrl, Constants.DefaultAssetUrl), out _));
    }

    [TestMethod]
    public void UrlOf_IsWhatTheGameAsksFor()
    {
        var file = new PrecacheFile("sg_fishing_medpond.gzne.z", 4712, 3470421027, new string('0', 64));

        Assert.AreEqual("http://play.evergrove.fyi:8080/assets/215/sg_fishing_medpond.gzne.z?3470421027",
            AssetPrecache.UrlOf(new Uri("http://play.evergrove.fyi:8080/assets"), file).AbsoluteUri);
    }
}
