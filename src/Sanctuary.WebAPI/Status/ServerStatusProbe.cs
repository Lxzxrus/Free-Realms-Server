using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sanctuary.WebAPI.Options;

namespace Sanctuary.WebAPI.Status;

public sealed record ServerStatus(bool Online, bool Locked, int Players, DateTimeOffset CheckedAt)
{
    public string Status => !Online ? "offline" : Locked ? "locked" : "online";
}

/// <summary>
/// Asks the Login server what the launcher asks (launcher/src/Launcher/Helpers/ServerStatusHelper.cs): online while a
/// Gateway is connected, locked, and players online. One answer is shared for <see cref="WebAPIOptions.StatusCacheDuration"/>,
/// so the public status page can't be used to flood the Login server.
/// </summary>
public sealed class ServerStatusProbe
{
    // From UdpLibrary.UdpPacketType.ServerStatus. The request is padded to the reply's size: Login never sends a reply
    // larger than its request.
    private const byte ServerStatusPacketType = 32;
    private const int ReplySize = 6;

    private readonly ILogger<ServerStatusProbe> _logger;
    private readonly WebAPIOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ServerStatus? _last;

    public ServerStatusProbe(ILogger<ServerStatusProbe> logger, IOptions<WebAPIOptions> options, TimeProvider timeProvider)
    {
        _logger = logger;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<ServerStatus> GetAsync(CancellationToken cancellationToken)
    {
        if (IsFresh(_last))
            return _last!;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (IsFresh(_last))
                return _last!;

            _last = await AskAsync(cancellationToken);

            return _last;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool IsFresh(ServerStatus? status)
    {
        return status is not null && _timeProvider.GetUtcNow() - status.CheckedAt < _options.StatusCacheDuration;
    }

    private async Task<ServerStatus> AskAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        try
        {
            var endPoint = await ResolveAsync(_options.StatusLoginServer, cancellationToken);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.StatusTimeout);

            using var udpClient = new UdpClient(endPoint.AddressFamily);
            udpClient.Connect(endPoint);

            var request = new byte[ReplySize];
            request[1] = ServerStatusPacketType;

            await udpClient.SendAsync(request, timeout.Token);

            var reply = await udpClient.ReceiveAsync(timeout.Token);

            if (reply.Buffer.Length < ReplySize)
                return new ServerStatus(false, false, 0, now);

            var players = Math.Max(0, BinaryPrimitives.ReadInt32LittleEndian(reply.Buffer.AsSpan(2)));

            return new ServerStatus(reply.Buffer[0] != 0, reply.Buffer[1] != 0, players, now);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("No status from the Login server at {Address}: {Message}", _options.StatusLoginServer, ex.Message);

            return new ServerStatus(false, false, 0, now);
        }
    }

    private static async Task<IPEndPoint> ResolveAsync(string address, CancellationToken cancellationToken)
    {
        var separator = address.LastIndexOf(':');

        if (separator <= 0 || !int.TryParse(address.AsSpan(separator + 1), out var port))
            throw new SocketException((int)SocketError.AddressNotAvailable);

        var host = address[..separator];

        if (IPAddress.TryParse(host, out var ip))
            return new IPEndPoint(ip, port);

        var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, cancellationToken);

        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        return new IPEndPoint(addresses[0], port);
    }
}
