using Sanctuary.Game.Entities;
using Sanctuary.Game.Minigames;
using Sanctuary.Gateway.Handlers;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.Gateway;

/// <summary>
/// Opens a Games menu entry for a player through the Games menu's own code
/// (<see cref="ActivityPacketJoinActivityRequestHandler.Open(GatewayConnection, int)"/>), with all its checks: how the
/// NPC who offers a quest step's game starts it (the quest manager, which can't reach the Gateway itself), and a world
/// object its games (<see cref="Sanctuary.Game.Minigames.GameStationMenu"/>). It sends on the player's own connection.
/// </summary>
public sealed class ActivityLauncher : IActivityLauncher
{
    public ActivityLaunchResult Launch(Player player, int activityId) =>
        player.Connection is GatewayConnection { Status: Status.Connected } connection
            ? ActivityPacketJoinActivityRequestHandler.Open(connection, activityId)
            : ActivityLaunchResult.NotConnected;
}
