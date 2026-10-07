using Microsoft.VisualStudio.TestTools.UnitTesting;

using Launcher.Helpers;

namespace Launcher.Tests;

[TestClass]
public class LaunchArgumentsTests
{
    [TestMethod]
    public void Server_arguments_are_passed_through_split_on_spaces()
    {
        var arguments = LaunchArguments.Build(
            "play.example.com:20042",
            "0123456789abcdef",
            "en_US",
            "AssetDelivery:IndirectServerAddress=http://assets.example.com/assets  Portrait:UploadUrl=https://play.example.com/image/00ff");

        CollectionAssert.AreEqual(new[]
        {
            "Server=play.example.com:20042",
            "SessionId=0123456789abcdef",
            "Internationalization:Locale=en_US",
            "AssetDelivery:IndirectServerAddress=http://assets.example.com/assets",
            "Portrait:UploadUrl=https://play.example.com/image/00ff"
        }, arguments);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void No_server_arguments(string? serverArguments)
    {
        var arguments = LaunchArguments.Build("play.example.com:20042", "abc", "en_US", serverArguments);

        Assert.HasCount(3, arguments);
    }
}
