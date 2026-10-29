using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Database;
using Sanctuary.Game;
using Sanctuary.Game.Minigames;
using Sanctuary.Game.Minigames.MatchThree;
using Sanctuary.Game.Minigames.Mimic;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

/// <summary>
/// Launches the Flash minigames the server knows about, with the client's start screen, and carries their messages.
/// A match-three game (Mining, Farming, Forging, Mail Sorting) is played by <see cref="MatchThreeGame"/> with the levels
/// in <c>Resources/MatchThree.json</c>, and a win pays the game's reward from there; see docs/research/mining.md. A
/// Mimic game (Cooking) is a recipe in <c>Resources/Mimic.json</c>, served by <see cref="MimicHandler"/>; see
/// docs/research/mimic.md.
/// </summary>
/// <remarks>
/// Flow (FreeRealms.exe, docs/research/daily-wheel.md): MiniGameInfo with the start-screen flag on opens a session
/// and the start screen; the player's Go sends StartGame (39/5); the server answers GameStart (39/17) and
/// StartFlashGame (26/12); the Flash sends FRServer_Connect through minigame payloads (39/14). After EDITz's Mining
/// Practice launch on upstream's minigame branch, which used game_hidden.gfx.
/// </remarks>
[PacketHandler]
public static class FlashMinigameHandler
{
    private const string LuaClass = "MiniGameFlash";

    private static int _nextStateId = 20_000;

