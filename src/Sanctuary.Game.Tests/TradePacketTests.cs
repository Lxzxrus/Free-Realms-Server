using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Packet;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class TradePacketTests
{
    private static byte[] Wire(short opCode, BaseTradePacket.SubOpCode subOpCode, Action<PacketWriter> payload)
    {
        using var writer = new PacketWriter();

        writer.Write(opCode);
        writer.Write((byte)subOpCode);
        payload(writer);

        return writer.Buffer;
    }

    private static byte[] Wire(BaseTradePacket.SubOpCode subOpCode, Action<PacketWriter> payload) =>
        Wire(BaseTradePacket.OpCode, subOpCode, payload);

    [TestMethod]
    public void InviteReply_ReadsSessionAndAnswer()
    {
        var data = Wire(BaseTradePacket.SubOpCode.InviteReply, writer =>
        {
            writer.Write(77);
            writer.Write(true);
        });

        Assert.IsTrue(BaseTradePacket.TryDeserialize(data, out var command));
        Assert.AreEqual(new TradeInviteReplyCommand(77, true), command);
    }

    [TestMethod]
    public void EveryClientCommand_ReadsBackWhatTheClientSent()
    {
        Assert.IsTrue(BaseTradePacket.TryDeserialize(
            Wire(BaseTradePacket.SubOpCode.Accept, w => { w.Write(5); w.Write(9); }), out var accept));
        Assert.AreEqual(new TradeAcceptCommand(5, 9), accept);

        Assert.IsTrue(BaseTradePacket.TryDeserialize(
            Wire(BaseTradePacket.SubOpCode.Cancel, w => w.Write(5)), out var cancel));
        Assert.AreEqual(new TradeCancelCommand(5), cancel);

        Assert.IsTrue(BaseTradePacket.TryDeserialize(
            Wire(BaseTradePacket.SubOpCode.Lock, w => { w.Write(5); w.Write(9); w.Write(false); }), out var unlock));
        Assert.AreEqual(new TradeLockCommand(5, 9, false), unlock);

        Assert.IsTrue(BaseTradePacket.TryDeserialize(
            Wire(BaseTradePacket.SubOpCode.OfferItem, w => { w.Write(5); w.Write(12); w.Write(3); }), out var offer));
        Assert.AreEqual(new TradeOfferItemCommand(5, 12, 3), offer);

        Assert.IsTrue(BaseTradePacket.TryDeserialize(
            Wire(BaseTradePacket.SubOpCode.CoinCount, w => { w.Write(5); w.Write(250); }), out var coins));
        Assert.AreEqual(new TradeCoinCountCommand(5, 250), coins);
    }

    [TestMethod]
    public void PacketFromAnotherFamily_IsRefused()
    {
        var data = Wire(BaseQuestPacket.OpCode, BaseTradePacket.SubOpCode.Cancel, w => w.Write(5));

        Assert.IsFalse(BaseTradePacket.TryDeserialize(data, out var command));
        Assert.IsNull(command);
    }

    [TestMethod]
    public void ServerOnlySubOpCode_IsRefused()
    {
        var data = Wire(BaseTradePacket.SubOpCode.Confirm, w => w.Write(5));

        Assert.IsFalse(BaseTradePacket.TryDeserialize(data, out _));
    }

    [TestMethod]
    public void TruncatedOrPaddedPacket_IsRefused()
    {
        Assert.IsFalse(BaseTradePacket.TryDeserialize(
            Wire(BaseTradePacket.SubOpCode.OfferItem, w => { w.Write(5); w.Write(12); }), out _));

        Assert.IsFalse(BaseTradePacket.TryDeserialize(
            Wire(BaseTradePacket.SubOpCode.Cancel, w => { w.Write(5); w.Write((byte)0); }), out var command));
        Assert.IsNull(command);
    }

    [TestMethod]
    public void BooleanOtherThanZeroOrOne_IsRefused()
    {
        var data = Wire(BaseTradePacket.SubOpCode.InviteReply, w =>
        {
            w.Write(77);
            w.Write((byte)2);
        });

        Assert.IsFalse(BaseTradePacket.TryDeserialize(data, out _));
    }

    [TestMethod]
    public void ServerPackets_CarryTheTradeOpCodeAndSubOpCode()
    {
        var confirm = BaseTradePacket.CreateConfirm(42);

        CollectionAssert.AreEqual(
            Wire(BaseTradePacket.SubOpCode.Confirm, w => w.Write(42)),
            confirm);

        var itemCount = BaseTradePacket.CreateUpdateItemCount(42, 3, isSelf: true, itemId: 7, count: 2);

        CollectionAssert.AreEqual(
            Wire(BaseTradePacket.SubOpCode.UpdateItemCount, w =>
            {
                w.Write(42);
                w.Write(3);
                w.Write(true);
                w.Write(7);
                w.Write(2);
            }),
            itemCount);
    }
}
