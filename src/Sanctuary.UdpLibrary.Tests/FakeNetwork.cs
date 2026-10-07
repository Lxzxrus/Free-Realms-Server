using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;

using Microsoft.Extensions.DependencyInjection;

using Sanctuary.UdpLibrary.Abstractions;
using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;
using Sanctuary.UdpLibrary.Internal;
using Sanctuary.UdpLibrary.Statistics;

namespace Sanctuary.UdpLibrary.Tests;

/// <summary>
/// An in-memory network for UdpManagers: datagrams go straight to the driver bound to their destination, the clock only
/// moves when a test moves it, and a test can inject datagrams from any address, including ones nobody owns.
/// </summary>
internal sealed class FakeNetwork
{
    private readonly Dictionary<IPEndPoint, FakeUdpDriver> _bound = new();
    private int _nextEphemeralPort = 40000;

    public long Now = 1_000_000;

    public List<(IPEndPoint From, IPEndPoint To, byte[] Data)> Sent { get; } = new();

    public FakeUdpDriver CreateDriver(string ipAddress) => new(this, IPAddress.Parse(ipAddress));

    internal IPEndPoint Bind(FakeUdpDriver driver, IPAddress address, int port)
    {
        var endPoint = new IPEndPoint(address, port == 0 ? _nextEphemeralPort++ : port);

        _bound.Add(endPoint, driver);

        return endPoint;
    }

    internal void Unbind(IPEndPoint endPoint) => _bound.Remove(endPoint);

    public void Deliver(IPEndPoint from, IPEndPoint to, byte[] data)
    {
        Sent.Add((from, to, data));

        if (_bound.TryGetValue(to, out var driver))
            driver.Inbox.Enqueue((from, data));
    }

    /// <summary>
    /// Gives every manager time, then moves the clock on by <paramref name="stepMs"/>, <paramref name="steps"/> times.
    /// </summary>
    public void Pump(int steps, params IGiveTime[] managers) => Pump(steps, 10, managers);

    public void Pump(int steps, int stepMs, params IGiveTime[] managers)
    {
        for (var i = 0; i < steps; i++)
        {
            foreach (var manager in managers)
                manager.GiveTime();

            Now += stepMs;
        }
    }
}

internal interface IGiveTime
{
    bool GiveTime(int maxPollingTime = 500, bool giveConnectionsTime = true);
}

internal sealed class FakeUdpDriver(FakeNetwork network, IPAddress address) : IUdpDriver
{
    public Queue<(IPEndPoint From, byte[] Data)> Inbox { get; } = new();

    public IPEndPoint? EndPoint { get; private set; }

    public bool SocketOpen(int port, int incomingBufferSize, int outgoingBufferSize, string? bindIpAddress)
    {
        EndPoint = network.Bind(this, address, port);
        return true;
    }

    public void SocketClose()
    {
        if (EndPoint is not null)
            network.Unbind(EndPoint);
    }

    public int SocketReceive(Span<byte> buffer, SocketAddress socketAddress)
    {
        if (!Inbox.TryDequeue(out var packet))
            return -1;

        packet.From.Serialize().Buffer.Span.CopyTo(socketAddress.Buffer.Span);
        packet.Data.CopyTo(buffer);

        return packet.Data.Length;
    }

    public bool SocketSend(ReadOnlySpan<byte> data, SocketAddress socketAddress)
    {
        var to = (IPEndPoint)new IPEndPoint(IPAddress.Any, 0).Create(socketAddress);

        network.Deliver(EndPoint!, to, data.ToArray());

        return true;
    }

    public void SocketSendPortAlive(ReadOnlySpan<byte> data, SocketAddress socketAddress) => SocketSend(data, socketAddress);

    public bool SocketGetLocalIp(out IPAddress ipAddress)
    {
        ipAddress = address;
        return true;
    }

    public int SocketGetLocalPort() => EndPoint?.Port ?? 0;

