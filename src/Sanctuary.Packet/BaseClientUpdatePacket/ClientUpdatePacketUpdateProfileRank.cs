using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// A job reached a new level; the client plays its level-up notice. From upstream OSFR PR #120 (ImAlko).
/// </summary>
public class ClientUpdatePacketUpdateProfileRank : BaseClientUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 18;

    public int ProfileId;
    public int Rank;

    public ClientUpdatePacketUpdateProfileRank() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ProfileId);
        writer.Write(Rank);

        return writer.Buffer;
    }
}
