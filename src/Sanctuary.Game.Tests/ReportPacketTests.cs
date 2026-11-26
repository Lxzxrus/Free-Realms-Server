using System;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Packet;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The report packets, built in the layout the client sends (docs/research/reports.md): int16 opcode, int16 sub-opcode,
/// then the fields. Every strict-parsing rule is here: the whole packet is read and nothing may follow, the counts are
/// bounded, the opcode and the sub-opcode must both be right.
/// </summary>
[TestClass]
public sealed class ReportPacketTests
{
    private static byte[] Wire(Action<PacketWriter> write)
    {
        using var writer = new PacketWriter();

        write(writer);

        return writer.Buffer;
    }

    private static byte[] PlayerReport(string name, params (int Channel, string Message, ulong Time)[] lines) => Wire(w =>
    {
        w.Write((short)68);
        w.Write((short)1);
        w.Write(name);
        w.Write(lines.Length);

        foreach (var (channel, message, time) in lines)
        {
            w.Write(channel);
            w.Write(message);
            w.Write(time);
        }
    });

    private static byte[] HouseReport() => Wire(w =>
    {
        w.Write((short)68);
        w.Write((short)3);
        w.Write(178UL);
        w.Write(33UL);
        w.Write("Wugachug Lot");
        w.Write("Bob Smith");
    });

    private static byte[] BugReport(string xml, int first = 0, string second = "", int third = 0) => Wire(w =>
    {
        w.Write((short)83);
        w.Write((short)1);
        w.Write(first);
        w.Write(second);
        w.Write(third);
        w.Write(xml);
    });

    // BubbaJoeX's own test wire, to show the layout we read is the one he tested against the client.
    [TestMethod]
    public void PlayerReport_ReadsBubbaJoeXsSampleBytes()
    {
        var bytes = Convert.FromHexString("44000100" + "07000000" + Convert.ToHexString("Griefer"u8.ToArray()) + "01000000"
            + "01000000" + "07000000" + Convert.ToHexString("go away"u8.ToArray()) + "00F1536500000000");

        Assert.IsTrue(ReportPacketReportPlayer.TryDeserialize(bytes, out var report));
        Assert.AreEqual("Griefer", report.ReportedName);
        Assert.AreEqual((1, "go away", 0x6553F100UL), report.Lines.Single());
    }

    [TestMethod]
    public void PlayerReport_RoundTrips()
    {
        var bytes = PlayerReport("Bob Smith", (1, "hello", 100UL), (2, "héllo wörld", 200UL), (0, string.Empty, ulong.MaxValue));

        Assert.IsTrue(ReportPacketReportPlayer.TryDeserialize(bytes, out var report));
        Assert.AreEqual("Bob Smith", report.ReportedName);
        CollectionAssert.AreEqual(new[] { (1, "hello", 100UL), (2, "héllo wörld", 200UL), (0, string.Empty, ulong.MaxValue) }, report.Lines);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(10)]
    public void PlayerReport_AcceptsZeroToTenLines(int count)
    {
        var lines = Enumerable.Range(0, count).Select(i => (i, "line " + i, (ulong)i)).ToArray();

        Assert.IsTrue(ReportPacketReportPlayer.TryDeserialize(PlayerReport("Bob", lines), out var report));
        Assert.AreEqual(count, report.Lines.Count);
    }

