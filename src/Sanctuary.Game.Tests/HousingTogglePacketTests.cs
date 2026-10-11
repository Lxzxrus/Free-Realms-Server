using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Packet;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class HousingTogglePacketTests
{
    private static byte[] Wire(short opCode, short subOpCode, params byte[] payload)
    {
        using var writer = new PacketWriter();

        writer.Write(opCode);
        writer.Write(subOpCode);

        foreach (var value in payload)
            writer.Write(value);

        return writer.Buffer;
    }

    /// <summary>
    /// The client's names for housing sub-opcodes 11 to 13, as listed in <c>PacketReaderExtensions</c>.
    /// </summary>
    [TestMethod]
    public void SubOpCodes_MatchTheClient()
    {
        // As one list: comparing two constants trips MSTEST0025, which takes it for an assert that can never pass.
        short[] subOpCodes =
        [
            ClientHousingPacketToggleLocked.OpCode,
            ClientHousingPacketToggleFloraAllowed.OpCode,
            ClientHousingPacketTogglePetAutospawn.OpCode
        ];

        CollectionAssert.AreEqual(new short[] { 11, 12, 13 }, subOpCodes);
    }

    [TestMethod]
    public void Toggles_ReadWhatTheClientSends()
    {
        Assert.IsTrue(ClientHousingPacketToggleLocked.TryDeserialize(
            Wire(BaseHousingPacket.OpCode, ClientHousingPacketToggleLocked.OpCode), out _));
        Assert.IsTrue(ClientHousingPacketToggleFloraAllowed.TryDeserialize(
            Wire(BaseHousingPacket.OpCode, ClientHousingPacketToggleFloraAllowed.OpCode), out _));
        Assert.IsTrue(ClientHousingPacketTogglePetAutospawn.TryDeserialize(
            Wire(BaseHousingPacket.OpCode, ClientHousingPacketTogglePetAutospawn.OpCode), out _));
    }

    [TestMethod]
    public void Toggles_RefuseEachOthersSubOpCode()
    {
        Assert.IsFalse(ClientHousingPacketToggleLocked.TryDeserialize(
            Wire(BaseHousingPacket.OpCode, ClientHousingPacketToggleFloraAllowed.OpCode), out _));
        Assert.IsFalse(ClientHousingPacketToggleFloraAllowed.TryDeserialize(
            Wire(BaseHousingPacket.OpCode, ClientHousingPacketTogglePetAutospawn.OpCode), out _));
        Assert.IsFalse(ClientHousingPacketTogglePetAutospawn.TryDeserialize(
            Wire(BaseHousingPacket.OpCode, ClientHousingPacketToggleLocked.OpCode), out _));
    }

    [TestMethod]
    public void Toggles_RefuseAPacketFromAnotherFamily()
    {
        Assert.IsFalse(ClientHousingPacketToggleLocked.TryDeserialize(
            Wire(BaseHousingPacket.OpCode + 1, ClientHousingPacketToggleLocked.OpCode), out _));
    }

    [TestMethod]
    public void Toggles_RefuseTrailingBytes()
    {
        Assert.IsFalse(ClientHousingPacketToggleLocked.TryDeserialize(
            Wire(BaseHousingPacket.OpCode, ClientHousingPacketToggleLocked.OpCode, 1), out _));
    }
}
