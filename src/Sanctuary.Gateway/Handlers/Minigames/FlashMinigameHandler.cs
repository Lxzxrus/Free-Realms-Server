using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Game.Minigames;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

/// <summary>
/// Launches the Flash minigames the server knows about, with the client's start screen, and carries their messages.
/// The game logic itself (the match-three board for Mining) comes later; for now the server answers the Flash's
/// connection and logs what it sends, so the launch can be checked in game.
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

    /// <summary>Activity id → game. Mining Practice: activity 1113, MiniGameData row 375, type 8 (match three).</summary>
    public static readonly IReadOnlyDictionary<int, FlashMinigameDefinition> Definitions = new Dictionary<int, FlashMinigameDefinition>
    {
        [1113] = new(ActivityId: 1113, GameId: 375, Type: 8, Swf: "game_mining.gfx")
    };

    private static int _nextStateId = 20_000;

    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(FlashMinigameHandler));
    }

    /// <summary>Opens the start screen for a known Flash minigame. False if the activity isn't one.</summary>
    public static bool TryOpen(GatewayConnection connection, Sanctuary.Packet.Common.ClientActivityDefinition activity)
    {
        if (!Definitions.TryGetValue(activity.Id, out var definition))
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

        session.Started = true;

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
            case "FRServer_Connect":
                // The game's own server would load its level data here, then say it's ready.
                Send(connection, session, "OnServerReadyMsg");
                break;

            case "FRServer_GameClose":
            case "FRServer_GameQuit":
                Close(connection, session);
                break;
        }

        return true;
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

    private static void Send(GatewayConnection connection, FlashMinigameSession session, string name, params object[] args)
    {
        var text = string.Join('\t', [name, .. Array.ConvertAll(args, arg => Convert.ToString(arg, System.Globalization.CultureInfo.InvariantCulture))]);

        connection.SendTunneled(new MiniGamePayloadPacket
        {
            StateId = session.StateId,
            Payload = Encoding.UTF8.GetBytes(text + "\0")
        });
    }

    private static void Close(GatewayConnection connection, FlashMinigameSession session)
    {
        _logger.LogInformation("Minigame: session {stateId} closed.", session.StateId);

        connection.Player.FlashMinigame = null;
        connection.SendTunneled(new MiniGameLeavePacket(session.StateId));
    }
}
