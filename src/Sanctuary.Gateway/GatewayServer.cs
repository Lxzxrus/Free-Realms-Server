using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Game;
using Sanctuary.Game.Entities;
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
    /// Evergrove: the connection a player in the game plays on, for code that has only the player, such as an NPC's menu
    /// option that opens a game (<see cref="ActivityLauncher"/>). False once they have left. A snapshot, as the ticks take.
    /// </summary>
    public bool TryGetConnection(Player player, [NotNullWhen(true)] out GatewayConnection? connection)
    {
        connection = ConnectionList.ToArray().FirstOrDefault(candidate => ReferenceEquals(candidate?.Player, player));
        return connection is not null;
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
