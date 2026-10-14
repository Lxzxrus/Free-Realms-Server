using System;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Helpers;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A client can send a 64 KiB packet as often as it likes; logging all of it as hex on every fault would let
/// it fill the disk (task 30).
/// </summary>
[TestClass]
public sealed class PacketLogTests
{
    [TestMethod]
    public void ShortPackets_AreLoggedWhole()
    {
        Assert.AreEqual("0102FF", PacketLog.Hex([1, 2, 255]));
    }

    [TestMethod]
    public void LongPackets_AreCut_AndSayHowLongTheyWere()
    {
        var hex = PacketLog.Hex(new byte[65_536]);

        Assert.IsTrue(hex.Length < 2 * PacketLog.MaxBytes + 32, hex.Length.ToString());
        Assert.IsTrue(hex.EndsWith("... (65536 bytes)", StringComparison.Ordinal), hex);
    }

    [TestMethod]
    public void Text_IsOneLine_AndCut()
    {
        Assert.AreEqual("a b", PacketLog.Text("a\nb"));

        var text = PacketLog.Text(string.Concat(Enumerable.Repeat('x', 1000)), 10);
        Assert.AreEqual("xxxxxxxxxx... (1000 chars)", text);
    }
}
