using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// The owner names the house from the title bar: <c>House.SetName(part1, part2, part3)</c> from the name picker, or
/// <c>House.SetNameCustom(text)</c>. Both send the name as text.
/// </summary>
/// <remarks>
/// From FreeRealms.exe: <c>SetName</c> (0xac4e90) looks the three parts up in the client's house name list and joins
/// them into the packet's string; <c>SetNameCustom</c> (0xac51b0) copies its text. Both build the packet with
/// constructor 0xaad540 (sub-opcode 0x11) and write it at 0xac1430 (writer 0xabcf40): the string, then one byte the
/// constructor never sets, so its value means nothing. Some captures may lack it, so it's optional here.
/// </remarks>
public class ClientHousingPacketRequestName : BaseHousingPacket, IDeserializable<ClientHousingPacketRequestName>
{
    public new const short OpCode = 17;

    public string Name = string.Empty;

    public ClientHousingPacketRequestName() : base(OpCode)
    {
    }

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out ClientHousingPacketRequestName value)
    {
        value = new ClientHousingPacketRequestName();

        var reader = new PacketReader(data);

        if (!value.TryRead(ref reader))
            return false;

        if (!reader.TryRead(out value.Name))
            return false;

        if (reader.RemainingLength == 1 && !reader.TryRead(out byte _))
            return false;

        return reader.RemainingLength == 0;
    }
}
