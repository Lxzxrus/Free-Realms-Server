using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.UdpLibrary.Abstractions;
using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.UdpLibrary.Tests;

[TestClass]
public sealed class QueuedCallbackTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ZoneSend_CanFinishDuringCallback_OnlyWhenQueued(bool queued)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var manager = new CallbackManager(services, queued);
        var connection = manager.CreateConnection();
        var zoneLock = new object();
        using var zoneLocked = new ManualResetEventSlim();
        using var callbackEntered = new ManualResetEventSlim();
        using var sendFinished = new ManualResetEventSlim();
        var sentDuringCallback = false;

        // Reproduce the production lock order: respawn owns the zone lock and sends
        // while the pickup callback needs the same zone. Bound the wait to avoid hanging CI.
        var respawn = new Thread(() =>
        {
            lock (zoneLock)
            {
                zoneLocked.Set();
                if (!callbackEntered.Wait(TimeSpan.FromSeconds(5)))
                    return;

                connection.Send(UdpChannel.Reliable1, new byte[] { 7 });
                sendFinished.Set();
            }
        }) { IsBackground = true };
        connection.PacketAction = _ =>
        {
            callbackEntered.Set();
            sentDuringCallback = sendFinished.Wait(TimeSpan.FromSeconds(2));
            if (sentDuringCallback)
            {
                lock (zoneLock) { }
            }
        };

        respawn.Start();
        try
        {
            Assert.IsTrue(zoneLocked.Wait(TimeSpan.FromSeconds(5)));
            manager.Receive(connection, new byte[] { 1, 2, 3 });
            manager.GiveTime(50, giveConnectionsTime: false);
        }
        finally
        {
            callbackEntered.Set();
            Assert.IsTrue(respawn.Join(TimeSpan.FromSeconds(5)), "Respawn send did not finish.");
        }

        Assert.AreEqual(queued, sentDuringCallback);
        Assert.HasCount(1, connection.Packets);
    }

    [TestMethod]
    public void QueuedLifecycleEvents_AreDeliveredOnceWithoutBlockingPackets()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var manager = new CallbackManager(services, true);
        var connection = manager.CreateConnection();
        manager.CallbackConnectComplete(connection);
        manager.CallbackRoutePacket(connection, new byte[] { 1, 2, 3 });
        manager.CallbackTerminated(connection);
        manager.CallbackRoutePacket(connection, new byte[] { 4, 5 });

        Assert.AreEqual(0, connection.ConnectedCallbacks);
        Assert.HasCount(0, connection.Packets);
        manager.GiveTime(50, giveConnectionsTime: false);
        manager.GiveTime(50, giveConnectionsTime: false);

        Assert.AreEqual(1, connection.ConnectedCallbacks);
        Assert.AreEqual(1, connection.TerminatedCallbacks);
        Assert.HasCount(2, connection.Packets);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, connection.Packets[0]);
        CollectionAssert.AreEqual(new byte[] { 4, 5 }, connection.Packets[1]);
        manager.GetStats(out var stats);
        Assert.AreEqual(0, stats.EventListCount);
        Assert.AreEqual(0, stats.EventListBytes);
    }

    [TestMethod]
    public void QueuedPackets_OwnTheirDataAndPreserveOrderAndLength()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var manager = new CallbackManager(services, true);
        var connection = manager.CreateConnection();
        byte[] buffer = [1, 2, 3];
        manager.Receive(connection, buffer);
        buffer[0] = 9;
        manager.Receive(connection, buffer);
        Array.Clear(buffer);

        Assert.HasCount(0, connection.Packets);
        manager.GiveTime(50, giveConnectionsTime: false);

        Assert.HasCount(2, connection.Packets);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, connection.Packets[0]);
        CollectionAssert.AreEqual(new byte[] { 9, 2, 3 }, connection.Packets[1]);
    }

    [TestMethod]
    public void QueuedCallbackThatThrows_DropsOnlyItsConnection()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var manager = new CallbackManager(services, true);
        var faulty = manager.CreateConnection(12345);
        var healthy = manager.CreateConnection(12346);
        faulty.PacketAction = _ => throw new InvalidOperationException("A handler bug.");

        manager.Receive(faulty, [1, 1, 1]);
        manager.Receive(healthy, [2, 2, 2]);
        manager.Receive(faulty, [3, 3, 3]);

        // Without the guard this throws out of GiveTime, which on the Gateway is the server's only loop. Each call delivers
        // at least one event, so four deliver all four (three packets and faulty's disconnect) whatever the clock does.
        for (var i = 0; i < 4; i++)
            manager.GiveTime(50, giveConnectionsTime: false);

        Assert.AreEqual(Status.Disconnected, faulty.Status);
        Assert.AreEqual(DisconnectReason.CorruptPacket, faulty.DisconnectReason);
        Assert.AreEqual(1, faulty.TerminatedCallbacks);
        Assert.HasCount(1, faulty.Packets, "nothing after the packet that threw");
        Assert.AreEqual(Status.Connected, healthy.Status);
        CollectionAssert.AreEqual(new byte[] { 2, 2, 2 }, healthy.Packets.Single());
        manager.GetStats(out var stats);
        Assert.AreEqual(1, stats.ConnectionFaults);
        Assert.AreEqual(0, stats.EventListCount);
    }

    [TestMethod]
    public void QueuedPackets_AreNotDelivered_OnceTheirConnectionHasDisconnected()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        using var manager = new CallbackManager(services, true);
        var connection = manager.CreateConnection();
        manager.Receive(connection, [1, 2, 3]);
        manager.Receive(connection, [4, 5]);

        // a kick, say, after the packets were read and before they were handled
        connection.Disconnect();

        // each call delivers at least one event, so three deliver all three whatever the clock does
        for (var i = 0; i < 3; i++)
            manager.GiveTime(50, giveConnectionsTime: false);

        Assert.HasCount(0, connection.Packets);
        Assert.AreEqual(1, connection.TerminatedCallbacks);
        Assert.AreEqual(0, connection.QueuedPackets);
        manager.GetStats(out var stats);
        Assert.AreEqual(0, stats.EventListCount);
        Assert.AreEqual(0, stats.EventListBytes);
    }

    private sealed class CallbackManager : UdpManager<CallbackConnection>
    {
        public CallbackManager(IServiceProvider services, bool queued)
            : base(new UdpParams { EventQueuing = queued, UdpDriver = new TestDriver(), LingerDelay = 0 }, services)
        {
        }

        public CallbackConnection CreateConnection(int port = 12345)
        {
            var connection = new CallbackConnection(this, port);
            AddConnection(connection);
            return connection;
        }

        public void Receive(CallbackConnection connection, byte[] packet) =>
            ProcessRawPacket(connection.SocketAddress, packet);
    }

    private sealed class CallbackConnection : UdpConnection
    {
        public List<byte[]> Packets { get; } = [];
        public Action<byte[]>? PacketAction { get; set; }
        public int ConnectedCallbacks { get; private set; }
        public int TerminatedCallbacks { get; private set; }

        public CallbackConnection(CallbackManager manager, int port)
            : base(manager, new IPEndPoint(IPAddress.Loopback, port).Serialize(), port)
        {
        }

        public override void OnRoutePacket(Span<byte> data)
        {
            var packet = data.ToArray();
            Packets.Add(packet);
            PacketAction?.Invoke(packet);
        }

        public override void OnConnectComplete() => ConnectedCallbacks++;
        public override void OnTerminated() => TerminatedCallbacks++;
    }

    private sealed class TestDriver : IUdpDriver
    {
        public bool SocketOpen(int port, int incomingBufferSize, int outgoingBufferSize, string? bindIpAddress) => true;
        public void SocketClose() { }
        public int SocketReceive(Span<byte> buffer, SocketAddress socketAddress) => -1;
        public bool SocketSend(ReadOnlySpan<byte> data, SocketAddress socketAddress) => true;
        public void SocketSendPortAlive(ReadOnlySpan<byte> data, SocketAddress socketAddress) { }
        public bool SocketGetLocalIp(out IPAddress ipAddress) { ipAddress = IPAddress.Loopback; return true; }
        public int SocketGetLocalPort() => 12345;
        public bool GetHostByName(out IPAddress ipAddress, string hostName) { ipAddress = IPAddress.Loopback; return true; }
        public long Clock() => Environment.TickCount64;
        public void Sleep(int lingerDelay) { }
    }
}
