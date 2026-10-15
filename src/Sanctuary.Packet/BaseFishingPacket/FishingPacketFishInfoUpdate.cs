using System.Collections.Generic;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

/// <summary>The Fish Finder's list of fish for the hole.</summary>
public class FishingPacketFishInfoUpdate : BaseFishingPacket, ISerializablePacket
{
    public new const short OpCode = 4;

    public List<ClientFishEntryInfo> ClientFishEntries = [];

    public FishingPacketFishInfoUpdate() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ClientFishEntries);

        return writer.Buffer;
    }
}
