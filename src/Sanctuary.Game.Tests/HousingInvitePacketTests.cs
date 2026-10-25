using System;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;

namespace Sanctuary.Game.Tests;

/// <summary>The house invite and naming packets, against the layouts read from FreeRealms.exe.</summary>
[TestClass]
public sealed class HousingInvitePacketTests
{
    private static PacketWriter Header(short subOpCode)
    {
        var writer = new PacketWriter();
        writer.Write(BaseHousingPacket.OpCode);
        writer.Write(subOpCode);
        return writer;
    }

    [TestMethod]
    public void SubOpCodes_MatchTheClient()
    {
        // As one list: comparing two constants trips MSTEST0025, which takes it for an assert that can never pass.
        short[] subOpCodes =
        [
            ClientHousingPacketInvitePlayer.OpCode,
            ClientHousingPacketDeclineInvite.OpCode,
            ClientHousingPacketRequestName.OpCode,
            HousingPacketInviteNotification.OpCode,
            HousingPacketDeclineNotification.OpCode
        ];

        CollectionAssert.AreEqual(new short[] { 15, 16, 17, 47, 48 }, subOpCodes);
    }

    [TestMethod]
    public void InvitePlayer_ReadsTheGuidAndTheUnusedField()
    {
        using var writer = Header(ClientHousingPacketInvitePlayer.OpCode);
        writer.Write(0x0102030405060708UL);
        writer.Write(0UL);

        Assert.IsTrue(ClientHousingPacketInvitePlayer.TryDeserialize(writer.Buffer, out var packet));
        Assert.AreEqual(0x0102030405060708UL, packet.PlayerGuid);
    }

    [TestMethod]
    public void InvitePlayer_RefusesAShortPacket()
    {
        using var writer = Header(ClientHousingPacketInvitePlayer.OpCode);
        writer.Write(42UL);

        Assert.IsFalse(ClientHousingPacketInvitePlayer.TryDeserialize(writer.Buffer, out _));
    }

    [TestMethod]
    public void DeclineInvite_ReadsTheHouseGuid()
    {
        using var writer = Header(ClientHousingPacketDeclineInvite.OpCode);
        writer.Write(77UL);

        Assert.IsTrue(ClientHousingPacketDeclineInvite.TryDeserialize(writer.Buffer, out var packet));
        Assert.AreEqual(77UL, packet.HouseGuid);
    }

    [TestMethod]
    public void DeclineInvite_RefusesTheInviteSubOpCode()
    {
        using var writer = Header(ClientHousingPacketInvitePlayer.OpCode);
        writer.Write(77UL);

        Assert.IsFalse(ClientHousingPacketDeclineInvite.TryDeserialize(writer.Buffer, out _));
    }

    [TestMethod]
    public void RequestName_ReadsTheNameWithOrWithoutTheTrailingByte()
    {
        using var withByte = Header(ClientHousingPacketRequestName.OpCode);
        withByte.Write("Mushroom Manor");
        withByte.Write((byte)0xCC);

        using var withoutByte = Header(ClientHousingPacketRequestName.OpCode);
        withoutByte.Write("Mushroom Manor");

        Assert.IsTrue(ClientHousingPacketRequestName.TryDeserialize(withByte.Buffer, out var first));
        Assert.AreEqual("Mushroom Manor", first.Name);
        Assert.IsTrue(ClientHousingPacketRequestName.TryDeserialize(withoutByte.Buffer, out var second));
        Assert.AreEqual("Mushroom Manor", second.Name);
    }

    [TestMethod]
    public void RequestName_RefusesMoreThanOneTrailingByte()
    {
        using var writer = Header(ClientHousingPacketRequestName.OpCode);
        writer.Write("Mushroom Manor");
        writer.Write((short)1);

        Assert.IsFalse(ClientHousingPacketRequestName.TryDeserialize(writer.Buffer, out _));
    }

    [TestMethod]
    public void InviteNotification_PutsTheHouseThirdAndTheInviterFirst()
    {
        var bytes = new HousingPacketInviteNotification
        {
            InviterGuid = 1,
            InviteeGuid = 2,
            HouseGuid = 3,
            OwnerGuid = 4,
            InviterName = new NameData { FirstName = "Nate", LastName = "Dev" },
            InviteeName = new NameData { FirstName = "Ariel" }
        }.Serialize();

        var reader = new PacketReader(bytes);
        Assert.IsTrue(reader.TryRead(out short opCode));
        Assert.AreEqual(BaseHousingPacket.OpCode, opCode);
        Assert.IsTrue(reader.TryRead(out short subOpCode));
        Assert.AreEqual(HousingPacketInviteNotification.OpCode, subOpCode);

        // The client enters the third guid when the invite is accepted.
        for (ulong expected = 1; expected <= 4; expected++)
        {
            Assert.IsTrue(reader.TryRead(out ulong value));
            Assert.AreEqual(expected, value);
        }

        // The prompt names the inviter from the first name.
        var inviter = new NameData();
        Assert.IsTrue(inviter.TryRead(ref reader));
        Assert.AreEqual("Nate Dev", inviter.FullName);

        var invitee = new NameData();
        Assert.IsTrue(invitee.TryRead(ref reader));
        Assert.AreEqual("Ariel", invitee.FullName);

        Assert.IsTrue(reader.TryRead(out ulong _));
        Assert.AreEqual(0, reader.RemainingLength);
    }

    [TestMethod]
    public void DeclineNotification_EndsWithTheDeclinersName()
    {
        var bytes = new HousingPacketDeclineNotification
        {
            PlayerGuid = 5,
            HouseGuid = 6,
            Name = new NameData { FirstName = "Ariel" }
        }.Serialize();

        // Header (4), two guids (16), three name ids (12), then the first name with its length.
        var name = Encoding.UTF8.GetBytes("Ariel");
        Assert.AreEqual(4 + 16 + 12 + 4 + name.Length + 4, bytes.Length);
        Assert.AreEqual(name.Length, BitConverter.ToInt32(bytes, 32));
        Assert.AreEqual("Ariel", Encoding.UTF8.GetString(bytes, 36, name.Length));
    }
}
