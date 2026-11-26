using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Game.Reports;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

/// <summary>
/// Reports of players and houses (the chat log's "Report", /report, the house panel's "Report Offensive"). The client
/// expects nothing back; staff read them with <c>!mod reports</c>. Ported from BubbaJoeX/Sanctuary 982a800 (AGPL-3.0).
/// </summary>
[PacketHandler]
public static class BaseReportPacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseReportPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out short opCode))
        {
            // A hostile client can send this as fast as it likes: the same rate limit as every other refused report packet.
            ReportRefusalLog.Refused(_logger, "no opcode", reader.Span);
            return false;
        }

        return opCode switch
        {
            ReportPacketReportPlayer.OpCode => ReportPacketReportPlayerHandler.HandlePacket(connection, reader.Span),
            ReportPacketReportHouse.OpCode => ReportPacketReportHouseHandler.HandlePacket(connection, reader.Span),
            _ => RefuseUnknown(opCode, reader.Span)
        };
    }

    private static bool RefuseUnknown(short opCode, ReadOnlySpan<byte> data)
    {
        ReportRefusalLog.Refused(_logger, $"unknown sub-opcode {opCode}", data);
        return false;
    }
}
