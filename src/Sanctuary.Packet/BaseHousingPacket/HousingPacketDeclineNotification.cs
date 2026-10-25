using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

/// <summary>
/// Tells the inviter their house invite was declined: "[invited_player] has declined your house invitation."
/// </summary>
/// <remarks>
/// From FreeRealms.exe: the reader (0xab6bf0, constructor 0xab3c60, sub-opcode 0x30) reads two 8-byte values and a
/// name; the handler (0xabaca0) shows only the name, as <c>invited_player</c>. The two values' meanings are guesses.
/// </remarks>
public class HousingPacketDeclineNotification : BaseHousingPacket, ISerializablePacket
{
    public new const short OpCode = 48;

    /// <summary>The player who declined.</summary>
    public ulong PlayerGuid;

    public ulong HouseGuid;

    /// <summary>The player who declined.</summary>
    public NameData Name = new();

    public HousingPacketDeclineNotification() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(PlayerGuid);
        writer.Write(HouseGuid);

        Name.Serialize(writer);

        return writer.Buffer;
    }
}
