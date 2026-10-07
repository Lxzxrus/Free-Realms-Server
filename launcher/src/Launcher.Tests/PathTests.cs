using System.IO;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Launcher.Extensions;
using Launcher.Helpers;

namespace Launcher.Tests;

[TestClass]
public class PathTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "launcher-tests", "Client");

    [TestMethod]
    [DataRow("", "FreeRealms.exe")]
    [DataRow("Resources", "Assets_000.pack")]
    [DataRow("a/b", "c.txt")]
    [DataRow("a/../b", "c.txt")]
    public void Files_inside_the_client_folder(string directory, string fileName)
    {
        Assert.IsTrue(PathHelper.TryGetPathInside(Root, directory, fileName, out var fullPath));
        StringAssert.StartsWith(fullPath, Root + Path.DirectorySeparatorChar);
    }

    [TestMethod]
    [DataRow("", "../evil.exe")]
    [DataRow("..", "evil.exe")]
    [DataRow("a/../..", "evil.exe")]
    [DataRow("", "../Client2/evil.exe")]
    [DataRow("", "")]
    [DataRow("", ".")]
    public void Files_outside_the_client_folder_are_refused(string directory, string fileName)
    {
        Assert.IsFalse(PathHelper.TryGetPathInside(Root, directory, fileName, out _));
    }

    [TestMethod]
    public void Rooted_names_are_refused()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "evil.exe");

        Assert.IsFalse(PathHelper.TryGetPathInside(Root, "", rooted, out _));
        Assert.IsFalse(PathHelper.TryGetPathInside(Root, rooted, "x", out _));
    }

    [TestMethod]
    [DataRow("My Server", "My Server")]
    [DataRow("../../Startup", ".._.._Startup")]
    [DataRow("..", "Server")]
    [DataRow("  ", "Server")]
    [DataRow("C:\\Windows", "C__Windows")]
    public void Server_names_become_one_folder(string name, string expected)
    {
        var folder = name.ToValidDirectoryName();

        Assert.AreEqual(expected, folder);
        Assert.AreEqual(-1, folder.IndexOfAny(['/', '\\']));
    }
}
