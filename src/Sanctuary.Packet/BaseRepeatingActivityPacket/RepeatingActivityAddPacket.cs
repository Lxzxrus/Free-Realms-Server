using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// Adds or updates one repeating activity on the client. The client finds it by <see cref="Name"/>
/// (<c>Ui.GetRepeatingActivityCount("wheel")</c>) and, for the wheel, only lets the player spin while
/// <see cref="Count"/> is above zero. An existing <see cref="Id"/> is updated in place.
/// </summary>
/// <remarks>
/// Layout from FreeRealms.exe: the body reader 0xadafa0 reads the five ints of 0xadaed0, then two strings and an int;
/// the handler 0xadb990 matches on the id and calls 0xada6f0(count, consecutive, seconds, seconds), which sets the
/// count and two times as "now plus seconds". Which of the two times is "next spin" isn't known, so both are sent.
/// </remarks>
public class RepeatingActivityAddPacket : BaseRepeatingActivityPacket, ISerializablePacket
{
    public new const byte OpCode = 1;

    public int Id;
    public int Count;
    public int Consecutive;
    public int SecondsUntilNext;
    public int SecondsUntilNext2;

    public string Name = string.Empty;
    public string Name2 = string.Empty;

    public int Unknown;

    public RepeatingActivityAddPacket() : base(OpCode)
    {
    }

    public byte[] Serialize()
    {
        using var writer = new PacketWriter();

        Write(writer);

        writer.Write(Id);
        writer.Write(Count);
        writer.Write(Consecutive);
        writer.Write(SecondsUntilNext);
        writer.Write(SecondsUntilNext2);

        writer.Write(Name);
        writer.Write(Name2);

        writer.Write(Unknown);

        return writer.Buffer;
    }
}
