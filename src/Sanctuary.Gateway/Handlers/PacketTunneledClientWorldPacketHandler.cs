using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;
using Sanctuary.Core.Helpers;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketTunneledClientWorldPacketHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketTunneledClientWorldPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, Span<byte> data)
    {
        if (!PacketTunneledClientWorldPacket.TryDeserialize(data, out var packetTunneledClientWorldPacket))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketTunneledClientWorldPacket));
            return false;
        }

        // Every tunneled packet is about the player. The game client sends none before its login is answered, so
        // one that arrives earlier is from something else, and the handlers would throw on the missing player.
        if (connection.Player is null)
        {
            connection.ReportFault();
            return false;
        }

        var reader = new PacketReader(packetTunneledClientWorldPacket.Payload);

        if (!reader.TryRead(out short opCode))
        {
            _logger.LogError("Failed to read opcode from packet. ( Data: {data} )", PacketLog.Hex(data));
            return false;
        }

        var handled = opCode switch
        {
            BaseCommandPacket.OpCode => BaseCommandPacketHandler.HandlePacket(connection, reader),
            BaseTradePacket.OpCode => BaseTradePacketHandler.HandlePacket(connection, packetTunneledClientWorldPacket.Payload),
            PacketWorldTeleportRequest.OpCode => PacketWorldTeleportRequestHandler.HandlePacket(connection, packetTunneledClientWorldPacket.Payload),
            PacketBaseInGamePurchase.OpCode => PacketBaseInGamePurchaseHandler.HandlePacket(connection, reader),
            AnnouncementDataSendPacket.OpCode => AnnouncementBasePacketHandler.HandlePacket(connection, reader),
            PacketSetLocale.OpCode => PacketSetLocaleHandler.HandlePacket(connection, packetTunneledClientWorldPacket.Payload),
            BaseLobbyGameDefinitionPacket.OpCode => BaseLobbyGameDefinitionPacketHandler.HandlePacket(connection, reader),
            BaseHousingPacket.OpCode => BaseHousingPacketHandler.HandlePacket(connection, reader),
            BaseRatingPacket.OpCode => BaseRatingPacketHandler.HandlePacket(connection, reader),
            BaseGuildPacket.OpCode => BaseGuildPacketHandler.HandlePacket(connection, reader),
            BaseMatchmakingPacket.OpCode => BaseMatchmakingPacketHandler.HandlePacket(connection, reader),
            PacketAddClientPortraitCrc.OpCode => PacketAddClientPortraitCrcHandler.HandlePacket(connection, packetTunneledClientWorldPacket.Payload),
            BaseFotomatPacket.OpCode => BaseFotomatPacketHandler.HandlePacket(connection, reader),
            BaseActivityServicePacket.OpCode => BaseActivityServicePacketHandler.HandlePacket(connection, reader, 1),
            WallOfDataBasePacket.OpCode => WallOfDataBasePacketHandler.HandlePacket(connection, reader),
            PacketClientLuaMetrics.OpCode => PacketClientLuaMetricsHandler.HandlePacket(connection, packetTunneledClientWorldPacket.Payload),
            _ => false
        };

#if DEBUG
        if (!handled)
        {
            reader.Reset();
            System.Diagnostics.Debug.WriteLine(reader.ReadTunneledPacketName(), "TunneledClientWorld");
        }
#endif

        return handled;
    }
}
