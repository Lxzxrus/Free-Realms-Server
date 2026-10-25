using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// The result of a minigame that has ended. The client keeps it with the game and shows it on the end screen, which
/// opens when the <see cref="MiniGameLeavePacket"/> that follows arrives.
/// </summary>
/// <remarks>
/// From FreeRealms.exe: the minigame dispatcher (0x9bf890) builds the packet for sub-opcode 0x12 (constructor 0x9a9ab0),
/// reads a bool and three ints after the base fields (0x9b48b0), and hands them to <c>MiniGame:OnGameOver</c>
/// (0x9ad480), which stores them on the game. <c>MiniGame:ShowMiniGameEndDialog</c> (0x9b7170) passes them to the UI's
/// <c>setMiniGameEndData(status, goals, bonus goals, time, stars, score)</c>: the bool as the status, the second int
/// as minutes and seconds, the first as the stars, the third as the score.
/// </remarks>
public class MiniGameGameOverPacket : BaseMiniGamePacket, ISerializablePacket
{
    public new const byte OpCode = 18;

    public bool Won;

    public int StarsEarned;

    /// <summary>Shown as minutes and seconds; the UI calls it the time remaining.</summary>
    public int Seconds;

    public int Score;

    public MiniGameGameOverPacket(int stateId) : base(OpCode, stateId, -1, -1)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Won);
        writer.Write(StarsEarned);
        writer.Write(Seconds);
        writer.Write(Score);

        return writer.Buffer;
    }
}
