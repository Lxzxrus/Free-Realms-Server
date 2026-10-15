using System.Collections.Generic;
using System.Numerics;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// A school of decorative fish. Not sent: the client draws every fish of a school at the same point, a clump, so the
/// underwater fish come from <see cref="FishingPacketSpawnFishRun"/> instead.
/// </summary>
public class FishingPacketSpawnProxiedFishingSchool : BaseFishingPacket, ISerializablePacket
{
    public new const short OpCode = 11;

    public int SchoolId;
    public Vector4 Position;
    public Vector4 Rotation;
    public List<FishingSchoolFishItem> Fish = [];
    public List<int> ModelIds = [];
    public int Unknown1;
    public int Unknown2;

    public FishingPacketSpawnProxiedFishingSchool() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(SchoolId);
        writer.Write(Position);
        writer.Write(Rotation);

        writer.Write(Fish.Count);

        foreach (var fish in Fish)
        {
            writer.Write(fish.ModelId);
            writer.Write(fish.Unknown2);
            writer.Write(fish.Unknown3);
        }

        writer.Write(ModelIds);
        writer.Write(Unknown1);
        writer.Write(Unknown2);

        return writer.Buffer;
    }
}

public class FishingSchoolFishItem
{
    public int ModelId;
    public int Unknown2;
    public int Unknown3;
}
