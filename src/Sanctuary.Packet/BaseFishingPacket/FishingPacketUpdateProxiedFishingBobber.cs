using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Drives the bite, which is the server's to decide. The flags are exclusive: <see cref="Flag2"/> alone makes the
/// fish interested (it swims to the hook), <see cref="Flag1"/> makes it bite, neither makes it escape.
/// </summary>
public class FishingPacketUpdateProxiedFishingBobber : BaseFishingPacket, ISerializablePacket
{
    public new const short OpCode = 10;

    public ulong Guid;

    /// <summary>Which underwater fish (its id in the fish run).</summary>
    public int Unknown;

    /// <summary>Hooked.</summary>
    public bool Flag1;

    /// <summary>Interested.</summary>
    public bool Flag2;

    public FishingPacketUpdateProxiedFishingBobber() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(Unknown);
        writer.Write(Flag1);
        writer.Write(Flag2);

        return writer.Buffer;
    }
}
