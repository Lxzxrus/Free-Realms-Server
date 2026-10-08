using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Launcher.Helpers;

namespace Launcher.Tests;

[TestClass]
public class ClientVerifierTests
{
    private static readonly byte[] GameDll = Encoding.ASCII.GetBytes("the official game library");
    private static readonly byte[] PackData = Encoding.ASCII.GetBytes("official pack data");

    private string _root = null!;
    private string _client = null!;
    private Dictionary<string, byte[]> _official = null!;
    private ClientPinSet _pins = null!;
    private int _fetches;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        _client = Path.Combine(_root, "Client");
        Directory.CreateDirectory(_client);

        _official = new Dictionary<string, byte[]>
        {
            ["GameLib.dll"] = GameDll,
            ["Data/Assets_000.pack"] = PackData
        };
        _pins = new ClientPinSet(_official.Select(pair => new ClientFilePin(pair.Key, pair.Value.Length, Sha256(pair.Value))));
        _fetches = 0;
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, recursive: true);

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private ClientVerifier Verifier(Func<string, byte[]>? serve = null) => new(
        _pins,
        _client,
        Path.Combine(_root, "ClientCheck.cache"),
        (path, _) =>
        {
            _fetches++;
            return Task.FromResult<Stream>(new MemoryStream(serve?.Invoke(path) ?? _official[path]));
        });

    private void WriteClientFile(string relativePath, byte[] content)
    {
        var path = Path.Combine(_client, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private byte[] ReadClientFile(string relativePath) => File.ReadAllBytes(Path.Combine(_client, relativePath));

    private void WriteOfficialClient()
    {
        foreach (var (path, content) in _official)
            WriteClientFile(path, content);
    }

    [TestMethod]
    public async Task OfficialClient_IsClean()
    {
        WriteOfficialClient();

        var result = await Verifier().CheckAsync();

        Assert.IsTrue(result.IsClean);
    }

    [TestMethod]
    public async Task TamperedDll_IsFoundAndReplacedFromTheOfficialDownload()
    {
        WriteOfficialClient();
        WriteClientFile("GameLib.dll", Encoding.ASCII.GetBytes("the official game librarX"));

        var verifier = Verifier();
        var result = await verifier.CheckAsync();

        Assert.AreEqual("GameLib.dll", result.Bad.Single().Path);

        var failed = await verifier.RepairAsync(result.Bad);

        Assert.AreEqual(0, failed.Count);
        CollectionAssert.AreEqual(GameDll, ReadClientFile("GameLib.dll"));
        Assert.IsTrue((await verifier.CheckAsync()).IsClean);
    }

    [TestMethod]
    public async Task MissingFile_IsFetched()
    {
        WriteClientFile("GameLib.dll", GameDll);

        var verifier = Verifier();
        var result = await verifier.CheckAsync();

        Assert.AreEqual("Data/Assets_000.pack", result.Bad.Single().Path);
        Assert.AreEqual(0, (await verifier.RepairAsync(result.Bad)).Count);
        CollectionAssert.AreEqual(PackData, ReadClientFile(Path.Combine("Data", "Assets_000.pack")));
    }

    [TestMethod]
    public async Task DownloadThatFailsItsPin_IsNeverWritten()
    {
        var tampered = Encoding.ASCII.GetBytes("the official game librarX");
        WriteOfficialClient();
        WriteClientFile("GameLib.dll", tampered);

        // The download is just as wrong (same size, different bytes), or too long.
        foreach (var served in new[] { Encoding.ASCII.GetBytes("a malicious game library!"), GameDll.Concat(new byte[] { 0 }).ToArray() })
        {
            var verifier = Verifier(_ => served);
            var failed = await verifier.RepairAsync(_pins.All.Where(pin => pin.Path == "GameLib.dll"));

            Assert.AreEqual(1, failed.Count);
            CollectionAssert.AreEqual(tampered, ReadClientFile("GameLib.dll"));
            Assert.IsFalse(Directory.EnumerateFiles(_client).Any(path => path.EndsWith(".launcher-download")));
        }
    }

    [TestMethod]
    public async Task UnofficialCode_IsFoundAndQuarantined_DataIsLeftAlone()
    {
        WriteOfficialClient();
        WriteClientFile("dinput8.dll", Encoding.ASCII.GetBytes("a proxy dll"));
        WriteClientFile(Path.Combine("Miles", "evil.asi"), Encoding.ASCII.GetBytes("a sound plugin"));
        WriteClientFile(Path.Combine("UI", "housingEditPanel.gfx"), Encoding.ASCII.GetBytes("a UI mod"));

        var verifier = Verifier();
        var result = await verifier.CheckAsync();

        CollectionAssert.AreEquivalent(new[] { "dinput8.dll", Path.Combine("Miles", "evil.asi") }, result.UnknownCode.ToArray());

        var quarantine = Path.Combine(_root, "Quarantine");
        Assert.AreEqual(0, verifier.Quarantine(result.UnknownCode, quarantine).Count);

        Assert.IsFalse(File.Exists(Path.Combine(_client, "dinput8.dll")));
        Assert.IsTrue(File.Exists(Path.Combine(quarantine, "dinput8.dll")));
        Assert.IsTrue(File.Exists(Path.Combine(quarantine, "Miles", "evil.asi")));
        Assert.IsTrue(File.Exists(Path.Combine(_client, "UI", "housingEditPanel.gfx")));
        Assert.IsTrue((await verifier.CheckAsync()).IsClean);
    }

    [TestMethod]
    public async Task CodeCheck_HashesCodeEveryTime()
    {
        WriteOfficialClient();
        var verifier = Verifier();
        Assert.IsTrue((await verifier.CheckAsync()).IsClean);

        // Same size, same write time as when it passed: only a hash catches it.
        var path = Path.Combine(_client, "GameLib.dll");
        var stamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("the official game librarX"));
        File.SetLastWriteTimeUtc(path, stamp);

        var result = await verifier.CheckCodeAsync();

        Assert.AreEqual("GameLib.dll", result.Bad.Single().Path);
    }

    [TestMethod]
    [DataRow("FreeRealms.exe", true)]
    [DataRow("dinput8.DLL", true)]
    [DataRow("Miles/mssmp3.asi", true)]
    [DataRow("Miles/mssdsp.flt", true)]
    [DataRow("start.bat", true)]
    [DataRow("UI/housingEditPanel.gfx", false)]
    [DataRow("Assets_000.pack", false)]
    [DataRow("Resources/Tints.xml", false)]
    public void CodeFiles_AreRecognised(string path, bool isCode)
    {
        Assert.AreEqual(isCode, ClientCodeFiles.IsCode(path));
    }

    [TestMethod]
    public void OfficialPins_CoverTheWholeClient()
    {
        var pins = ClientPinSet.Official;

        Assert.IsTrue(pins.All.Count > 1700);
        Assert.IsTrue(pins.TryGet("FreeRealms.exe", out var executable));
        Assert.IsTrue(pins.TryGet(@"miles\MSS32.dll", out _) || pins.TryGet("mss32.dll", out _));
        Assert.IsTrue(pins.All.All(pin => ClientPin.IsValidHash(pin.Sha256) && pin.Size >= 0));

        // The executable pin set at build time, when it is set, must be the same game.
        if (ClientPin.IsValidHash(Constants.ClientExecutableSha256))
            Assert.AreEqual(executable.Sha256, Constants.ClientExecutableSha256.Trim().ToLowerInvariant());
    }
}
