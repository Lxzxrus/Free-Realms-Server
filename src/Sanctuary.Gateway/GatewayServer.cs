using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Game;
using Sanctuary.Game.Helpers;
using Sanctuary.Packet;
using Sanctuary.UdpLibrary;
using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.Gateway;

public class GatewayServer : UdpManager<GatewayConnection>
{
    private readonly ILogger _logger;
    private readonly IResourceManager _resourceManager;

    public GatewayServer(ILogger<GatewayServer> logger, IResourceManager resourceManager, UdpParams udpParams, IServiceProvider serviceProvider) : base(udpParams, serviceProvider)
    {
        _logger = logger;
        _resourceManager = resourceManager;

        // Run game callbacks after releasing the UDP connection lock. Zone ticks send
        // packets while holding zone locks, so inline callbacks can invert that order.
        EventQueuing = true;
    }

    public override bool OnConnectRequest(UdpConnection udpConnection)
    {
        _logger.LogInformation("{connection} connected.", udpConnection);

        return true;
    }

    /// <summary>The characters of the players in the game, for the Login server after it reconnects.</summary>
    public List<ulong> OnlineCharacterIds()
    {
        return ConnectionList
            .Where(connection => connection.Player is not null)
            .Select(connection => GuidHelper.GetPlayerId(connection.Player.Guid))
            .ToList();
    }

    /// <summary>
    /// The characters in the game with the login session each was announced under, for the Login server after it
    /// reconnects. Login only counts a logout for the session it names.
    /// </summary>
    public List<(ulong Id, ulong Session)> OnlineCharacterSessions()
    {
        return ConnectionList
            .ToArray()
            .Where(connection => connection?.Player is not null)
            .Select(connection => (GuidHelper.GetPlayerId(connection.Player.Guid), connection.LoginSession))
            .ToList();
    }

    /// <summary>How often fishing's bite timers are moved along.</summary>
    public const int FishingTickMs = 50;

