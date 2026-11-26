using System;

using Sanctuary.Core.IO;

namespace Sanctuary.Packet;

public class LoginRequest : IDeserializable<LoginRequest>
{
    public const byte OpCode = 1;

    /// <summary>
    /// The longest Session that parses. A real one is 32 characters (LoginSession); anything much longer is refused
    /// as a bad packet, before the handler sees it.
    /// </summary>
    public const int MaxSessionLength = 64;

    public string? Session;
    public string? Fingerprint;

    public static bool TryDeserialize(ReadOnlySpan<byte> data, out LoginRequest value)
    {
        value = new LoginRequest();

        var reader = new PacketReader(data);

        if (!reader.TryRead(out byte opCode) || opCode != OpCode)
            return false;

        if (!reader.TryRead(out value.Session) || value.Session.Length > MaxSessionLength)
            return false;

        // The client's own text, never used or logged. No limit beyond the packet's: its real length isn't known.
        if (!reader.TryRead(out value.Fingerprint))
            return false;

        return reader.RemainingLength == 0;
    }

    /// <summary>Lengths only: the session is a credential, and both strings are the client's.</summary>
    public override string ToString()
    {
        return $"{nameof(Session)}: ({Session?.Length ?? 0} chars), " +
               $"{nameof(Fingerprint)}: ({Fingerprint?.Length ?? 0} chars)";
    }
}
