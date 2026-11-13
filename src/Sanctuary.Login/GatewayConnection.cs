using System;
using System.Collections.Generic;
using System.Net;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Configuration;
using Sanctuary.Core.IO;
using Sanctuary.Login.Handlers;
using Sanctuary.Packet;
using Sanctuary.Packet.Common;
using Sanctuary.UdpLibrary;
using Sanctuary.UdpLibrary.Enumerations;
using Sanctuary.Core.Helpers;
using Sanctuary.Game.Helpers;

namespace Sanctuary.Login;

public class GatewayConnection : LoginServerConnection
{
    private readonly ILogger _logger;
    private readonly GatewayServer _gatewayServer;

    public string ServerAddress { get; set; } = null!;
    public GameServerData ServerData { get; set; } = null!;

    // Online character id -> the gateway session that logged it in (upstream OSFR PR #190).
    private readonly OnlineCharacterSessions _onlineSessions = new();

    public int OnlineCount => _onlineSessions.Count;

    public bool IsOnline(ulong characterId) => _onlineSessions.IsOnline(characterId);

    public void MarkOnline(ulong characterId, ulong session) => _onlineSessions.MarkOnline(characterId, session);

    public void MarkReleased(ulong characterId) => _onlineSessions.MarkReleased(characterId);

    public bool TryLogout(ulong characterId, ulong session) => _onlineSessions.TryLogout(characterId, session);

    /// <summary>
    /// Whether this gateway passed the challenge in <see cref="GatewayLoginRequest"/> (only then is its address set).
    /// Until it has, nothing else it sends is handled.
    /// </summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(ServerAddress);

    public GatewayConnection(ILogger<GatewayConnection> logger, IOptionsMonitor<LoginServerOptions> options, GatewayServer gatewayServer, SocketAddress socketAddress, int connectCode) : base(options, gatewayServer, socketAddress, connectCode)
    {
        _logger = logger;
        _gatewayServer = gatewayServer;
    }

    public override void OnTerminated()
    {
        base.OnTerminated();

        var reason = DisconnectReason == DisconnectReason.OtherSideTerminated
            ? OtherSideDisconnectReason
            : DisconnectReason;

        _logger.LogInformation("{connection} disconnected. {reason}", this, reason);
    }

    public override void OnRoutePacket(Span<byte> data)
    {
        var reader = new PacketReader(data);

        if (!reader.TryRead(out byte opCode))
        {
            _logger.LogError("Failed to read opcode from packet. ( Data: {data} )", PacketLog.Hex(data));
            return;
        }

        // Evergrove: a gateway that hasn't passed the challenge may only send the challenge. Before, any connection to
        // the login-gateway port could mark characters online or offline, or answer a release request.
        if (opCode != GatewayLoginRequest.OpCode && !IsAuthenticated)
        {
            _logger.LogWarning("{connection} sent a packet before passing the gateway challenge. ( OpCode: {opcode} )", this, opCode);
            Disconnect();
            return;
        }

        var handled = opCode switch
        {
            GatewayLoginRequest.OpCode => GatewayLoginRequestHandler.HandlePacket(this, data),
            GatewayCharacterLogin.OpCode => GatewayCharacterLoginHandler.HandlePacket(this, data),
            GatewayCharacterLogout.OpCode => GatewayCharacterLogoutHandler.HandlePacket(this, data),
            GatewayCharacterReleaseReply.OpCode => GatewayCharacterReleaseReplyHandler.HandlePacket(this, data),
            _ => false
        };

#if DEBUG
        if (!handled)
        {
            _logger.LogWarning("{connection} received an unhandled packet. ( OpCode: {opcode}, Data: {data} )", this, opCode, PacketLog.Hex(data));
        }
#endif
    }

    public void Send(ISerializablePacket packet)
    {
        var data = packet.Serialize();

        Send(UdpChannel.Reliable1, data);
    }
}