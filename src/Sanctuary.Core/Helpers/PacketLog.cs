using System;

namespace Sanctuary.Core.Helpers;

/// <summary>
/// Packet bytes for a log line. A client may send 64 KiB per packet as often as it likes, and logging every byte of
/// a bad one (twice over, as hex) would let it fill the disk, so a log line carries at most <see cref="MaxBytes"/>.
/// </summary>
public static class PacketLog
{
    public const int MaxBytes = 256;

    public static string Hex(ReadOnlySpan<byte> data)
    {
        if (data.Length <= MaxBytes)
            return Convert.ToHexString(data);

        return $"{Convert.ToHexString(data[..MaxBytes])}... ({data.Length} bytes)";
    }

    /// <summary>
    /// Text from a client for a log line: at most <paramref name="maxLength"/> characters, on one line. Control
    /// characters become spaces, so the text can't start a new line (and pass for a line of its own) or carry a
    /// terminal escape sequence into the console that Docker keeps. Line separators and the characters that reorder
    /// text (bidirectional overrides) are replaced too.
    /// </summary>
    public static string Text(string? text, int maxLength = MaxBytes)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var length = Math.Min(text.Length, maxLength);

        // don't cut a character that takes two UTF-16 units in half
        if (length < text.Length && length > 0 && char.IsHighSurrogate(text[length - 1]))
            length--;

        var safe = string.Create(length, text, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = IsUnsafe(source[i]) ? ' ' : source[i];
        });

        if (length == text.Length)
            return safe;

        return $"{safe}... ({text.Length} chars)";
    }

    private static bool IsUnsafe(char c) =>
        char.IsControl(c)                                  // C0 and C1 controls: CR, LF, ESC, NEL (U+0085) and the rest
        || c is (char)0x2028 or (char)0x2029               // line and paragraph separators
        || c is >= (char)0x202A and <= (char)0x202E        // bidirectional embeddings and overrides
        || c is >= (char)0x2066 and <= (char)0x2069;       // bidirectional isolates
}