    [TestMethod]
    public void PlayerReport_RefusesMoreThanTenLines()
    {
        var lines = Enumerable.Range(0, 11).Select(i => (i, "line", (ulong)i)).ToArray();

        Assert.IsFalse(ReportPacketReportPlayer.TryDeserialize(PlayerReport("Bob", lines), out _));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    [DataRow(11)]
    [DataRow(int.MaxValue)]
    public void PlayerReport_RefusesACountOutsideZeroToTen(int count)
    {
        // The count says more or fewer lines than follow; it is refused before any line is read.
        var bytes = Wire(w => { w.Write((short)68); w.Write((short)1); w.Write("Bob"); w.Write(count); });

        Assert.IsFalse(ReportPacketReportPlayer.TryDeserialize(bytes, out _));
    }

    [TestMethod]
    public void PlayerReport_RefusesANegativeStringLength()
    {
        var bytes = Wire(w => { w.Write((short)68); w.Write((short)1); w.Write(-5); w.Write(0); });

        Assert.IsFalse(ReportPacketReportPlayer.TryDeserialize(bytes, out _));
    }

    [TestMethod]
    public void PlayerReport_RefusesAStringLongerThanThePacket()
    {
        var bytes = Wire(w => { w.Write((short)68); w.Write((short)1); w.Write(1_000_000); w.Write(new byte[4]); });

        Assert.IsFalse(ReportPacketReportPlayer.TryDeserialize(bytes, out _));
    }

    [TestMethod]
    public void PlayerReport_RefusesEveryTruncation()
    {
        var bytes = PlayerReport("Bob", (1, "hello", 100UL), (2, "again", 200UL));

        for (var length = 0; length < bytes.Length; length++)
            Assert.IsFalse(ReportPacketReportPlayer.TryDeserialize(bytes.AsSpan(0, length), out _), $"accepted a packet cut to {length} bytes");

        Assert.IsTrue(ReportPacketReportPlayer.TryDeserialize(bytes, out _));
    }

    [TestMethod]
    public void PlayerReport_RefusesTrailingBytes()
    {
        var bytes = PlayerReport("Bob", (1, "hello", 100UL)).Append((byte)0).ToArray();

        Assert.IsFalse(ReportPacketReportPlayer.TryDeserialize(bytes, out _));
    }

    [TestMethod]
    public void PlayerReport_RefusesTheHouseSubOpCodeAndAnotherOpCode()
    {
        var bytes = PlayerReport("Bob");

        var house = (byte[])bytes.Clone();
        house[2] = 3;
        Assert.IsFalse(ReportPacketReportPlayer.TryDeserialize(house, out _));

        var other = (byte[])bytes.Clone();
        other[0] = 69;
        Assert.IsFalse(ReportPacketReportPlayer.TryDeserialize(other, out _));

        // Right sub-opcode, wrong opcode: the guard is || (a && would take it).
        var sub = (byte[])bytes.Clone();
        sub[2] = 2;
        Assert.IsFalse(ReportPacketReportPlayer.TryDeserialize(sub, out _));
    }

    [TestMethod]
    public void HouseReport_RoundTrips()
    {
        Assert.IsTrue(ReportPacketReportHouse.TryDeserialize(HouseReport(), out var report));
        Assert.AreEqual(178UL, report.HouseGuid);
        Assert.AreEqual(33UL, report.OwnerGuid);
        Assert.AreEqual("Wugachug Lot", report.HouseName);
        Assert.AreEqual("Bob Smith", report.OwnerName);
    }

    [TestMethod]
    public void HouseReport_ReadsBubbaJoeXsSampleBytes()
    {
        var bytes = Convert.FromHexString("44000300" + "B200000000000000" + "2100000000000000"
            + "0C000000" + Convert.ToHexString("Wugachug Lot"u8.ToArray()) + "03000000" + Convert.ToHexString("Bob"u8.ToArray()));

        Assert.IsTrue(ReportPacketReportHouse.TryDeserialize(bytes, out var report));
        Assert.AreEqual(178UL, report.HouseGuid);
        Assert.AreEqual("Bob", report.OwnerName);
    }

    [TestMethod]
    public void HouseReport_RefusesTruncationTrailingBytesAndWrongSubOpCode()
    {
        var bytes = HouseReport();

        for (var length = 0; length < bytes.Length; length++)
            Assert.IsFalse(ReportPacketReportHouse.TryDeserialize(bytes.AsSpan(0, length), out _), $"accepted a packet cut to {length} bytes");

        Assert.IsFalse(ReportPacketReportHouse.TryDeserialize(bytes.Append((byte)1).ToArray(), out _));

        // Sub-opcode 2 is not the house report: the exe uses 3.
        var wrong = (byte[])bytes.Clone();
        wrong[2] = 2;
        Assert.IsFalse(ReportPacketReportHouse.TryDeserialize(wrong, out _));

        // And a player report's bytes are not a house report.
        Assert.IsFalse(ReportPacketReportHouse.TryDeserialize(PlayerReport("Bob"), out _));
    }

    [TestMethod]
    public void BugReport_RoundTrips()
    {
        const string xml = "<BugSubmission Category=\"Gameplay\" Severity=\"2\" BugDescription=\"It broke\"/>";

        Assert.IsTrue(BugSubmissionPacketAddBug.TryDeserialize(BugReport(xml), out var report));
        Assert.AreEqual(xml, report.BugXml);
        Assert.AreEqual(0, report.Unknown1);
        Assert.AreEqual(string.Empty, report.Unknown2);
        Assert.AreEqual(0, report.Unknown3);
    }

    [TestMethod]
    public void BugReport_RefusesTruncationTrailingBytesAndWrongOpCodes()
    {
        var bytes = BugReport("<BugSubmission/>");

        for (var length = 0; length < bytes.Length; length++)
            Assert.IsFalse(BugSubmissionPacketAddBug.TryDeserialize(bytes.AsSpan(0, length), out _), $"accepted a packet cut to {length} bytes");

        Assert.IsFalse(BugSubmissionPacketAddBug.TryDeserialize(bytes.Append((byte)1).ToArray(), out _));

        var opCode = (byte[])bytes.Clone();
        opCode[0] = 84;
        Assert.IsFalse(BugSubmissionPacketAddBug.TryDeserialize(opCode, out _));

        var subOpCode = (byte[])bytes.Clone();
        subOpCode[2] = 2;
        Assert.IsFalse(BugSubmissionPacketAddBug.TryDeserialize(subOpCode, out _));
    }

    [TestMethod]
    public void BugReport_RefusesALengthLongerThanThePacket()
    {
        // The client copies at most a fixed buffer of the text but writes the full length: such a packet is malformed.
        var bytes = Wire(w => { w.Write((short)83); w.Write((short)1); w.Write(0); w.Write(0); w.Write(0); w.Write(50_000); w.Write(new byte[100]); });

        Assert.IsFalse(BugSubmissionPacketAddBug.TryDeserialize(bytes, out _));
    }
}