    private static ILogger _logger = null!;
    private static ILogger _gameLogger = null!;
    private static IResourceManager _resourceManager = null!;
    private static IDbContextFactory<DatabaseContext> _dbContextFactory = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(FlashMinigameHandler));
        _gameLogger = loggerFactory.CreateLogger(nameof(MatchThreeGame));

        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _dbContextFactory = serviceProvider.GetRequiredService<IDbContextFactory<DatabaseContext>>();
    }

    /// <summary>Opens the start screen for a known Flash minigame. False if the activity isn't one.</summary>
    public static bool TryOpen(GatewayConnection connection, Sanctuary.Packet.Common.ClientActivityDefinition activity)
    {
        if (!FlashMinigames.Definitions.TryGetValue(activity.Id, out var definition) && !MimicHandler.TryDefine(activity.Id, out definition))
            return false;

        var player = connection.Player;

        if (player.FlashMinigame is not null || player.WheelSession is not null)
            return true;

        var session = new FlashMinigameSession(Interlocked.Increment(ref _nextStateId), definition);
        player.FlashMinigame = session;

        _logger.LogInformation("Minigame: opening {swf} (activity {activity}) as session {stateId} for {player}.",
            definition.Swf, activity.Id, session.StateId, player.Guid);

        var info = new MiniGameInfo
        {
            NameId = activity.DisplayNameId,
            IconId = activity.ImageSetId,
            DescriptionId = activity.DisplayDescriptionId,
            Difficulty = activity.Difficulty,
            ProfileType = 1,
            Type = definition.Type,
            PreselectedGameId = definition.GameId,
            // The start screen: the client shows it and waits for Go (MiniGameInfo offset 0x267).
            Unknown11 = true,
            Unknown13 = definition.Swf
        };

        var group = new MiniGameGroupInfo
        {
            Id = definition.GameId,
            NameId = activity.DisplayNameId,
            DescriptionId = activity.DisplayDescriptionId,
            IconId = activity.ImageSetId,
            BackgroundSwf = definition.Swf
        };

        using var writer = new PacketWriter();
        info.Serialize(writer);
        writer.Write(1);
        writer.Write(2);
        writer.Write(group.Serialize());

        connection.SendTunneled(new ClientActivityLaunchPacketInviteDetails(activity.Id, 0)
        {
            Guid = player.Guid,
            Inviter = player.Name.FullName,
            Members =
            {
                new() { Id = 1, Guid = player.Guid, InviteStatus = 2, IsFoundingMember = true }
            },
            Request =
            {
                RequestorGuid = player.Guid,
                SysHashkey = JenkinsHelper.OneAtATimeHash("Minigame"),
                ReqId = session.StateId,
                MinMembers = 1,
                MaxMembers = 1,
                ImageSetId = activity.ImageSetId,
                NameStringId = activity.DisplayNameId,
                DescStringId = activity.DisplayDescriptionId,
                SysSpecificData = writer.Buffer
            }
        });

        var launched = new ClientActivityLaunchPacketActivityLaunched(activity.Id, 0);
        launched.Guids.Add(player.Guid);
        connection.SendTunneled(launched);

        connection.SendTunneled(new MiniGameInfoPacket(session.StateId, -1, -1) { Info = info });

        return true;
    }

    /// <summary>The player pressed Go: the game starts and the Flash opens.</summary>
    public static bool TryStart(GatewayConnection connection, int stateId)
    {
        var session = connection.Player.FlashMinigame;
        if (session is null || session.StateId != stateId)
            return false;

        if (session.Started)
            return true;

        session.Started = true;

        if (session.Definition.MatchThreeGame is { } name)
        {
            if (_resourceManager.MatchThree.Find(name) is { } game)
                session.MatchThree = new MatchThreeGame(game, new Output(connection, session.StateId),
                    () => Environment.TickCount64, Random.Shared, _gameLogger);
            else
                _logger.LogError("Minigame: no match-three game \"{name}\" in MatchThree.json; {swf} will have no board.", name, session.Definition.Swf);
        }

        if (session.Definition.MimicGame is not null)
            MimicHandler.Start(connection, session, new Output(connection, session.StateId));

        _logger.LogInformation("Minigame: session {stateId} started; sending {swf}.", stateId, session.Definition.Swf);

        connection.SendTunneled(new MiniGameGameStartPacket(stateId, -1, -1));
        connection.SendTunneled(new CommandPacketStartFlashGame { LuaClass = LuaClass, Swf = session.Definition.Swf });

        return true;
    }

    /// <summary>A message from the game's Flash. False if it isn't for the open Flash minigame.</summary>
    public static bool TryHandle(GatewayConnection connection, MiniGamePayloadPacket packet)
    {
        var session = connection.Player.FlashMinigame;
        if (session is null || session.StateId != packet.StateId)
            return false;

        var message = Encoding.UTF8.GetString(packet.Payload).TrimEnd('\0').Split('\t');
        var name = PacketLog.Text(message[0], 64);

        if (session.ShouldLog(name))
            _logger.LogInformation("Minigame: session {stateId} first sent {message} ({count} fields).", session.StateId, name, message.Length);

        switch (message[0])
        {
            case "FRServer_GameClose":
            case "FRServer_GameQuit":
                Close(connection, session);
                return true;

            // The Flash has finished its game (won, lost or stopped; a win after its end animation) and waits for the
            // server to end it. Without this the game froze with its own mouse pointer stuck on screen.
            case "FRServer_GameOver":
                GameOver(connection, session);
                return true;
        }

        if (session.MatchThree is { } game)
        {
            game.Handle(message);
            Settle(connection, session, game);
        }
        else if (session.Mimic is not null)
            MimicHandler.Handle(connection, session, message);
        else if (message[0] == "FRServer_Connect")
            Send(connection, session, "OnServerReadyMsg");

        return true;
    }

    /// <summary>The match-three clock, from the Gateway's main loop.</summary>
    public static void Tick(GatewayConnection connection, FlashMinigameSession session)
    {
        if (session.MatchThree is not { } game)
            return;

        game.Tick();
        Settle(connection, session, game);
    }

    /// <summary>A game that threw: the player's minigame is closed rather than left half-working.</summary>
    public static void Fail(GatewayConnection connection, FlashMinigameSession session)
    {
        Close(connection, session);
        session.MatchThree = null;
    }

    /// <summary>
    /// Logs each new result, and pays a won game once, as Cooking pays (<see cref="MinigameReward.Pay"/>). The server's
    /// own game decides the win (<see cref="FlashMinigameSession.TryTakeMatchThreeReward"/>), never a message from the
    /// Flash, so a forged, repeated or replayed "game over" pays nothing more.
    /// </summary>
    private static void Settle(GatewayConnection connection, FlashMinigameSession session, MatchThreeGame game)
    {
        var player = connection.Player;

        if (game.Result != session.LoggedResult)
        {
            session.LoggedResult = game.Result;

            _logger.LogInformation("Minigame: {player} {result} {game} on level {level} with {score} points.",
                player.Guid, game.Result, game.Name, game.Level?.Number, game.TotalScore);
        }

        if (!session.TryTakeMatchThreeReward(out var reward))
            return;

        using var dbContext = _dbContextFactory.CreateDbContext();

        var paid = reward.Pay(_resourceManager, dbContext, _logger, player);

        _logger.LogInformation("Minigame: {player} won {game}: paid {coins} coins and {stars} stars on job {profileId}.",
            player.Guid, game.Name, paid.Coins ? reward.Coins : 0, paid.Stars ? reward.Experience : 0, reward.ProfileId);
    }

    /// <summary>The client ended the session (the minigame frame's close button).</summary>
    public static bool TryEnd(GatewayConnection connection, int stateId)
    {
        var session = connection.Player.FlashMinigame;
        if (session is null || session.StateId != stateId)
            return false;

        Close(connection, session);
        return true;
    }

    private static void Send(GatewayConnection connection, FlashMinigameSession session, string name, params object[] args) =>
        new Output(connection, session.StateId).Send(name, args);

    /// <summary>Messages to the game's Flash: minigame payloads, tab-separated and NUL-terminated.</summary>
    private sealed class Output(GatewayConnection connection, int stateId) : IMatchThreeOutput, IMimicOutput
    {
        public void Send(string name, params object[] args)
        {
            connection.SendTunneled(new MiniGamePayloadPacket
            {
                StateId = stateId,
                Payload = Encoding.UTF8.GetBytes(MatchThreeGame.Format(name, args) + "\0")
            });
        }
    }

    /// <summary>
    /// Ends the game: the result, then the leave. On the leave the client shows its end screen if the game's own data
    /// asks for one (FreeRealms.exe, <c>MiniGame:LeaveMiniGame</c> at 0x9bf190), or quits straight back to the world.
    /// </summary>
    private static void GameOver(GatewayConnection connection, FlashMinigameSession session)
    {
        var over = new MiniGameGameOverPacket(session.StateId);

        if (session.MatchThree is { } game)
        {
            over.Won = game.Result == MatchThreeResult.Won;
            over.Score = (int)Math.Clamp(game.TotalScore, 0, int.MaxValue);
            over.Seconds = (int)game.Summarize().Seconds;
        }
        else if (session.Mimic is { } mimic)
        {
            over.Won = mimic.Result == MimicResult.Won;
            over.Score = (int)Math.Clamp(mimic.Score, 0, int.MaxValue);
            over.Seconds = Math.Max(mimic.ReportedSeconds, 0);
        }

        connection.SendTunneled(over);
        Close(connection, session);
    }

    private static void Close(GatewayConnection connection, FlashMinigameSession session)
    {
        _logger.LogInformation("Minigame: session {stateId} closed.", session.StateId);

        if (session.MatchThree is { } game)
        {
            var summary = game.Summarize();

            _logger.LogInformation("Minigame: {player} closed {game} (game {number}): {result}, level {level} of {levels} reached, " +
                "{won} won, {score} points in {seconds:0} s, {desyncs} desyncs.",
                connection.Player.Guid, summary.Name, summary.GameNumber,
                summary.Result == MatchThreeResult.None ? "left unfinished" : summary.Result.ToString().ToLowerInvariant(),
                summary.Level, summary.Levels, summary.LevelsWon, summary.Score, summary.Seconds, summary.Desyncs);
        }

        connection.Player.FlashMinigame = null;
        connection.SendTunneled(new MiniGameLeavePacket(session.StateId));
    }
}
