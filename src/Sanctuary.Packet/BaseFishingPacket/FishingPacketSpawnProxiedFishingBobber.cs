using System.Numerics;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>The bobber lands. Must come before <see cref="FishingPacketSpawnFishRun"/>, which uses it.</summary>
public class FishingPacketSpawnProxiedFishingBobber : BaseFishingPacket, ISerializablePacket
{
    public new const short OpCode = 8;

    public ulong Guid;

    /// <summary>The bobber's model. With 0 the client creates no bobber, then crashes on the fish run.</summary>
    public int Unknown;

    public Vector4 Position;

    /// <summary>
    /// A forward direction, not a quaternion: the client builds the bobber's matrix from it, so (0,0,0,1) collapses the
    /// bobber to nothing.
    /// </summary>
    public Vector4 Rotation;

    public FishingPacketSpawnProxiedFishingBobber() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(Unknown);
        writer.Write(Position);
        writer.Write(Rotation);

        return writer.Buffer;
    }
}
