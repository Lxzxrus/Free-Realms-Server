using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

/// <summary>
/// The client's bug report window (BaseBugSubmissionPacket 83, sub 1): the report as XML (category, severity, the
/// player's text and a snapshot of where they are, what they wear and their machine). Ported from BubbaJoeX/Sanctuary
/// 982a800 (AGPL-3.0). In the client both senders leave the two numbers 0 and the first string empty, but they are
/// read, not required to be, so a client that differs is still read the same way.
/// </summary>
public class BugSubmissionPacketAddBug : IDeserializable<BugSubmissionPacketAddBug>
{
    public const short OpCode = 83;
    public const short SubOpCode = 1;

    public int Unknown1;
    public string Unknown2 = string.Empty;
    public int Unknown3;

    public string BugXml = string.Empty;

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out BugSubmissionPacketAddBug value)
    {
        value = new BugSubmissionPacketAddBug();

        var reader = new PacketReader(data);

        // Both must match: a wrong opcode or a wrong sub-opcode refuses the packet.
        if (!reader.TryRead(out short opCode) || opCode != OpCode)
            return false;

        if (!reader.TryRead(out short subOpCode) || subOpCode != SubOpCode)
            return false;

        if (!reader.TryRead(out value.Unknown1) || !reader.TryRead(out value.Unknown2) || !reader.TryRead(out value.Unknown3) ||
            !reader.TryRead(out value.BugXml))
            return false;

        return reader.RemainingLength == 0;
    }
}
