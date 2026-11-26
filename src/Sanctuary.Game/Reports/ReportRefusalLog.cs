using System;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;

namespace Sanctuary.Game.Reports;

/// <summary>
/// Logs a report packet the server refused (it does not parse, or has a sub-opcode nobody handles), with its bytes. A
/// real client whose layout differs from what the server reads would otherwise lose every report without a trace, and a
/// hostile client can send these as fast as it likes, so the lines are limited like <c>OnCrcReject</c>'s: the first few,
/// then one a minute with the count held back. The bytes are capped by <see cref="PacketLog.Hex"/>.
/// </summary>
public static class ReportRefusalLog
{
    private static readonly LogRateLimiter Limiter = new(firstFree: 5, TimeSpan.FromMinutes(1));

    public static void Refused(ILogger logger, string what, ReadOnlySpan<byte> data) => Refused(logger, Limiter, what, data);

    public static void Refused(ILogger logger, LogRateLimiter limiter, string what, ReadOnlySpan<byte> data)
    {
        if (!limiter.TryAllow(out var suppressed))
            return;

        logger.LogWarning("Refused a report packet: {What}. ( Data: {Data} ) ({Suppressed} more refused since the last line; these are logged at most once a minute after the first few)",
            what, PacketLog.Hex(data), suppressed);
    }
}
