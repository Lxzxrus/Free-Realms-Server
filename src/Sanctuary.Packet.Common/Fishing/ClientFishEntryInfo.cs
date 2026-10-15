using Sanctuary.Core.IO;

namespace Sanctuary.Packet.Common;

/// <summary>One row of the Fish Finder. The client drops rows with a <see cref="Type"/> it has already listed.</summary>
public class ClientFishEntryInfo : ISerializableType
{
    public int Type;

    /// <summary>The fish's name, a string id.</summary>
    public int NameId;

    public int IconId;

    /// <summary>The client skips rows with this set.</summary>
    public bool Unknown4;

    public bool FishSpecial;

    public int FishLureRequirement;

    public string? Unknown7;

    public int Unknown8;

    /// <summary>False greys the fish out in the finder.</summary>
    public bool FishCatchable;

    public int Unknown10;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(Type);
        writer.Write(NameId);
        writer.Write(IconId);

        writer.Write(Unknown4);
        writer.Write(FishSpecial);

        writer.Write(FishLureRequirement);

        writer.Write(Unknown7);

        writer.Write(Unknown8);

        writer.Write(FishCatchable);

        writer.Write(Unknown10);
    }
}
