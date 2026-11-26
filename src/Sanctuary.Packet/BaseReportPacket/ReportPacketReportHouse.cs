using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// A visitor's "Report Offensive" on the house side panel. The client echoes the house record the server sent it: the
/// guids are the house's instance guid (<c>PlayerHousingInstanceInfo.InstanceGuid</c>) and its owner's character guid.
/// The names are for display only. Sub-op 3 (BubbaJoeX and the client agree; sub-op 2 is not this packet).
/// </summary>
public class ReportPacketReportHouse : BaseReportPacket, IDeserializable<ReportPacketReportHouse>
{
    public new const short OpCode = 3;

    public ulong HouseGuid;
    public ulong OwnerGuid;

    public string HouseName = string.Empty;
    public string OwnerName = string.Empty;

    public ReportPacketReportHouse() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ReportPacketReportHouse value)
    {
        value = new ReportPacketReportHouse();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader) || !reader.TryRead(out value.HouseGuid) || !reader.TryRead(out value.OwnerGuid) ||
            !reader.TryRead(out value.HouseName) || !reader.TryRead(out value.OwnerName))
            return false;

        return reader.RemainingLength == 0;
    }
}
