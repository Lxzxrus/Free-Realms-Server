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

namespace Sanctuary.Login;

public class GatewayConnection : LoginServerConnection
{
    private readonly ILogger _logger;
    private readonly GatewayServer _gatewayServer;

    public string ServerAddress { get; set; } = null!;
    public GameServerData ServerData { get; set; } = null!;

    public HashSet<ulong> OnlineCharacters { get; set; } = new();

    /// <summary>Whether this connection passed the challenge (GatewayLoginRequestHandler). Until then it is not a gateway.</summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// How many packets other than the challenge a connection may send before passing it. The real Gateway sends its
    /// challenge first, perhaps behind a logout or two it queued while reconnecting; anything sending more isn't it.
    /// </summary>
    public const int MaxPacketsBeforeChallenge = 16;

    // packets ignored because they came before the challenge was passed
    private int _ignoredBeforeChallenge;

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
        // a connection being closed (see below) handles nothing more of the datagram it came in
        if (Status != Status.Connected)
            return;

        var reader = new PacketReader(data);

        if (!reader.TryRead(out byte opCode))
        {
            _logger.LogError("Failed to read opcode from packet. ( Data: {data} )", PacketLog.Hex(data));
            return;
        }

        // Only the challenge may come before the challenge is passed: anyone who reaches this port could otherwise report
        // characters online, which the server status counts (Evergrove). Ignored rather than disconnected: the real
        // Gateway may have queued a logout while it was reconnecting, ahead of its challenge, and must not lose the link.
        if (!IsAuthenticated && opCode != GatewayLoginRequest.OpCode)
        {
            _ignoredBeforeChallenge++;

            // the first is logged; a line for each would let whatever this is fill the log
            if (_ignoredBeforeChallenge == 1)
                _logger.LogWarning("{connection} sent opcode {opcode} before passing the challenge; ignored (any more are counted, not logged).", this, opCode);

            if (_ignoredBeforeChallenge > MaxPacketsBeforeChallenge)
            {
                _logger.LogWarning("{connection} sent {count} packets before passing the challenge. Disconnecting it.", this, _ignoredBeforeChallenge);

                Disconnect();
            }

            return;
        }

        var handled = opCode switch
        {
            GatewayLoginRequest.OpCode => GatewayLoginRequestHandler.HandlePacket(this, data),
            GatewayCharacterLogin.OpCode => GatewayCharacterLoginHandler.HandlePacket(this, data),
            GatewayCharacterLogout.OpCode => GatewayCharacterLogoutHandler.HandlePacket(this, data),
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