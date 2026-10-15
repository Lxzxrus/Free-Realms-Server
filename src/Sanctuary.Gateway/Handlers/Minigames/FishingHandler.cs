using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Game;
using Sanctuary.Game.Entities;
using Sanctuary.Game.Helpers;
using Sanctuary.Game.Minigames.Fishing;
using Sanctuary.Game.Resources.Definitions.Zones;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

/// <summary>
/// Fishing, from edenfps' fishing minigame (upstream PR #31). A hole is an activity in the Games menu (560 to 565) and
/// a zone of the same id: joining shows its start screen, starting takes the player into the hole's scene and puts
/// their client into fishing, and the fishing packets (opcode 138) drive a <see cref="FishingSession"/> from there.
/// </summary>
[PacketHandler]
public static class FishingHandler
{
    /// <summary>Minigame type and profile type 21: fishing, the Fisherman's.</summary>
    private const int FishingMiniGameType = 21;

    private static ILogger _logger = null!;
    private static ILogger _sessionLogger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IRewardManager _rewardManager = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(FishingHandler));
        _sessionLogger = loggerFactory.CreateLogger(nameof(FishingSession));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _rewardManager = serviceProvider.GetRequiredService<IRewardManager>();
    }

    /// <summary>Whether the activity is a fishing hole: there is a fishing zone with its id.</summary>
    public static bool IsFishingHole(int activityId) => TryGetHole(activityId, out _);

    private static bool TryGetHole(int activityId, out FishingZoneDefinition hole)
    {
        if (_resourceManager.Zones.TryGetValue(activityId, out var zone) && zone is FishingZoneDefinition fishingZone)
        {
            hole = fishingZone;
            return true;
        }

        hole = null!;
        return false;
    }

    /// <summary>
    /// The hole's start screen, as edenfps sent it: the invite, "launched", and the minigame's info. The client then
    /// shows the start screen, and its Go button sends a start game (39/5) with the activity id as the state id.
    /// </summary>
    public static void Join(GatewayConnection connection, ClientActivityDefinition activity)
    {
        var player = connection.Player;

        // Already fishing there: nothing to start.
        if (player.FishingSession?.ActivityId == activity.Id)
            return;

        player.PendingFishingActivityId = activity.Id;

        _logger.LogDebug("Fishing: {player} opened the start screen of hole {activityId}.", player.Guid, activity.Id);

        var miniGameInfo = new MiniGameInfo
        {
            NameId = activity.DisplayNameId,
            IconId = activity.ImageSetId,
            DescriptionId = activity.DisplayDescriptionId,
            Difficulty = activity.Difficulty,
            ProfileType = FishingMiniGameType,
            Type = FishingMiniGameType,
            PreselectedGameId = activity.Id,
            ShowActionBar = true
        };

        var miniGameGroupInfo = new MiniGameGroupInfo
        {
            Id = 10,
            NameId = activity.DisplayNameId,
            DescriptionId = activity.DisplayDescriptionId,
            IconId = activity.ImageSetId
        };

        using var writer = new PacketWriter();

        miniGameInfo.Serialize(writer);

        writer.Write(0);
        writer.Write(0);

        writer.Write(miniGameGroupInfo.Serialize());

        connection.SendTunneled(new ClientActivityLaunchPacketInviteDetails(activity.Id, 0)
        {
            Guid = player.Guid,
            Inviter = "Test",
            Members =
            {
                new()
                {
                    Id = 1,
                    Guid = player.Guid,
                    InviteStatus = 2,
                    IsFoundingMember = true
                }
            },
            Request =
            {
                RequestorGuid = player.Guid,
                SysHashkey = JenkinsHelper.OneAtATimeHash("Minigame"),
                ReqId = 69420,
                MinMembers = 1,
                MaxMembers = 1,
                ImageSetId = activity.ImageSetId,
                NameStringId = activity.DisplayNameId,
                DescStringId = activity.DisplayDescriptionId,
                SysSpecificData = writer.Buffer
            }
        });

        var activityLaunched = new ClientActivityLaunchPacketActivityLaunched(activity.Id, 0);
        activityLaunched.Guids.Add(player.Guid);

        connection.SendTunneled(activityLaunched);

        connection.SendTunneled(new MiniGameInfoPacket(activity.Id, -1, -1)
        {
            Info = miniGameInfo
        });
    }

    /// <summary>
    /// A point of interest at a fishing hole (the map lists all six) also offers that hole's start screen, once the
    /// player stands by its water. Matched by name: each hole's point of interest has its activity's name.
    /// </summary>
    public static void OfferHoleAt(GatewayConnection connection, PointOfInterestDefinition pointOfInterest)
    {
        var activity = _resourceManager.ClientActivityDefinitions.Values
            .FirstOrDefault(activity => activity.NameId == pointOfInterest.NameId && IsFishingHole(activity.Id));

        if (activity is not null)
            Join(connection, activity);
    }

    /// <summary>The start screen's Go: into the hole, and into fishing. False if it isn't a hole the player joined.</summary>
    public static bool TryStart(GatewayConnection connection, int stateId, int groupId, int gameId)
    {
        var player = connection.Player;

        if (player.PendingFishingActivityId != stateId || !TryGetHole(stateId, out var hole))
            return false;

        player.PendingFishingActivityId = 0;

        // Echoed as the client sent it, as edenfps did.
        connection.SendTunneled(new MiniGameGameStartPacket(stateId, groupId, gameId));

        // Never a zone made here: TeleportToZone moves the player and sends the zoning.
        if (!player.TeleportToZone(hole.Id, null, hole.SpawnPosition, hole.SpawnRotation))
        {
            _logger.LogWarning("Fishing: {player} could not enter hole {activityId} ({zone}).", player.Guid, hole.Id, hole.Name);
            connection.SendTunneled(new MiniGameLeavePacket(stateId));
            return true;
        }

        player.FishingSession = new FishingSession(new PlayerFishingHost(player, _rewardManager), hole, _sessionLogger, Random.Shared);

        // The client switches to fishing and asks to register (sub-opcode 2), which the loading screen waits for.
        connection.SendTunneled(new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = player.Guid,
            State = PlayerUpdatePacketUpdateCharacterState.FishingState
        });

        _logger.LogInformation("Fishing: {player} entered hole {activityId} ({zone}).", player.Guid, hole.Id, hole.Name);

        return true;
    }

    /// <summary>The minigame's end: back to where the player stood before fishing.</summary>
    public static bool TryEnd(GatewayConnection connection, int stateId)
    {
        var player = connection.Player;

        if (player.FishingSession is not { } session || session.ActivityId != stateId)
            return false;

        connection.SendTunneled(new MiniGameLeavePacket(stateId));

        // Leaving the hole's zone ends the session and takes the client out of fishing (FishingZone).
        if (!player.ReturnToStartingZone())
            _logger.LogWarning("Fishing: {player} could not leave hole {activityId}.", player.Guid, stateId);

        return true;
    }

    /// <summary>The client entered fishing and waits for its configuration; its loading screen stays up until then.</summary>
    public static bool HandleRegisterPlayer(GatewayConnection connection)
    {
        var player = connection.Player;

        if (player.FishingSession is not { } session)
        {
            _logger.LogDebug("Fishing: {player} asked to register while not fishing.", player.Guid);
            return true;
        }

        var castRange = FishingGear.CastRangeFor(EquippedRodId(player));

        connection.SendTunneled(session.RegisterResponse(castRange));

        connection.SendTunneled(new FishingPacketUpdateData
        {
            Guid = player.Guid,
            Position = player.Position
        });

        connection.SendTunneled(session.FishInfoUpdate());

        _logger.LogDebug("Fishing: {player} registered in hole {activityId} with {rod} (casts {min} to {max}).",
            player.Guid, session.ActivityId, castRange.RodName, castRange.Min, castRange.Max);

        return true;
    }

    /// <summary>The casting pose, shown to the caster (it attaches the rod) and to everyone who can see them.</summary>
    public static bool HandleCastAnim(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!FishingPacketCastAnimRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(FishingPacketCastAnimRequest));
            return false;
        }

        var player = connection.Player;

        // The cast distance travels as a float's bits.
        var distance = BitConverter.Int32BitsToSingle(packet.UnknownInt);

        if (player.FishingSession is not { } session || !Positions.IsFinite(packet.Position) || !float.IsFinite(distance) ||
            !session.AllowCastPose(Environment.TickCount64))
            return true;

        // Relayed under the caster's own guid, whatever the client wrote.
        packet.Guid = player.Guid;

        player.SendTunneledToVisible(packet, sendToSelf: true);

        return true;
    }

    public static bool HandleCast(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!FishingPacketCastRequest.TryDeserialize(data, out var packet))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(FishingPacketCastRequest));
            return false;
        }

        // The bobber goes to everyone who can see the caster, so only a real position does.
        if (connection.Player.FishingSession is { } session && Positions.IsFinite(packet.Position))
            session.OnCast(packet.Position, Environment.TickCount64);

        return true;
    }

    public static bool HandleReelIn(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!FishingPacketReelInRequest.TryDeserialize(data, out _))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(FishingPacketReelInRequest));
            return false;
        }

        connection.Player.FishingSession?.OnReel(Environment.TickCount64);

        return true;
    }

    /// <summary>Answered as edenfps did; what the client uses it for is unknown.</summary>
    public static bool HandleSpecial(GatewayConnection connection, ReadOnlySpan<byte> data)
    {
        if (!FishingPacketSpecialRequest.TryDeserialize(data, out _))
        {
            _logger.LogError("Failed to deserialize {packet}.", nameof(FishingPacketSpecialRequest));
            return false;
        }

        if (connection.Player.FishingSession is null)
            return true;

        connection.SendTunneled(new FishingPacketSpecialResponse
        {
            Guid = connection.Player.Guid,
            Unknown = 0,
            Flag = true
        });

        return true;
    }

    /// <summary>
    /// The rod the player has equipped, 0 if none: the active profile's, else any profile's (the Fisherman's).
    /// </summary>
    private static int EquippedRodId(Player player)
    {
        var profiles = player.Profiles.OrderByDescending(profile => profile.Id == player.ActiveProfileId);

        foreach (var profile in profiles)
        {
            if (!profile.Items.TryGetValue(FishingGear.RodSlot, out var profileItem))
                continue;

            var item = player.Items.FirstOrDefault(item => item.Id == profileItem.Id);

            if (item is not null && FishingGear.IsRod(item.Definition))
                return item.Definition;
        }

        return 0;
    }
}
