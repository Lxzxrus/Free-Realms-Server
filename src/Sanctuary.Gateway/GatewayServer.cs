using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Game;
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

    public void OnStarted()
    {
        _resourceManager.Zones.CollectionChanged += Zones_CollectionChanged;
    }

    private void Zones_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
    }

    public void OnStopping()
    {
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
}