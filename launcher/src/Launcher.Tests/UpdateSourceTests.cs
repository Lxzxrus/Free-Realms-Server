using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Launcher.Helpers;

namespace Launcher.Tests;

[TestClass]
public class UpdateSourceTests
{
    [TestMethod]
    [DataRow("https://github.com/Lxzxrus/Free-Realms-Server")]
    [DataRow("https://github.com/Lxzxrus/Free-Realms-Server/")]
    [DataRow("https://GitHub.com/Lxzxrus/Free-Realms-Server")]
    public void Uses_the_repository_github_releases(string url)
    {
        Assert.IsTrue(UpdateSource.TryCreate(url, out var source));
        Assert.AreEqual("https://github.com/Lxzxrus/Free-Realms-Server", source.RepoUri.AbsoluteUri);
        Assert.IsFalse(source.Prerelease);
    }

    [TestMethod]
    // The game server, which is what this replaces.
    [DataRow("https://play.example.com/launcher")]
    [DataRow("https://play.example.com/Lxzxrus/Free-Realms-Server")]
    // Velopack would treat these as GitHub Enterprise servers.
    [DataRow("https://github.com.evil.example/Lxzxrus/Free-Realms-Server")]
    [DataRow("https://192.168.1.20/Lxzxrus/Free-Realms-Server")]
    [DataRow("http://github.com/Lxzxrus/Free-Realms-Server")]
    [DataRow("https://github.com:8443/Lxzxrus/Free-Realms-Server")]
    [DataRow("https://user:token@github.com/Lxzxrus/Free-Realms-Server")]
    [DataRow("https://github.com/Lxzxrus")]
    [DataRow("https://github.com/Lxzxrus/Free-Realms-Server/releases")]
    [DataRow("https://github.com/Lxzxrus/../Free-Realms-Server")]
    [DataRow("https://github.com/Lxzxrus/Free-Realms-Server?x=1")]
    [DataRow("github.com/Lxzxrus/Free-Realms-Server")]
    [DataRow("")]
    public void Refuses_anything_but_a_github_repository(string url)
    {
        Assert.IsFalse(UpdateSource.TryCreate(url, out _));
    }

    [TestMethod]
    public void The_built_launcher_updates_from_github_not_the_game_server()
    {
        Assert.IsTrue(UpdateSource.TryGetRepository(Constants.UpdateRepositoryUrl, out var repository),
            $"UpdateRepositoryUrl '{Constants.UpdateRepositoryUrl}' in Directory.Build.props must be https://github.com/{{owner}}/{{repository}}.");

        var gameServer = new Uri(Constants.DefaultServerUrl);

        Assert.AreNotEqual(gameServer.IdnHost, repository.IdnHost, StringComparer.OrdinalIgnoreCase);
    }
}