    /// <summary>Moves every fishing player's bite along. On the main loop, the thread that handles their packets.</summary>
    public void TickFishing(long now)
    {
        // A snapshot, as GiveTime takes: a trade can disconnect a player from another thread. And no exception may
        // leave here: it would end the main loop, and with it the Gateway.
        try
        {
            foreach (var connection in ConnectionList.ToArray())
            {
                if (connection?.Player?.FishingSession is not { } session)
                    continue;

                try
                {
                    session.Update(now);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Fishing: the session of {player} failed; ending it.", connection.Player.Guid);

                    connection.Player.FishingSession = null;
                    connection.Player.ReturnToStartingZone();
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Fishing: the tick failed.");
        }
    }

    /// <summary>
    /// Makes each move to another zone that came too soon and waited (<see cref="ZoneTeleports"/>) once its turn comes.
    /// Main loop.
    /// </summary>
    public void TickZoneTeleports(long now)
    {
        // A snapshot, and no exception may leave here, as in TickFishing.
        try
        {
            foreach (var connection in ConnectionList.ToArray())
            {
                if (connection?.Player is not { WaitingZoneMove: not null } player)
                    continue;

                // Made on a timer, not in a packet handler, so it takes the guard a packet handler would hold: a trade
                // works on the player on another thread under it, and may disconnect them. Busy, it waits a tick more.
                if (!connection.TryAcquireMutationGuard(TimeSpan.Zero, out var lease))
                    continue;

                using (lease)
                {
                    try
                    {
                        if (connection.Status == Status.Connected)
                            ZoneTeleports.Tick(player, now);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Zone teleports: the move of {player} failed.", player.Guid);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Zone teleports: the tick failed.");
        }
    }

    /// <summary>Runs the clocks of open match-three games (Mining): a level ends when its time is up. Main loop.</summary>
    public void TickMinigames()
    {
        try
        {
            foreach (var connection in ConnectionList.ToArray())
            {
                if (connection?.Player?.FlashMinigame is not { MatchThree: not null } session)
                    continue;

                try
                {
                    Handlers.FlashMinigameHandler.Tick(connection, session);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Minigame: the game of {player} failed; ending it.", connection.Player.Guid);
                    Handlers.FlashMinigameHandler.Fail(connection, session);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Minigame: the tick failed.");
        }
    }

    /// <summary>
    /// Evergrove: carries out the pet requests that came too soon once their turn comes (PetHelper.StartWaitingRequest).
    /// Main loop, the thread that handles their packets.
    /// </summary>
    public void TickPetRequests(long now)
    {
        try
        {
            foreach (var connection in ConnectionList.ToArray())
            {
                if (connection?.Player is not { } player)
                    continue;

                try
                {
                    PetHelper.StartWaitingRequest(player, now);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Pets: a waiting request of {player} failed.", player.Guid);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Pets: the tick failed.");
        }
    }

    public void OnStarted()
    {
        _resourceManager.Zones.CollectionChanged -= Zones_CollectionChanged;
        _resourceManager.Zones.CollectionChanged += Zones_CollectionChanged;
    }

    private void Zones_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
    }

    /// <summary>
    /// Saves every player still online, as the Gateway stops: disposing the UDP manager disconnects them without
    /// OnTerminated, so their logout save would never run. On the main loop, once it has ended.
    /// </summary>
    public void SaveOnlinePlayers()
    {
        var saved = 0;

        try
        {
            foreach (var connection in ConnectionList.ToArray())
            {
                if (connection?.Player is null)
                    continue;

                try
                {
                    if (connection.SavePlayerOnce())
                        saved++;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to save {player} as the Gateway stops.", connection.Player.Guid);
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to save the players online as the Gateway stops.");
        }

        _logger.LogInformation("Saved {count} players still online as the Gateway stops.", saved);
    }

    /// <summary>
    /// Drops the character's sessions if none has received a packet within <paramref name="idleThresholdMilliseconds"/>.
    /// Returns false, and drops nothing, while any of them is still live. A session on a loading screen counts as live.
    /// Otherwise returns true and calls <paramref name="onReleased"/> once every dropped session has saved and logged out
    /// (right away if there was none).
    /// </summary>
    public bool TryReleaseIdleCharacter(ulong characterId, uint idleThresholdMilliseconds, Action onReleased)
    {
        var sessions = ConnectionList
            .ToArray()
            .Where(x => x?.Player is not null && GuidHelper.GetPlayerId(x.Player.Guid) == characterId)
            .ToList();

        // Login asks again every couple of seconds while it waits, so these checks log at Debug.
        foreach (var session in sessions)
        {
            // Already being dropped for an earlier request; login will see it gone on a later ask.
            if (session.OnReleased is not null)
                return false;

            // A player isn't Visible until the client finishes loading, and it doesn't send anything while loading,
            // so silence then isn't a crash.
            if (!session.Player.Visible)
            {
                _logger.LogDebug("Character {id} is still loading on {connection}, treating it as live.", characterId, session);
                return false;
            }

            var idle = session.MillisecondsSinceLastReceive();

            if (idle < idleThresholdMilliseconds)
            {
                _logger.LogDebug("Character {id} is still live on {connection} (last packet {idle} ms ago).", characterId, session, idle);
                return false;
            }
        }

        if (sessions.Count == 0)
        {
            onReleased();
            return true;
        }

        // Answer only after the last dropped session has saved, not when Disconnect is called.
        var remaining = sessions.Count;

        foreach (var session in sessions)
        {
            _logger.LogInformation("Dropping silent {connection} so character {id} can log in again.", session, characterId);

            session.OnReleased = () =>
            {
                if (Interlocked.Decrement(ref remaining) == 0)
                    onReleased();
            };

            session.Disconnect();
        }

        return true;
    }

    public void OnStopping()
    {
        _resourceManager.Zones.CollectionChanged -= Zones_CollectionChanged;

        var packetNotice = new PacketWorldShutdownNotice();

        // Scheduled maintenance and updates.
        packetNotice.ReasonId = 418992;

        var packetTunneled = new PacketTunneledClientPacket();

        packetTunneled.Payload = packetNotice.Serialize();

        var packetData = packetTunneled.Serialize();

        foreach (var connection in ConnectionList)
        {
            connection.Send(UdpChannel.Reliable1, packetData);
        }
    }

    public override void Dispose()
    {
        _resourceManager.Zones.CollectionChanged -= Zones_CollectionChanged;
        base.Dispose();
    }

}
