using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

/// <summary>
/// One underwater fish. <see cref="Unknown8"/> to <see cref="Unknown18"/> are floats: integers there freeze the
/// fish (int 1 read as a float is about 0).
/// </summary>
public class UnderwaterFishSpawnInfo : ISerializableType
{
    /// <summary>The fish's id, which the bite packet names.</summary>
    public int Unknown;

    /// <summary>1670 thin, 1671 medium or 1672 fat.</summary>
    public int ModelId;

    public string? TintAlias;
    public string? TextureAlias;

    /// <summary>Size, 1 to 4.</summary>
    public int Unknown5;

    public int Unknown6;

    /// <summary>
    /// Catchable. A catchable fish is parked at the hook, frozen, and only counts as the first of the run; only
    /// fish that aren't catchable swim, bite and fight.
    /// </summary>
    public bool Unknown7;

    /// <summary>Approach time (above 0.25).</summary>
    public float Unknown8;

    /// <summary>Reel-in speed divisor.</summary>
    public float Unknown9;

    /// <summary>Nibble and flee speed.</summary>
    public float Unknown10;

    public float Unknown11;

    /// <summary>Reel-in speed offset.</summary>
    public float Unknown12;

    /// <summary>Turn speed.</summary>
    public float Unknown13;

    /// <summary>Wander speed.</summary>
    public float Unknown14;

    /// <summary>Wander deceleration.</summary>
    public float Unknown15;

    /// <summary>Approach and wander turn rate.</summary>
    public float Unknown16;

    /// <summary>Shortest pause between wanders.</summary>
    public float Unknown17;

    /// <summary>Longest pause between wanders.</summary>
    public float Unknown18;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Unknown);

        writer.Write(ModelId);

        writer.Write(TintAlias);
        writer.Write(TextureAlias);

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
        writer.Write(Unknown17);

        writer.Write(Unknown18);
    }
}
