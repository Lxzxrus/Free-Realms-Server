using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

/// <summary>
/// Tells a player they've been invited into a house. The client queues a "[Player] has invited you to enter their
/// house." prompt; accepting sends <see cref="ClientHousingPacketEnterRequest"/> for <see cref="HouseGuid"/>, declining
/// sends <see cref="ClientHousingPacketDeclineInvite"/>.
/// </summary>
/// <remarks>
/// From FreeRealms.exe: the reader (0xab8bd0, constructor 0xab3be0, sub-opcode 0x2F) reads four 8-byte values and two
/// names (0xab3540), then one more 8-byte value. The prompt (0xa0b830) uses only the third value, as the house to enter,
/// and the first name, as the inviter. The other fields' meanings are guesses, following the from/to order of
/// <c>PacketChat</c>; the client ignores them.
/// </remarks>
public class HousingPacketInviteNotification : BaseHousingPacket, ISerializablePacket
{
    public new const short OpCode = 47;

    public ulong InviterGuid;
    public ulong InviteeGuid;
    public ulong HouseGuid;

    /// <summary>The character who bought the house.</summary>
    public ulong OwnerGuid;

    public NameData InviterName = new();
    public NameData InviteeName = new();

    public ulong Unknown;

    public HousingPacketInviteNotification() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(InviterGuid);
        writer.Write(InviteeGuid);
        writer.Write(HouseGuid);
        writer.Write(OwnerGuid);

        InviterName.Serialize(writer);
        InviteeName.Serialize(writer);

        writer.Write(Unknown);

        return writer.Buffer;
    }
}
