using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Quests;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class PacketDialogResponseHandler
{
    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(PacketDialogResponseHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!PacketDialogResponse.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(PacketDialogResponse));
            return false;
        }

        if (QuestDialogue.TryAdvance(connection.Player, packet.ResponseId, out var onFinished))
            return true;

        connection.Player.SendTunneled(new CommandPacketEndDialog());

        // After the dialog closes, so a quest step completed by the conversation (Brenna's quiz) shows its updates,
        // or its turn-in window, on a clear screen.
        onFinished?.Invoke();
        return true;
    }
}
