using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

public class FishingZoneConfig : ISerializableType
{
    /// <summary>The fishing scene's name.</summary>
    public string? Unknown;

    public int Unknown2;

    /// <summary>X of the underwater fish arena: the scene's Underwater_Bed X (the client fixes Y and Z).</summary>
    public float Unknown3;

    public int Unknown4;
    public int Unknown5;

    /// <summary>The water surface's height, where the bobber floats.</summary>
    public float Unknown6;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Unknown);

        writer.Write(Unknown2);
        writer.Write(Unknown3);
        writer.Write(Unknown4);
        writer.Write(Unknown5);
        writer.Write(Unknown6);
    }
}
