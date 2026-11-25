using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class PacketLogin : IDeserializable<PacketLogin>
{
    public const short OpCode = 1;

    /// <summary>
    /// The longest Ticket and Version that parse. A real ticket is 32 characters (a Guid as "N", which Login sends the
    /// client) and the real version 14 ("1.910.1.530630"). Anything much longer is a bad packet, refused before the
    /// handler sees it, so it can't be logged.
    /// </summary>
    public const int MaxTicketLength = 64;

    public const int MaxVersionLength = 64;

    public string? Ticket;
    public ulong Guid;
    public string? Version;
    public string? Unknown;

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out PacketLogin value)
    {
        value = new PacketLogin();

        var reader = new PacketReader(data);

        if (!reader.TryRead(out short opCode) || opCode != OpCode)
            return false;

        if (!reader.TryRead(out value.Ticket) || value.Ticket.Length > MaxTicketLength)
            return false;

        if (!reader.TryRead(out value.Guid))
            return false;

        if (!reader.TryRead(out value.Version) || value.Version.Length > MaxVersionLength)
            return false;

        // The client's own text, never used or logged. No limit beyond the packet's: its real length isn't known.
        if (!reader.TryRead(out value.Unknown))
            return false;

        return reader.RemainingLength == 0;
    }

    /// <summary>No ticket: it is the player's credential for this login (and the key of the connection's cipher).</summary>
    public override string ToString()
    {
        return $"( {nameof(Ticket)}: ({Ticket?.Length ?? 0} chars), {nameof(Guid)}: {Guid}, {nameof(Version)}: \"{Version}\", {nameof(Unknown)}: ({Unknown?.Length ?? 0} chars) )";
    }
}
