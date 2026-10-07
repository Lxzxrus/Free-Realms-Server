using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Launcher.Helpers;

namespace Launcher.Tests;

[TestClass]
public class ClientPinTests
{
    // SHA-256 of "abc", from FIPS 180-2.
    private static readonly byte[] Genuine = Encoding.ASCII.GetBytes("abc");
    private const string GenuineSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    private static readonly byte[] Tampered = Encoding.ASCII.GetBytes("abd");

    private string _clientDirectory = null!;

    [TestInitialize]
    public void CreateClientDirectory()
    {
        _clientDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"), "Client");
        Directory.CreateDirectory(_clientDirectory);
    }

    [TestCleanup]
    public void DeleteClientDirectory()
    {
        Directory.Delete(Path.GetDirectoryName(_clientDirectory)!, recursive: true);
    }

    private string ExecutablePath => Path.Combine(_clientDirectory, Constants.ClientExecutableName);

    [TestMethod]
    [DataRow(GenuineSha256)]
    [DataRow("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [DataRow(" " + GenuineSha256 + "\n")]
    public async Task The_expected_executable_passes(string expected)
    {
        await File.WriteAllBytesAsync(ExecutablePath, Genuine);

        Assert.AreEqual(ClientPinResult.Match, await ClientPin.CheckFileAsync(ExecutablePath, expected));
    }

    [TestMethod]
    public async Task Any_other_executable_fails()
    {
        await File.WriteAllBytesAsync(ExecutablePath, Tampered);

        Assert.AreEqual(ClientPinResult.Mismatch, await ClientPin.CheckFileAsync(ExecutablePath, GenuineSha256));
    }

    [TestMethod]
    public async Task A_missing_executable_is_reported_as_missing()
    {
        Assert.AreEqual(ClientPinResult.Missing, await ClientPin.CheckFileAsync(ExecutablePath, GenuineSha256));
        Assert.AreEqual(ClientPinResult.Missing, await ClientPin.CheckFileAsync(Path.Combine(_clientDirectory, "nope", "FreeRealms.exe"), GenuineSha256));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("abc")]
    [DataRow("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015a")]
    [DataRow("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015adz")]
    [DataRow("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public async Task Without_a_valid_pin_nothing_passes(string expected)
    {
        await File.WriteAllBytesAsync(ExecutablePath, Genuine);

        Assert.IsFalse(ClientPin.IsValidHash(expected));
        Assert.AreEqual(ClientPinResult.NotConfigured, await ClientPin.CheckFileAsync(ExecutablePath, expected));
        Assert.AreEqual(ClientPinResult.NotConfigured, await ClientPin.CheckAsync(new MemoryStream(Genuine), expected));
    }

    [TestMethod]
    public void The_pin_in_Directory_Build_props_is_empty_or_a_sha256()
    {
        // Empty means no game starts, which the release workflow refuses; anything else must be a real hash, or
        // every player would be refused.
        Assert.IsTrue(Constants.ClientExecutableSha256.Length == 0 || ClientPin.IsValidHash(Constants.ClientExecutableSha256),
            $"ClientExecutableSha256 '{Constants.ClientExecutableSha256}' must be the 64 hex digits of FreeRealms.exe's SHA-256.");
    }

    [TestMethod]
    [DataRow("FreeRealms.exe")]
    [DataRow("freerealms.exe")]
    [DataRow("FREEREALMS.EXE")]
    [DataRow("Resources/../FreeRealms.exe")]
    public void Recognises_the_executable_whatever_its_case(string relativePath)
    {
        Assert.IsTrue(ClientPin.IsClientExecutable(_clientDirectory, Path.Combine(_clientDirectory, relativePath)));
    }

    [TestMethod]
    [DataRow("Resources/FreeRealms.exe")]
    [DataRow("FreeRealms.exe.bak")]
    [DataRow("FreeRealms.dll")]
    public void Other_files_are_not_the_executable(string relativePath)
    {
        Assert.IsFalse(ClientPin.IsClientExecutable(_clientDirectory, Path.Combine(_clientDirectory, relativePath)));
    }

    [TestMethod]
    public async Task A_server_can_send_the_genuine_executable()
    {
        Assert.IsTrue(await ClientPin.TrySaveClientFileAsync(_clientDirectory, ExecutablePath, new MemoryStream(Genuine), GenuineSha256));

        CollectionAssert.AreEqual(Genuine, await File.ReadAllBytesAsync(ExecutablePath));
    }

    [TestMethod]
    public async Task A_server_cannot_replace_the_executable()
    {
        await File.WriteAllBytesAsync(ExecutablePath, Genuine);

        Assert.IsFalse(await ClientPin.TrySaveClientFileAsync(_clientDirectory, ExecutablePath, new MemoryStream(Tampered), GenuineSha256));

        CollectionAssert.AreEqual(Genuine, await File.ReadAllBytesAsync(ExecutablePath), "The genuine copy must be left alone.");
    }

    [TestMethod]
    public async Task A_server_cannot_add_the_executable_under_another_case()
    {
        var path = Path.Combine(_clientDirectory, "freerealms.exe");

        Assert.IsFalse(await ClientPin.TrySaveClientFileAsync(_clientDirectory, path, new MemoryStream(Tampered), GenuineSha256));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task Without_a_pin_a_server_cannot_send_the_executable()
    {
        Assert.IsFalse(await ClientPin.TrySaveClientFileAsync(_clientDirectory, ExecutablePath, new MemoryStream(Genuine), ""));
        Assert.IsFalse(File.Exists(ExecutablePath));
    }

    [TestMethod]
    public async Task Other_game_files_are_saved_as_sent()
    {
        var path = Path.Combine(_clientDirectory, "Assets_000.pack");

        Assert.IsTrue(await ClientPin.TrySaveClientFileAsync(_clientDirectory, path, new MemoryStream(Tampered), GenuineSha256));

        CollectionAssert.AreEqual(Tampered, await File.ReadAllBytesAsync(path));
    }

    [TestMethod]
    public async Task A_genuine_executable_on_disk_is_never_downloaded_again()
    {
        await File.WriteAllBytesAsync(ExecutablePath, Genuine);

        Assert.IsTrue(await ClientPin.IsPinnedClientInPlaceAsync(_clientDirectory, ExecutablePath, GenuineSha256));
    }

    [TestMethod]
    public async Task A_tampered_or_missing_executable_is_downloaded_and_checked()
    {
        Assert.IsFalse(await ClientPin.IsPinnedClientInPlaceAsync(_clientDirectory, ExecutablePath, GenuineSha256));

        await File.WriteAllBytesAsync(ExecutablePath, Tampered);

        Assert.IsFalse(await ClientPin.IsPinnedClientInPlaceAsync(_clientDirectory, ExecutablePath, GenuineSha256));
    }
}
