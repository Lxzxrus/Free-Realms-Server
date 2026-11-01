using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Game;
using Sanctuary.Game.Minigames;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class ActivityPacketJoinActivityRequestHandler
{
    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(ActivityPacketJoinActivityRequestHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
    }

    public static bool HandlePacket(GatewayConnection connection, ReadOnlySpan<byte> data, int serverType)
    {
        if (!ActivityPacketJoinActivityRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(ActivityPacketJoinActivityRequest));
            return false;
        }

        _logger.LogTrace("Received {name} packet. ( {packet} )", nameof(ActivityPacketJoinActivityRequest), packet);

        if (!_resourceManager.ClientActivityDefinitions.TryGetValue(packet.ActivityId, out var clientActivityDefinition))
            return true;

        if (Open(connection, clientActivityDefinition) != ActivityLaunchResult.NotAGamesMenuGame)
            return true;

        // Free Realms Trading Card Game Lobby
        if (packet.ActivityId == 7)
        {
            var miniGameInfo = new MiniGameInfo()
            {
                NameId = clientActivityDefinition.DisplayNameId,
                IconId = clientActivityDefinition.ImageSetId,
                DescriptionId = clientActivityDefinition.DisplayDescriptionId,
                Difficulty = clientActivityDefinition.Difficulty,
                ProfileType = 0,
                Type = 16, // Trading Card Game
                PreselectedGameId = 7
            };

            var miniGameGroupInfo = new MiniGameGroupInfo()
            {
                Id = 69,
                NameId = clientActivityDefinition.DisplayNameId,
                DescriptionId = clientActivityDefinition.DisplayDescriptionId,
                IconId = clientActivityDefinition.ImageSetId
            };

            using var writer = new PacketWriter();

            miniGameInfo.Serialize(writer);

            writer.Write(0); // Unused
            writer.Write(0); // Unused

            writer.Write(miniGameGroupInfo.Serialize());

            var clientActivityLaunchPacketInviteDetails = new ClientActivityLaunchPacketInviteDetails(packet.ActivityId, 0)
            {
                Guid = connection.Player.Guid,
                Inviter = "Test",
                Members =
                {
                    new()
                    {
                        Id = 1,
                        Guid = connection.Player.Guid,
                        InviteStatus = 2,
                        IsFoundingMember = true
                    }
                },
                Request =
                {
                    RequestorGuid = connection.Player.Guid,
                    SysHashkey = JenkinsHelper.OneAtATimeHash("Minigame"),
                    ReqId = 69420,
                    MinMembers = 1,
                    MaxMembers = 1,
                    ImageSetId = clientActivityDefinition.ImageSetId,
                    NameStringId = clientActivityDefinition.DisplayNameId,
                    DescStringId = clientActivityDefinition.DisplayDescriptionId,
                    SysSpecificData = writer.Buffer
                }
            };

            connection.SendTunneled(clientActivityLaunchPacketInviteDetails);

            var clientActivityLaunchPacketActivityLaunched = new ClientActivityLaunchPacketActivityLaunched(packet.ActivityId, 0);

            clientActivityLaunchPacketActivityLaunched.Guids.Add(connection.Player.Guid);

            connection.SendTunneled(clientActivityLaunchPacketActivityLaunched);

            var miniGameInfoPacket = new MiniGameInfoPacket(packet.ActivityId, -1, -1)
            {
                Info = miniGameInfo
            };

            connection.SendTunneled(miniGameInfoPacket);
        }

        return true;
    }

    /// <summary>
    /// Evergrove: opens one of the games the server serves, as choosing it in the Games menu does: the wheel, a Flash
    /// minigame (match three, Cooking) or a fishing hole, each with its own checks. A Flash minigame is
    /// <see cref="ActivityLaunchResult.AnotherGameOpen"/> while the player has one or the wheel open;
    /// <see cref="ActivityLaunchResult.NotAGamesMenuGame"/> for any other activity. The NPC who offers a quest step's game
    /// and a world object that opens games open them through here too (<see cref="ActivityLauncher"/>).
    /// </summary>
    public static ActivityLaunchResult Open(GatewayConnection connection, Sanctuary.Packet.Common.ClientActivityDefinition activity)
    {
        if (activity.Id == DailyWheelHandler.ActivityId)
        {
            DailyWheelHandler.Open(connection);
            return ActivityLaunchResult.Opened;
        }

        var flashMinigame = FlashMinigameHandler.Open(connection, activity);
        if (flashMinigame != ActivityLaunchResult.NotAGamesMenuGame)
            return flashMinigame;

        if (FishingHandler.IsFishingHole(activity.Id))
        {
            FishingHandler.Join(connection, activity);
            return ActivityLaunchResult.Opened;
        }

        return ActivityLaunchResult.NotAGamesMenuGame;
    }

    /// <inheritdoc cref="Open(GatewayConnection, Sanctuary.Packet.Common.ClientActivityDefinition)"/>
    public static ActivityLaunchResult Open(GatewayConnection connection, int activityId) =>
        _resourceManager.ClientActivityDefinitions.TryGetValue(activityId, out var activity)
            ? Open(connection, activity)
            : ActivityLaunchResult.NotAGamesMenuGame;
}