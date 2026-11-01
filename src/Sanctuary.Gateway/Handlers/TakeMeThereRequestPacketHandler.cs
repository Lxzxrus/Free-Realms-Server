using System;
using System.Numerics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Game.Quests;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class TakeMeThereRequestPacketHandler
{
    private static ILogger _logger = null!;
    private static IQuestManager _questManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(TakeMeThereRequestPacketHandler));
        _questManager = serviceProvider.GetRequiredService<IQuestManager>();
    }

    /// <summary>
    /// Reached through <see cref="PacketClientLuaMetricsHandler"/>, which finds the request in the client's Lua
    /// metrics report. The stock client's button itself is answered by <see cref="ClientPathRequestPacketHandler"/>:
    /// this reply (type 1) only reaches the client's breadcrumb trail, which drops it unless it is waiting for or
    /// showing a path of its own.
    /// </summary>
    public static bool HandlePacket(GatewayConnection connection, TakeMeThereRequestPacket request)
    {
        _logger.LogTrace("Received {packet} {name}.", nameof(TakeMeThereRequestPacket), request.ActionName);

        var player = connection.Player;

        if (!_questManager.TryGetActiveObjectiveTarget(player, out var goalPosition))
            return true;

        // Upstream OSFR's names for the reply's fields (Type was our ResultType, 1; Id our RequestId; Points our Path).
        var reply = new ClientPathReplyPacket { Type = 1, Id = request.RequestId };

        reply.Points.Add(player.Position);

        var pathfinder = player.Zone.Pathfinder;
        if (pathfinder is not null)
        {
            var start = new Vector3(player.Position.X, player.Position.Y, player.Position.Z);

            foreach (var node in pathfinder.FindPath(start, goalPosition))
                reply.Points.Add(new Vector4(node.Position.X, node.Position.Y, node.Position.Z, 1f));
        }

        reply.Points.Add(new Vector4(goalPosition.X, goalPosition.Y, goalPosition.Z, 1f));

        player.SendTunneled(reply);

        return true;
    }
}
