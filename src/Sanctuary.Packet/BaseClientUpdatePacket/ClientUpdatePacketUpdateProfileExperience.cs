using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// A job's level and stars after a change: the profile id, then the same fields, in the same order, as the
/// rank block of <c>ClientPcProfile</c>. Layout from upstream OSFR PR #120 (ImAlko), who saw the client's level bar
/// move with it.
/// </summary>
public class ClientUpdatePacketUpdateProfileExperience : BaseClientUpdatePacket, ISerializablePacket
{
    public new const short OpCode = 14;

    public int ProfileId;
    public int Rank;
    public int RankPercent;
    public int StarsAvailable;
    public int StarsEarned;
    public int Unknown6;

    public ClientUpdatePacketUpdateProfileExperience() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(ProfileId);
        writer.Write(Rank);
        writer.Write(RankPercent);
        writer.Write(StarsAvailable);
        writer.Write(StarsEarned);
        writer.Write(Unknown6);

        return writer.Buffer;
    }
}
