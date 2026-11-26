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

    [TestMethod]
    [DataRow(0x000D, DisplayName = "carriage return")]
    [DataRow(0x001B, DisplayName = "escape, which starts a terminal escape sequence")]
    [DataRow(0x0085, DisplayName = "next line (NEL)")]
    [DataRow(0x2028, DisplayName = "line separator")]
    [DataRow(0x2029, DisplayName = "paragraph separator")]
    [DataRow(0x202E, DisplayName = "right-to-left override")]
    [DataRow(0x2066, DisplayName = "bidirectional isolate")]
    [DataRow(0x0000, DisplayName = "NUL")]
    public void Text_CarriesNoControlOrReorderingCharacters(int codePoint)
    {
        var text = PacketLog.Text("a" + (char)codePoint + "[2Jb");

        Assert.AreEqual("a [2Jb", text);
    }

    [TestMethod]
    public void Text_KeepsOrdinaryText()
    {
        Assert.AreEqual("Zoë O'Malley 1.910.1.530630", PacketLog.Text("Zoë O'Malley 1.910.1.530630"));
        Assert.AreEqual(string.Empty, PacketLog.Text(null));
        Assert.AreEqual(string.Empty, PacketLog.Text(string.Empty));
    }

    [TestMethod]
    public void Text_DoesNotCutATwoUnitCharacterInHalf()
    {
        // "a" then an emoji (two UTF-16 units); a cut after 2 units would leave half of it
        var text = PacketLog.Text("a\U0001F600bcd", 2);

        Assert.AreEqual("a... (6 chars)", text);
    }
}
