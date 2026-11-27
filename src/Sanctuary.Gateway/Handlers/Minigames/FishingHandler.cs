using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
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
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(FishingHandler));
        _sessionLogger = loggerFactory.CreateLogger(nameof(FishingSession));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
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

    /// <summary>The asset the start screen loads before its Go turns on (see <see cref="Join(GatewayConnection, ClientActivityDefinition, bool)"/>).</summary>
    private const string ReadyAsset = "fishingStatus.gfx";

    /// <summary>What a player without a rod is told when they try a hole.</summary>
    public const string NeedARod = "You need a fishing rod to fish here. Buy one in the Coin Store and equip it, then try again.";

    /// <summary>
    /// The hole's start screen: the invite, "launched", and the minigame's info. The client shows the start screen,
    /// and its Go button sends a start game (39/5) with the activity id as the state id. A player with no rod equipped
    /// can't go in (<see cref="TryStart"/>): the client can't cast without one, so fishing would leave them stuck.
    /// </summary>
    /// <remarks>
    /// From the Games menu the start screen goes out even without a rod: the client hides its whole HUD as it asks to
    /// join, and closing a minigame's start screen is what gives it back. Answering with nothing but a chat line left
    /// the player with no UI at all (seen in a local run on 2026-11-25). Without a rod the screen leaves out
    /// <see cref="ReadyAsset"/>, so its Go stays on "Loading..." and the player can only close it, and the chat line
    /// says why.
    /// </remarks>
    public static void Join(GatewayConnection connection, ClientActivityDefinition activity) =>
        Join(connection, activity, fromMap: false);

    private static void Join(GatewayConnection connection, ClientActivityDefinition activity, bool fromMap)
    {
        var player = connection.Player;

        // Already fishing there: nothing to start.
        if (player.FishingSession?.ActivityId == activity.Id)
            return;

        var hasRod = EquippedRodId(player) != 0;

        // From the map the player only teleported to the water and their HUD is untouched: a chat line is enough.
        if (fromMap && !hasRod)
        {
            player.PendingFishingActivityId = 0;
            ChatHelper.SendSystemMessage(player, NeedARod);
            _logger.LogDebug("Fishing: {player} has no rod, so hole {activityId} wasn't offered.", player.Guid, activity.Id);
            return;
        }

        player.PendingFishingActivityId = activity.Id;

        _logger.LogDebug("Fishing: {player} opened the start screen of hole {activityId} (rod: {hasRod}).", player.Guid, activity.Id, hasRod);

        if (!hasRod)
            ChatHelper.SendSystemMessage(player, NeedARod);

        var miniGameInfo = new MiniGameInfo
        {
            NameId = activity.DisplayNameId,
            IconId = activity.ImageSetId,
            DescriptionId = activity.DisplayDescriptionId,
            Difficulty = activity.Difficulty,
            ProfileType = FishingMiniGameType,
            Type = FishingMiniGameType,
            PreselectedGameId = activity.Id,
            ShowActionBar = true,
            // The start screen: the client shows it and waits for Go (MiniGameInfo offset 0x267, read by the client's
            // PlayerStartMiniGame, 0x9BEB70). Off, as edenfps sent it, the client starts the game by itself the moment
            // the info arrives, so the map's teleport to a hole's water threw the player straight into the hole.
            Unknown11 = true,
            // An asset the client loads before it turns the start screen's Go on: PlayerStartMiniGame only waits for
            // a load (and so ever becomes ready) when this name isn't empty, and Go stays on "Loading..." otherwise.
            // A Flash minigame sends its game here; fishing has none, so it names a small fishing movie that is in
            // the client's own packs.
            Unknown13 = hasRod ? ReadyAsset : null
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

    /// <summary>How long a hole's start screen waits for the map teleport's loading screen to drop.</summary>
    private const int OfferAfterLoadingMs = 30_000;

    /// <summary>
    /// A point of interest at a fishing hole (the map lists all six) also offers that hole's start screen, once the
    /// player stands by its water. Matched by name: each hole's point of interest has its activity's name.
    /// </summary>
    /// <remarks>
    /// The teleport shows a loading screen, even within the zone, and a start screen that arrives before it drops is
    /// cancelled by the client (it sends a minigame end at once; seen in a local run on 2026-11-25). So the offer
    /// waits for the client's report that the loading screen was seen (<see cref="OnLoadingScreenDropped"/>), for at
    /// most <see cref="OfferAfterLoadingMs"/>.
    /// </remarks>
    public static void OfferHoleAt(GatewayConnection connection, PointOfInterestDefinition pointOfInterest)
    {
        var player = connection.Player;

        player.FishingOfferAfterLoadingActivityId = 0;

        var activity = _resourceManager.ClientActivityDefinitions.Values
            .FirstOrDefault(activity => activity.NameId == pointOfInterest.NameId && IsFishingHole(activity.Id));

        if (activity is null)
            return;

        player.FishingOfferAfterLoadingActivityId = activity.Id;
        player.FishingOfferAfterLoadingUntil = Environment.TickCount64 + OfferAfterLoadingMs;
        player.FishingOfferAfterLoadingZoneEntry = player.ZoneEntry;
    }

    /// <summary>The client's loading screen dropped: the start screen of a hole the map just took the player to.</summary>
    public static void OnLoadingScreenDropped(GatewayConnection connection)
    {
        var player = connection.Player;
        var activityId = player.FishingOfferAfterLoadingActivityId;

        if (activityId == 0)
            return;

        player.FishingOfferAfterLoadingActivityId = 0;

        // A loading screen from a later trip (the player left the zone before this one's came) isn't the map
        // teleport's: offering the hole then would pull them into fishing from wherever they are now.
        if (Environment.TickCount64 > player.FishingOfferAfterLoadingUntil ||
            player.ZoneEntry != player.FishingOfferAfterLoadingZoneEntry ||
            !_resourceManager.ClientActivityDefinitions.TryGetValue(activityId, out var activity))
            return;

        Join(connection, activity, fromMap: true);
    }

    /// <summary>The start screen's Go: into the hole, and into fishing. False if it isn't a hole the player joined.</summary>
    public static bool TryStart(GatewayConnection connection, int stateId, int groupId, int gameId)
    {
        var player = connection.Player;

        if (player.PendingFishingActivityId != stateId || !TryGetHole(stateId, out var hole))
            return false;

        player.PendingFishingActivityId = 0;

        // A rod taken off while the start screen was up (without one its Go never turns on): close the minigame
        // rather than strand them in the hole. The client keeps the three Fish Finder buttons on its action bar
        // afterwards, until something refreshes it; seen in a local run, harmless.
        if (EquippedRodId(player) == 0)
        {
            _logger.LogDebug("Fishing: {player} has no rod, so hole {activityId} wasn't started.", player.Guid, stateId);
            // Started, then left at once: a leave alone, for a minigame the client hasn't seen start, doesn't give
            // back the HUD it hid at Go (the client's leave, 0x9BF190, only quits a started game).
            connection.SendTunneled(new MiniGameGameStartPacket(stateId, groupId, gameId));
            connection.SendTunneled(new MiniGameLeavePacket(stateId));
            ChatHelper.SendSystemMessage(player, NeedARod);
            return true;
        }

        // Echoed as the client sent it, as edenfps did.
        connection.SendTunneled(new MiniGameGameStartPacket(stateId, groupId, gameId));

        // Into the hole's zone, and into fishing with it. A move to another zone that comes too soon after the player's
        // last waits for its turn (ZoneTeleports), like their moves into a house or to a point of interest.
        ZoneTeleports.Request(player, Environment.TickCount64, () => EnterHole(connection, stateId, hole));

        return true;
    }

    private static void EnterHole(GatewayConnection connection, int stateId, FishingZoneDefinition hole)
    {
        var player = connection.Player;

        // Never a zone made here: TeleportToZone moves the player and sends the zoning.
        if (!player.TeleportToZone(hole.Id, null, hole.SpawnPosition, hole.SpawnRotation))
        {
            _logger.LogWarning("Fishing: {player} could not enter hole {activityId} ({zone}).", player.Guid, hole.Id, hole.Name);
            connection.SendTunneled(new MiniGameLeavePacket(stateId));
            return;
        }

        player.FishingSession = new FishingSession(new PlayerFishingHost(player, _resourceManager, _dbContextFactory, _sessionLogger), hole, _sessionLogger, Random.Shared);

        // The client switches to fishing and asks to register (sub-opcode 2), which the loading screen waits for.
        connection.SendTunneled(new PlayerUpdatePacketUpdateCharacterState
        {
            Guid = player.Guid,
            State = PlayerUpdatePacketUpdateCharacterState.FishingState
        });

        _logger.LogInformation("Fishing: {player} entered hole {activityId} ({zone}).", player.Guid, hole.Id, hole.Name);
    }

    /// <summary>The minigame's end: back to where the player stood before fishing.</summary>
    public static bool TryEnd(GatewayConnection connection, int stateId)
    {
        var player = connection.Player;

        if (player.FishingSession is not { } session || session.ActivityId != stateId)
            return false;

        connection.SendTunneled(new MiniGameLeavePacket(stateId));

        // Leaving the hole's zone ends the session and takes the client out of fishing (FishingZone). It is a move to
        // another zone, so one that comes too soon after the last waits for its turn (ZoneTeleports).
        ZoneTeleports.Request(player, Environment.TickCount64, () =>
        {
            if (!player.ReturnToStartingZone())
                _logger.LogWarning("Fishing: {player} could not leave hole {activityId}.", player.Guid, stateId);
        });

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
