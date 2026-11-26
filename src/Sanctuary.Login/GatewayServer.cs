using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Logging;

using Sanctuary.UdpLibrary;
using Sanctuary.UdpLibrary.Configuration;

namespace Sanctuary.Login;

public class GatewayServer : UdpManager<GatewayConnection>
{
    private readonly ILogger _logger;

    /// <summary>
    /// The gateways that passed the challenge (<see cref="GatewayConnection.IsAuthenticated"/>). Anything else connected
    /// to the LoginGateway port isn't one: it has no server data to list, and players must never be sent to it.
    /// </summary>
    public IEnumerable<GatewayConnection> Gateways => ConnectionList.Where(x => x.IsAuthenticated);

    public GatewayServer(ILogger<GatewayServer> logger, UdpParams udpParams, IServiceProvider serviceProvider) : base(udpParams, serviceProvider)
    {
        _logger = logger;
    }

    public override bool OnConnectRequest(UdpConnection udpConnection)
    {
        _logger.LogInformation("{connection} connected.", udpConnection);

        return true;
    }
}