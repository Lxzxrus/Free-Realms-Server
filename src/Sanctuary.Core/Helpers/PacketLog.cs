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

    /// <summary>Text from a client for a log line: at most <paramref name="maxLength"/> characters, on one line.</summary>
    public static string Text(string text, int maxLength = MaxBytes)
    {
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ');

        if (oneLine.Length <= maxLength)
            return oneLine;

        return $"{oneLine[..maxLength]}... ({text.Length} chars)";
    }
}
