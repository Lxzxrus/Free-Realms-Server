using System;
using System.Buffers.Binary;
using System.Linq;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.Core.Configuration;
using Sanctuary.UdpLibrary;
using Sanctuary.UdpLibrary.Configuration;

namespace Sanctuary.Login;

public class LoginServer : UdpManager<LoginConnection>
{
    private readonly ILogger _logger;
    private readonly GatewayServer _gatewayServer;

    private LoginServerOptions _options;

    public LoginServer(ILogger<LoginServer> logger, IOptionsMonitor<LoginServerOptions> options, GatewayServer gatewayServer, UdpParams udpParams, IServiceProvider serviceProvider) : base(udpParams, serviceProvider)
    {
        _logger = logger;
        _gatewayServer = gatewayServer;

        _options = options.CurrentValue;
        options.OnChange(o => _options = o);
    }

    public override bool OnConnectRequest(UdpConnection udpConnection)
    {
        _logger.LogInformation("{connection} connected.", udpConnection);

        return true;
    }

    /// <summary>
    /// The launcher's server status ping (launcher/src/Launcher/Helpers/ServerStatusHelper.cs). The UDP library sends this
    /// reply only to a request at least as large, and rate-limits it per address, so the launcher pads its request.
    /// </summary>
    public override int OnServerStatusRequest(Span<byte> reply)
    {
        // 1 - Is Online
        // 1 - Is Locked
        // 4 - Online Players
        reply[0] = Convert.ToByte(_gatewayServer.Gateways.Any());
        reply[1] = Convert.ToByte(_options.IsLocked);

        var onlinePlayers = _gatewayServer.Gateways.Sum(x => x.OnlineCharacters.Count);

        BinaryPrimitives.WriteInt32LittleEndian(reply.Slice(2), onlinePlayers);

        return StatusReplySize;
    }

    public const int StatusReplySize = 6;
}