using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.IO;
using Sanctuary.Game.Quests;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseQuestPacketHandler
{
    private static ILogger _logger = null!;
    private static IQuestManager _questManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseQuestPacketHandler));

        _questManager = serviceProvider.GetRequiredService<IQuestManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out int subOpCode))
        {
            _logger.LogError("Failed to read sub-opcode from BaseQuestPacket. ( Data: {data} )", Convert.ToHexString(reader.Span));
            return false;
        }

        return subOpCode switch
        {
            QuestReplyPacket.SubOpCode => QuestReplyPacketHandler.HandlePacket(connection, reader.Span),
            QuestAbandonedPacket.SubOpCode => QuestAbandonedPacketHandler.HandlePacket(connection, reader.Span),
            QuestEndPacket.SubOpCode + 1 => HandleQuestEndReply(connection),
            _ => HandleUnknownOpCode(subOpCode)
        };
    }

    private const int QuestEndBlockedTextId = 0;

    private static bool HandleQuestEndReply(GatewayConnection connection)
    {
        if (!_questManager.TryHandInPendingQuest(connection.Player))
        {
            connection.Player.SendTunneled(new QuestEndBlockedPacket
            {
                TextId = QuestEndBlockedTextId,
                QuestId = connection.Player.ActiveQuestId
            });
            return true;
        }

        connection.Player.SendTunneled(new CommandPacketQuestDialogComplete());
        return true;
    }

    private static bool HandleUnknownOpCode(int subOpCode)
    {
        _logger.LogWarning("Unknown BaseQuestPacket sub-opcode: {subOpCode}", subOpCode);
        return false;
    }
}
