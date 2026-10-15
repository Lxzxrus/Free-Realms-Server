using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// How a cast ends. Display only: the client grants nothing, so the item is granted separately. The comments give
/// each field's offset in the client's struct and what it does there.
/// </summary>
public class FishingPacketFishingResult : BaseFishingPacket, ISerializablePacket
{
    public new const short OpCode = 14;

    /// <summary>Nothing caught: "Nothing Caught".</summary>
    public const int ResultNothingCaught = 0;

    /// <summary>The catch is pulled up to the rod.</summary>
    public const int ResultReelUp = 4;

    /// <summary>The catch is held up and its banner shown; the camera then comes back by itself.</summary>
    public const int ResultScoredCatch = 5;

    /// <summary>@16, the fishing player.</summary>
    public ulong Guid;

    /// <summary>@24.</summary>
    public int ResultType;

    /// <summary>@28: false for a fish (held up at its size, banner with size and weight), true for an item.</summary>
    public bool Caught;

    /// <summary>@32: the catch's name, a string id. A missing one shows "STRING 0 NOT FOUND".</summary>
    public int FishId;

    /// <summary>@36: the catch's icon.</summary>
    public int Unknown1;

    /// <summary>@40: unused by the banner.</summary>
    public string? FishName;

    /// <summary>@56: weight, shown as "%2.2f".</summary>
    public float Unknown2;

    /// <summary>@60: size, 1 small to 4 extra large.</summary>
    public int Unknown3;

    /// <summary>@64: time to catch, in the scoring list.</summary>
    public float Unknown4;

    /// <summary>@68: difficulty.</summary>
    public int Unknown5;

    /// <summary>@72: rarity.</summary>
    public int Unknown6;

    /// <summary>@76: score.</summary>
    public int Unknown7;

    /// <summary>@80: the model held up. Without one (0) there is no banner.</summary>
    public int Unknown8;

    /// <summary>@84: tint of the model held up.</summary>
    public string? UnknownStr1;

    /// <summary>@100: texture of the model held up.</summary>
    public string? UnknownStr2;

    /// <summary>@116: composite effect played when the catch is held up.</summary>
    public int Unknown9;

    /// <summary>@124.</summary>
    public int Unknown10;

    /// <summary>@128.</summary>
    public bool Unknown11;

    /// <summary>@120: size of the model held up, 1 to 4. Last on the wire.</summary>
    public int Unknown12;

    public FishingPacketFishingResult() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Guid);
        writer.Write(ResultType);
        writer.Write(Caught);
        writer.Write(FishId);
        writer.Write(Unknown1);
        writer.Write(FishName);
        writer.Write(Unknown2);
        writer.Write(Unknown3);
        writer.Write(Unknown4);
        writer.Write(Unknown5);
        writer.Write(Unknown6);
        writer.Write(Unknown7);
        writer.Write(Unknown8);
        writer.Write(UnknownStr1);
        writer.Write(UnknownStr2);
        writer.Write(Unknown9);
        writer.Write(Unknown10);
        writer.Write(Unknown11);
        writer.Write(Unknown12);

        return writer.Buffer;
    }
}
