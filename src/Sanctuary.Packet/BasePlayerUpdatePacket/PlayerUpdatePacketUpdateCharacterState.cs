using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// A character's state bits. Bit 0x400000 is fishing: the client switches to its fishing controls and asks to
/// register (<see cref="FishingPacketRegisterPlayerResponse"/>). From edenfps' fishing (upstream PR #31).
/// </summary>
public class PlayerUpdatePacketUpdateCharacterState : BasePlayerUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 20;

    public const int FishingState = 0x400000;

    public ulong Guid;

    public int State;

    public PlayerUpdatePacketUpdateCharacterState() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);

        writer.Write(State);

        return writer.Buffer;
    }
}
