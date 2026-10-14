using System;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Minigames;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;
using Sanctuary.Core.Helpers;

namespace Sanctuary.Gateway.Handlers;

/// <summary>
/// Spin For The Win, the daily wheel. Opened from the activity portal or the Games menu; one spin per character per
/// UTC day, paid in coins from <c>Resources/DailyWheel.json</c>.
/// </summary>
[PacketHandler]
public static class DailyWheelHandler
{
    public const int ActivityId = 8;

    /// <summary>Minigame type 22, "Wheel" (client MiniGameTypeData.txt); its MiniGameData row is 8.</summary>
    private const int WheelMiniGameType = 22;
    private const int WheelIconId = 20985;

    private static int _nextStateId = 8_000;

    private const string LuaClass = "MiniGameFlash";
    private const string Swf = "game_wheel.gfx";

    private static ILogger _logger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(DailyWheelHandler));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    /// <summary>
    /// Opens a minigame session for the wheel if today's spin is unused; otherwise refreshes the client's spin count.
    /// </summary>
    /// <remarks>
    /// The client forwards the Flash's messages only inside a minigame session (FreeRealms.exe 0x9be6b0), which a
    /// MiniGameInfo creates ("MiniGame:JoinGame", 0x9beb70). With the start-screen flag off the client then asks to
    /// start by itself ("MiniGame:RequestStartGame", 0x9be7d0, sub-op 5), and <see cref="TryStart"/> answers with the
    /// Flash. Starting the Flash without a session (the first version) left a window nothing could close.
    /// </remarks>
    public static void Open(GatewayConnection connection)
    {
        var player = connection.Player;
        var now = DateTimeOffset.UtcNow;

        if (player.WheelSession is not null)
            return;

        if (!DailyWheel.CanSpin(player.LastWheelSpin, now))
        {
            connection.SendTunneled(DailyWheelSession.SpinsPacket(_resourceManager.DailyWheel, player.LastWheelSpin, now));
            return;
        }

        var session = new DailyWheelSession { StateId = System.Threading.Interlocked.Increment(ref _nextStateId) };
        player.WheelSession = session;

        _logger.LogInformation("Daily wheel: opening session {stateId} for {player}.", session.StateId, player.Guid);

        var wheel = _resourceManager.DailyWheel;

        connection.SendTunneled(new MiniGameInfoPacket(session.StateId, session.GroupId, session.GameId)
        {
            Info = new MiniGameInfo
            {
                NameId = wheel.NameId,
                IconId = WheelIconId,
                DescriptionId = wheel.DescriptionId,
                Difficulty = 1,
                Type = WheelMiniGameType,
                PreselectedGameId = ActivityId,
                Unknown13 = Swf
            }
        });
    }

    /// <summary>The client's "start game" for the wheel's session: the game starts and the Flash opens.</summary>
    public static bool TryStart(GatewayConnection connection, int stateId)
    {
        var session = connection.Player.WheelSession;
        if (session is null || session.StateId != stateId)
        {
            _logger.LogInformation("Daily wheel: start for session {stateId} ignored (open session: {open}).", stateId, session?.StateId);
            return false;
        }

        _logger.LogInformation("Daily wheel: session {stateId} started; sending the Flash.", stateId);

        connection.SendTunneled(new MiniGameGameStartPacket(session.StateId, session.GroupId, session.GameId));

        connection.SendTunneled(new CommandPacketStartFlashGame
        {
            LuaClass = LuaClass,
            Swf = Swf
        });

        return true;
    }

    /// <summary>The client ended the wheel's session (its close button).</summary>
    public static bool TryEnd(GatewayConnection connection, int stateId)
    {
        var session = connection.Player.WheelSession;
        if (session is null || session.StateId != stateId)
            return false;

        Close(connection, session);
        return true;
    }

    /// <summary>Handles a message from the wheel's Flash. False if no wheel is open, so other minigames can.</summary>
    public static bool TryHandle(GatewayConnection connection, MiniGamePayloadPacket packet)
    {
        var session = connection.Player.WheelSession;
        if (session is null)
            return false;

        var message = DailyWheelSession.Parse(packet.Payload);

        // Debug, and cut short: the payload is the client's, up to a whole packet, as often as it likes.
        _logger.LogDebug("Daily wheel: message for session {stateId} (open: {open}): {message}",
            packet.StateId, session.StateId, PacketLog.Text(string.Join(" | ", message)));

        if (packet.StateId != session.StateId)
            return false;

        switch (message[0])
        {
            case "FRServer_Connect":
            case "FRServer_ReadyForGameData":
                SendWheel(connection, session);
                break;

            case "OnWheelSpinRequestMsg":
                Spin(connection, session);
                break;

            case "OnWheelStopMsg":
                ShowPrize(connection, session);
                break;

            case "OnChangeWheelRequestMsg":
                connection.SendTunneled(session.Message("OnWheelChangedMsg", _resourceManager.DailyWheel.WheelId));
                break;

            case "FRServer_GameClose":
            case "FRServer_GameQuit":
                Close(connection, session);
                break;
        }

        return true;
    }

    /// <summary>The wheel, its spins and "server ready", once per opening.</summary>
    private static void SendWheel(GatewayConnection connection, DailyWheelSession session)
    {
        if (session.DataSent)
            return;

        session.DataSent = true;

        var wheel = _resourceManager.DailyWheel;

        connection.SendTunneled(session.Message("OnWheelDataMsg", wheel.WheelId, wheel.WheelType, wheel.SlotList(), wheel.NameId, wheel.MessageId));
        connection.SendTunneled(session.Message("OnWheelUpdateMsg", wheel.WheelId, 1, 1, 0));
        connection.SendTunneled(session.Message("OnServerReadyMsg"));
    }

    /// <summary>
    /// Rolls the prize and pays it before the wheel stops, in one save with the spin's date, so closing the game
    /// mid-spin neither loses the prize nor frees a second spin. The coin total shown waits for the prize window.
    /// </summary>
    private static void Spin(GatewayConnection connection, DailyWheelSession session)
    {
        var player = connection.Player;
        var wheel = _resourceManager.DailyWheel;
        var now = DateTimeOffset.UtcNow;

        var slot = wheel.Roll(Random.Shared);

        if (!DailyWheel.CanSpin(player.LastWheelSpin, now) || !session.TryClaimSpin(slot))
        {
            _logger.LogWarning("Player {player} asked for a second daily wheel spin.", player.Guid);
            return;
        }

        using (var dbContext = _dbContextFactory.CreateDbContext())
        {
            var dbCharacter = dbContext.Characters.SingleOrDefault(character => character.Id == player.CharacterId);

            // The database decides: another session of this character may have spun already today.
            if (dbCharacter is null || !DailyWheel.CanSpin(dbCharacter.LastWheelSpin, now))
            {
                session.ReleaseSpin();
                return;
            }

            dbCharacter.LastWheelSpin = now;
            dbCharacter.Coins += wheel.Slots[slot].Coins;

            if (dbContext.SaveChanges() <= 0)
            {
                session.ReleaseSpin();
                return;
            }

            player.LastWheelSpin = now;
            player.Coins = dbCharacter.Coins;
        }

        _logger.LogInformation("Player {player} spun the daily wheel and won {coins} coins.", player.Guid, wheel.Slots[slot].Coins);

        connection.SendTunneled(session.Message("OnSpinInfoMsg", slot));
    }

    /// <summary>The prize window, the new coin total, and no spins left until UTC midnight.</summary>
    private static void ShowPrize(GatewayConnection connection, DailyWheelSession session)
    {
        if (session.PrizeSlot is not int slot || session.PrizeShown)
            return;

        session.PrizeShown = true;

        var player = connection.Player;
        var wheel = _resourceManager.DailyWheel;
        var now = DateTimeOffset.UtcNow;

        connection.SendTunneled(new ClientUpdatePacketCoinCount { Coins = player.Coins });

        connection.SendTunneled(session.Message("OnRewardInfoMsg",
            wheel.PrizeIconId, wheel.PrizeNameId, wheel.Slots[slot].Coins, wheel.PrizeNameId, wheel.MessageId, 0));

        var secondsUntilNext = (int)Math.Ceiling(DailyWheel.UntilNextSpin(player.LastWheelSpin, now).TotalSeconds);
        connection.SendTunneled(session.Message("OnWheelUpdateMsg", wheel.WheelId, 0, 1, secondsUntilNext));
        connection.SendTunneled(DailyWheelSession.SpinsPacket(wheel, player.LastWheelSpin, now));
    }

    private static void Close(GatewayConnection connection, DailyWheelSession session)
    {
        _logger.LogInformation("Daily wheel: session {stateId} closed.", session.StateId);

        // A prize paid but never shown (the wheel closed mid-spin): still update the coin total and the spin count.
        if (session.PrizeSlot is not null && !session.PrizeShown)
        {
            session.PrizeShown = true;
            connection.SendTunneled(new ClientUpdatePacketCoinCount { Coins = connection.Player.Coins });
            connection.SendTunneled(DailyWheelSession.SpinsPacket(_resourceManager.DailyWheel, connection.Player.LastWheelSpin, DateTimeOffset.UtcNow));
        }

        connection.Player.WheelSession = null;
        connection.SendTunneled(new MiniGameLeavePacket(session.StateId));
    }
}