    public bool GetHostByName(out IPAddress ipAddress, string hostName) => IPAddress.TryParse(hostName, out ipAddress!);

    public long Clock() => network.Now;

    public void Sleep(int lingerDelay)
    {
    }
}

/// <summary>
/// What a test wants the server's connections to do with the application packets they receive.
/// </summary>
internal sealed class FakeBehaviour
{
    /// <summary>A packet starting with this byte throws in the handler.</summary>
    public const byte Throw = 0xEE;

    /// <summary>Catch the handler's exception and report it, as the Gateway and Login servers do in Release.</summary>
    public bool ReportFaults;

    public bool ThrowOnTerminated;
}

internal sealed class FakeManager : UdpManager<FakeConnection>, IGiveTime
{
    public FakeBehaviour Behaviour { get; } = new();

    public List<FakeConnection> Accepted { get; } = new();

    public FakeUdpDriver Driver { get; }

    private FakeManager(UdpParams udpParams, FakeUdpDriver driver) : base(udpParams, new ServiceCollection().BuildServiceProvider())
    {
        Driver = driver;
    }

    public static FakeManager Create(FakeNetwork network, string ipAddress, UdpParams udpParams)
    {
        var driver = network.CreateDriver(ipAddress);

        udpParams.UdpDriver = driver;

        return new FakeManager(udpParams, driver);
    }

    public static UdpParams ServerParams(int port = 20260) => new()
    {
        CrcBytes = 2,
        NoDataTimeout = 30000,
        MaxConnections = 100,
        KeepAliveDelay = 29000,
        Port = port,
        ProtocolName = "Fake"
    };

    public static UdpParams ClientParams() => new(ManagerRole.ExternalClient)
    {
        ProtocolName = "Fake",
        MaxConnections = 4
    };

    public IPEndPoint EndPoint => Driver.EndPoint!;

    public UdpManagerStatistics Stats
    {
        get
        {
            GetStats(out var stats);
            return stats;
        }
    }

    public override bool OnConnectRequest(UdpConnection udpConnection)
    {
        Accepted.Add((FakeConnection)udpConnection);
        return true;
    }
}

internal sealed class FakeConnection : UdpConnection
{
    private readonly FakeManager _manager;

    public List<byte[]> Received { get; } = new();

    public bool Terminated { get; private set; }

    // client side
    public FakeConnection(FakeManager manager, SocketAddress socketAddress, long timeout) : base(manager, socketAddress, timeout)
    {
        _manager = manager;
    }

    // server side
    public FakeConnection(FakeManager manager, SocketAddress socketAddress, int connectCode) : base(manager, socketAddress, connectCode)
    {
        _manager = manager;
    }

    public override void OnRoutePacket(Span<byte> data)
    {
        if (data.Length > 0 && data[0] == FakeBehaviour.Throw)
        {
            if (!_manager.Behaviour.ReportFaults)
                throw new InvalidOperationException("A handler bug.");

            try
            {
                throw new InvalidOperationException("A handler bug.");
            }
            catch (InvalidOperationException)
            {
                ReportFault();
            }

            return;
        }

        Received.Add(data.ToArray());
    }

    public override void OnTerminated()
    {
        Terminated = true;

        if (_manager.Behaviour.ThrowOnTerminated)
            throw new InvalidOperationException("A bug while saving the player.");
    }

    public int EncryptCode => ConnectionConfig.EncryptCode;

    /// <summary>
    /// Wraps an internal packet the way this (client) connection would put it on the wire: no encryption, two CRC bytes.
    /// </summary>
    public byte[] Craft(params byte[] packet)
    {
        var data = new byte[packet.Length + 2];

        packet.CopyTo(data, 0);

        var crc = UdpMisc.Crc32(packet, packet.Length, ConnectionConfig.EncryptCode);

        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(packet.Length), (ushort)crc);

        return data;
    }
}
