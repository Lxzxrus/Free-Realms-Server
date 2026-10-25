using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;
using Sanctuary.Packet;

namespace Sanctuary.Game.Tests;

/// <summary>Minigame packets, against the layouts read from FreeRealms.exe.</summary>
[TestClass]
public sealed class MiniGamePacketTests
{
    [TestMethod]
    public void GameOver_IsTheBaseFields_ThenTheResult()
    {
        var packet = new MiniGameGameOverPacket(20_001) { Won = true, StarsEarned = 15, Seconds = 95, Score = 2400 };

        using var expected = new PacketWriter();
        expected.Write(BaseMiniGamePacket.OpCode);
        expected.Write((byte)18);
        expected.Write(20_001);
        expected.Write(-1);
        expected.Write(-1);
        expected.Write(true);
        expected.Write(15);
        expected.Write(95);
        expected.Write(2400);

        CollectionAssert.AreEqual(expected.Buffer, packet.Serialize());
    }
}
