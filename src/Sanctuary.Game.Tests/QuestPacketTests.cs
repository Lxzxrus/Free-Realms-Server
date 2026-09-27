using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Packet;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class QuestPacketTests
{
    private static byte[] Wire(short opCode, int subOpCode, int questId, bool accepted)
    {
        using var writer = new PacketWriter();

        writer.Write(opCode);
        writer.Write(subOpCode);
        writer.Write(questId);
        writer.Write(accepted);

        return writer.Buffer;
    }

    [TestMethod]
    public void QuestReply_ReadsBackWhatTheClientSent()
    {
        var data = Wire(BaseQuestPacket.OpCode, QuestReplyPacket.SubOpCode, 2563, true);

        Assert.IsTrue(QuestReplyPacket.TryDeserialize(data, out var packet));
        Assert.AreEqual(2563, packet.QuestId);
        Assert.IsTrue(packet.Accepted);
    }

    [TestMethod]
    public void QuestReply_KeepsRefusalsDistinctFromAcceptances()
    {
        var data = Wire(BaseQuestPacket.OpCode, QuestReplyPacket.SubOpCode, 2564, false);

        Assert.IsTrue(QuestReplyPacket.TryDeserialize(data, out var packet));
        Assert.AreEqual(2564, packet.QuestId);
        Assert.IsFalse(packet.Accepted);
    }

    /// <summary>
    /// The opcode guard used &amp;&amp; where || was meant, so a packet from another family was read as
    /// a quest reply instead of being rejected.
    /// </summary>
    [TestMethod]
    public void QuestReply_RefusesAPacketFromAnotherFamily()
    {
        var data = Wire(BaseQuestPacket.OpCode + 1, QuestReplyPacket.SubOpCode, 2563, true);

        Assert.IsFalse(QuestReplyPacket.TryDeserialize(data, out _));
    }

    [TestMethod]
    public void QuestReply_RefusesAnotherQuestPacketsSubOpCode()
    {
        var data = Wire(BaseQuestPacket.OpCode, QuestAbandonedPacket.SubOpCode, 2563, true);

        Assert.IsFalse(QuestReplyPacket.TryDeserialize(data, out _));
    }

    [TestMethod]
    public void QuestReply_RefusesATruncatedPacket()
    {
        var full = Wire(BaseQuestPacket.OpCode, QuestReplyPacket.SubOpCode, 2563, true);

        Assert.IsFalse(QuestReplyPacket.TryDeserialize(full.AsSpan(0, 6), out _));
    }

    [TestMethod]
    public void QuestAbandoned_ReadsBackTheQuestId()
    {
        using var writer = new PacketWriter();

        writer.Write(BaseQuestPacket.OpCode);
        writer.Write(QuestAbandonedPacket.SubOpCode);
        writer.Write(2563);

        Assert.IsTrue(QuestAbandonedPacket.TryDeserialize(writer.Buffer, out var packet));
        Assert.AreEqual(2563, packet.QuestId);
    }

    /// <summary>
    /// Both packets used to call their sub-opcode <c>OpCode</c>, hiding the quest family's opcode.
    /// Writing them must still lead with the family (49), then the sub-opcode.
    /// </summary>
    [TestMethod]
    public void QuestAbandoned_WritesItsFamilyAndSubOpCodeFirst()
    {
        var reader = new PacketReader(new QuestAbandonedPacket { QuestId = 2563 }.Serialize());

        Assert.IsTrue(reader.TryRead(out short opCode));
        Assert.AreEqual(BaseQuestPacket.OpCode, opCode);

        Assert.IsTrue(reader.TryRead(out int subOpCode));
        Assert.AreEqual(QuestAbandonedPacket.SubOpCode, subOpCode);

        Assert.IsTrue(reader.TryRead(out int questId));
        Assert.AreEqual(2563, questId);
    }

    [TestMethod]
    public void QuestComplete_WritesItsFamilyAndSubOpCodeFirst()
    {
        var reader = new PacketReader(new QuestCompletePacket { QuestId = 2563 }.Serialize());

        Assert.IsTrue(reader.TryRead(out short opCode));
        Assert.AreEqual(BaseQuestPacket.OpCode, opCode);

        Assert.IsTrue(reader.TryRead(out int subOpCode));
        Assert.AreEqual(QuestCompletePacket.SubOpCode, subOpCode);

        Assert.IsTrue(reader.TryRead(out int questId));
        Assert.AreEqual(2563, questId);
    }

    private static byte[] TakeMeThereWire(short opCode)
    {
        using var writer = new PacketWriter();

        writer.Write(opCode);
        writer.Write(1);
        writer.Write(2);
        writer.Write("TakeMeThere");
        writer.Write(7);

        return writer.Buffer;
    }

    [TestMethod]
    public void TakeMeThere_ReadsBackTheRequestId()
    {
        Assert.IsTrue(TakeMeThereRequestPacket.TryDeserialize(TakeMeThereWire(TakeMeThereRequestPacket.OpCode), out var packet));
        Assert.AreEqual("TakeMeThere", packet.ActionName);
        Assert.AreEqual(7, packet.RequestId);
    }

    /// <summary>
    /// Same &amp;&amp;-for-|| guard as the quest family: any opcode was accepted.
    /// </summary>
    [TestMethod]
    public void TakeMeThere_RefusesAnotherOpCode()
    {
        Assert.IsFalse(TakeMeThereRequestPacket.TryDeserialize(TakeMeThereWire(TakeMeThereRequestPacket.OpCode + 1), out _));
    }
}
