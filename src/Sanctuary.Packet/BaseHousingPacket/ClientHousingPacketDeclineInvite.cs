using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// "Decline" on a house invite (<see cref="HousingPacketInviteNotification"/>). "Accept" sends
/// <see cref="ClientHousingPacketEnterRequest"/> for the same house instead.
/// </summary>
/// <remarks>
/// From FreeRealms.exe: <c>GaqItemHouseInvite</c>'s decline (0xa09550) calls 0xac4e00, which builds the packet
/// (constructor 0xaa7b10, sub-opcode 0x10) with the invite's house guid and writes it at 0xac1310.
/// </remarks>
public class ClientHousingPacketDeclineInvite : BaseHousingPacket, IDeserializable<ClientHousingPacketDeclineInvite>
{
    public new const short OpCode = 16;

    public ulong HouseGuid;

    public ClientHousingPacketDeclineInvite() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ClientHousingPacketDeclineInvite value)
    {
        value = new ClientHousingPacketDeclineInvite();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.HouseGuid))
            return false;

        return reader.RemainingLength == 0;
    }
}
