using System.Collections.Generic;

using Sanctuary.Core.IO;
using Sanctuary.Packet.Common;

namespace Sanctuary.Packet;

/// <summary>
/// The underwater fish, sent to the caster only after the bobber. Its handler uses the bobber without checking it
/// exists, so another player's client, which may not have made it, crashes on it.
/// </summary>
public class FishingPacketSpawnFishRun : BaseFishingPacket, ISerializablePacket
{
    public new const short OpCode = 18;

    public bool Unknown;

    /// <summary>
    /// The model of an actor the client parks at the hook when the first fish isn't catchable. 0 crashes the client.
    /// </summary>
    public int Unknown2;

    public string? Unknown3;

    /// <summary>The fish. The first is the one at the hook.</summary>
    public List<UnderwaterFishSpawnInfo> UnderwaterFishSpawns = [];

    public FishingPacketSpawnFishRun() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Unknown);
        writer.Write(Unknown2);
        writer.Write(Unknown3);
        writer.Write(UnderwaterFishSpawns);

        return writer.Buffer;
    }
}
