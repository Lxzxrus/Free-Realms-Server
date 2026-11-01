using Sanctuary.Game.Entities;
using Sanctuary.Game.Minigames;
using Sanctuary.Gateway.Handlers;

namespace Sanctuary.Gateway;

/// <summary>
/// Opens a Games menu entry for a player through the Games menu's own code
/// (<see cref="ActivityPacketJoinActivityRequestHandler.TryOpen(GatewayConnection, int)"/>), with all its checks: how the
/// NPC who offers a quest step's game starts it (the quest manager, which can't reach the Gateway itself).
/// </summary>
public sealed class ActivityLauncher(GatewayServer server) : IActivityLauncher
{
    public bool TryLaunch(Player player, int activityId) =>
        server.TryGetConnection(player, out var connection)
        && ActivityPacketJoinActivityRequestHandler.TryOpen(connection, activityId);
}
