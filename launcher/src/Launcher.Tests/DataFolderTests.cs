using System;
using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Launcher.Helpers;

namespace Launcher.Tests;

[TestClass]
public class DataFolderTests
{
    private string _root = null!;
    private string _old = null!;
    private string _new = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"));
        _old = Path.Combine(_root, "EvergroveLauncher");
        _new = Path.Combine(_root, "EvergroveLauncherData");
        Directory.CreateDirectory(_old);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void MovesSettingsAndServersButLeavesVelopacksFiles()
    {
        File.WriteAllText(Path.Combine(_old, Constants.SettingsFile), "<settings/>");
        var client = Path.Combine(_old, Constants.ServersDirectory, "Free Realms Evergrove", "Client");
        Directory.CreateDirectory(client);
        File.WriteAllText(Path.Combine(client, "FreeRealms.exe"), "game");
        File.WriteAllText(Path.Combine(_old, "Update.exe"), "velopack");

        Assert.IsNotNull(DataFolder.MoveFromOldFolder(_old, _new));

        Assert.AreEqual("<settings/>", File.ReadAllText(Path.Combine(_new, Constants.SettingsFile)));
        Assert.AreEqual("game", File.ReadAllText(Path.Combine(_new, Constants.ServersDirectory, "Free Realms Evergrove", "Client", "FreeRealms.exe")));
        Assert.IsFalse(File.Exists(Path.Combine(_old, Constants.SettingsFile)));
        Assert.IsFalse(Directory.Exists(Path.Combine(_old, Constants.ServersDirectory)));
        Assert.IsTrue(File.Exists(Path.Combine(_old, "Update.exe")), "Velopack's files stay");
    }

    [TestMethod]
    public void DoesNothingWithoutOldSettings()
    {
        Directory.CreateDirectory(Path.Combine(_old, Constants.ServersDirectory));

        Assert.IsNull(DataFolder.MoveFromOldFolder(_old, _new));
        Assert.IsFalse(Directory.Exists(_new));
    }

    [TestMethod]
    public void NeverOverwritesNewSettings()
    {
        File.WriteAllText(Path.Combine(_old, Constants.SettingsFile), "old");
        Directory.CreateDirectory(_new);
        File.WriteAllText(Path.Combine(_new, Constants.SettingsFile), "new");

        Assert.IsNull(DataFolder.MoveFromOldFolder(_old, _new));
        Assert.AreEqual("new", File.ReadAllText(Path.Combine(_new, Constants.SettingsFile)));
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(_old, Constants.SettingsFile)));
    }

    [TestMethod]
    public void MovesSettingsWhenTheServersAlreadyMoved()
    {
        // A start that moved the servers but not the settings: the next one finishes the move.
        File.WriteAllText(Path.Combine(_old, Constants.SettingsFile), "<settings/>");
        Directory.CreateDirectory(Path.Combine(_new, Constants.ServersDirectory));

        Assert.IsNotNull(DataFolder.MoveFromOldFolder(_old, _new));
        Assert.IsTrue(File.Exists(Path.Combine(_new, Constants.SettingsFile)));
    }

    [TestMethod]
    public void TheDataFolderIsNotTheInstallFolder()
    {
        Assert.AreNotEqual(Path.GetFullPath(Constants.InstallDirectory), Path.GetFullPath(Constants.SavePath));
        StringAssert.StartsWith(Path.GetFullPath(Constants.LogsDirectory), Path.GetFullPath(Constants.SavePath));
    }
}
