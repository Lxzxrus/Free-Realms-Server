using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

/// <summary>A subset of the server's fishing player config (the client's own loader for it is dead code).</summary>
public class FishingPlayerConfig : ISerializableType
{
    public int Unknown;

    /// <summary>Minimum cast distance. The client allows a cast only within [min, max]; 0 and 0 make casting
    /// impossible. The server default is 3.</summary>
    public float Unknown2;

    /// <summary>Maximum cast distance; the server default is 20.</summary>
    public float Unknown3;

    public int Unknown4;
    public int Unknown5;
    public int Unknown6;
    public int Unknown7;
    public int Unknown8;
    public int Unknown9;

    /// <summary>Camera distance (6).</summary>
    public float Unknown10;

    /// <summary>Camera pitch (0.445).</summary>
    public float Unknown11;

    /// <summary>Camera heading (1.85).</summary>
    public float Unknown12;

    /// <summary>Camera target height (0.2).</summary>
    public float Unknown13;

    public int Unknown14;
    public int Unknown15;
    public int Unknown16;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Unknown);
        writer.Write(Unknown2);
        writer.Write(Unknown3);
        writer.Write(Unknown4);
        writer.Write(Unknown5);
        writer.Write(Unknown6);
        writer.Write(Unknown7);
        writer.Write(Unknown8);
        writer.Write(Unknown9);

        writer.Write(Unknown10);
        writer.Write(Unknown11);
        writer.Write(Unknown12);
        writer.Write(Unknown13);

        writer.Write(Unknown14);
        writer.Write(Unknown15);
        writer.Write(Unknown16);
    }
}
