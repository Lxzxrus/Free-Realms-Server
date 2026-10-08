using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

public class HousingPacketZoneData : BaseHousingPacket, ISerializablePacket
{
    public new const short OpCode = 45;

    /// <summary>
    /// Whether this player may decorate the house. The client shows "Click to Decorate" only when this is set
    /// (it calls <c>Housing:SetIsInInstance(true, CanEdit)</c>, FreeRealms.exe 0xac37a1).
    /// </summary>
    public bool CanEdit;

    private bool Unused = default;

    public int HeadSize;

    public PlayerHousingInstanceInfo InstanceInfo = new();

    public HousingPacketZoneData() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(CanEdit);
        writer.Write(Unused);
        writer.Write(HeadSize);

        InstanceInfo.Serialize(writer);

        return writer.Buffer;
    }
}