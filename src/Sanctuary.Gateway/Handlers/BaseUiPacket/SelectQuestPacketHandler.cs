using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Quests;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class SelectQuestPacketHandler
{
    private const int MinSelectIntervalMs = 250;

    private static ILogger _logger = null!;
    private static IQuestManager _questManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(SelectQuestPacketHandler));

        _questManager = serviceProvider.GetRequiredService<IQuestManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!SelectQuestPacket.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(SelectQuestPacket));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(SelectQuestPacket), packet);

        // Each change of tracked quest is a database write: a few a second is more than any player clicks.
        var player = connection.Player;

        if (Environment.TickCount64 < player.NextSelectQuestAt)
            return true;

        player.NextSelectQuestAt = Environment.TickCount64 + MinSelectIntervalMs;

        _questManager.SetActiveQuest(player, packet.QuestId);
        return true;
    }
}
