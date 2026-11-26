using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.UdpLibrary.Tests;

/// <summary>
/// The Gateway's queue limits (<see cref="UdpParams.MaxQueuedPacketsPerConnection"/>, 4096 packets and 2 MiB at launch) are
/// sized for a player loading a zone. They applied from the moment a connection was made, so a connection that had not
/// logged in could have as much waiting as a player. <see cref="UdpParams.StartingMaxQueuedPacketsPerConnection"/> and
/// <see cref="UdpParams.StartingMaxQueuedBytesPerConnection"/> apply until the application calls
/// <see cref="UdpConnection.RaiseStartingLimits"/>, which the Gateway does when a login succeeds.
/// </summary>
[TestClass]
public class StartingQueueLimitsTests
{
    private const string ServerIp = "10.0.0.1";

    /// <summary>A server that queues its callbacks, as the Gateway does, with the launch limits unless changed.</summary>
    private static FakeManager QueuingServer(FakeNetwork network, Action<UdpParams>? configure = null, IServiceProvider? services = null)
    {
        var udpParams = FakeManager.ServerParams();

        new PlayerUdpOptions().ApplyTo(udpParams);
        udpParams.EventQueuing = true;
        configure?.Invoke(udpParams);

        return FakeManager.Create(network, ServerIp, udpParams, services);
    }

    private static (FakeManager Manager, FakeConnection Connection) Client(FakeNetwork network, FakeManager server, string ipAddress = "10.0.0.2")
    {
        var client = FakeManager.Create(network, ipAddress, FakeManager.ClientParams());
        var connection = client.EstablishConnection(ServerIp, server.EndPoint.Port);

        Assert.IsNotNull(connection);

        network.Pump(5, server, client);
        Assert.AreEqual(Status.Connected, connection.Status);

        // the client talks first, which is what proves it owns its address
        connection.Send(UdpChannel.Reliable1, new byte[] { 9, 9, 9 });
        network.Pump(10, server, client);

        return (client, connection);
    }

    /// <summary>Small packets, each in a datagram of its own, all read in one poll before any is handled.</summary>
    private static void DeliverSmallPackets(FakeNetwork network, FakeManager client, FakeConnection sender, FakeManager server, int count)
    {
        for (var i = 0; i < count; i++)
            network.Deliver(client.EndPoint, server.EndPoint, sender.Craft(1, (byte)i, (byte)(i >> 8)));
    }

    [TestMethod]
    public void AConnectionThatHasNotLoggedInMayHaveOnlyAFewPacketsWaiting()
    {
        var network = new FakeNetwork();
        var server = QueuingServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        Assert.AreEqual(32, server.Params.StartingMaxQueuedPacketsPerConnection);
        Assert.AreEqual(1, accepted.Received.Count, "the first packet was handled");

        // as many as the starting limit: all kept
        DeliverSmallPackets(network, client, sender, server, 32);
        network.Pump(1, server);

        Assert.AreEqual(Status.Connected, accepted.Status);
        Assert.AreEqual(33, accepted.Received.Count);
        Assert.AreEqual(0, accepted.QueuedPackets, "the limit is on packets waiting, not packets sent");

        // one more than the limit in one poll
        DeliverSmallPackets(network, client, sender, server, 33);
        network.Pump(1, server);

        Assert.AreEqual(Status.Disconnected, accepted.Status);
        Assert.AreEqual(DisconnectReason.CorruptPacket, accepted.DisconnectReason);
        Assert.AreEqual(1, server.Stats.QueueLimitDisconnects);
        Assert.AreEqual(33, accepted.Received.Count, "what it had waiting went with it");
        Assert.AreEqual(0, accepted.QueuedBytes);
    }

    [TestMethod]
    public void OneDatagramOfManyTinyPacketsIsStoppedAtTheStartingLimit()
    {
        var network = new FakeNetwork();
        var server = QueuingServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        // a multi packet (00 03) of 200 one-byte packets, 2 bytes each on the wire: each takes a buffer and an event
        var multi = new byte[2 + 200 * 2];
        multi[1] = 3;

        for (var i = 0; i < 200; i++)
        {
            multi[2 + i * 2] = 1;
            multi[3 + i * 2] = 7;
        }

        network.Deliver(client.EndPoint, server.EndPoint, sender.Craft(multi));
        network.Pump(1, server);

        Assert.AreEqual(Status.Disconnected, accepted.Status);
        Assert.AreEqual(1, server.Stats.QueueLimitDisconnects);
        Assert.AreEqual(1, accepted.Received.Count, "none of the 200 reached a handler");
    }

    [TestMethod]
    public void OnceRaisedTheFullLimitsApply()
    {
        var network = new FakeNetwork();
        var server = QueuingServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        accepted.RaiseStartingLimits();

        DeliverSmallPackets(network, client, sender, server, 1000);
        network.Pump(20, server);

        Assert.AreEqual(Status.Connected, accepted.Status);
        Assert.AreEqual(1001, accepted.Received.Count);
        Assert.AreEqual(0, server.Stats.QueueLimitDisconnects);
    }

    [TestMethod]
    public void RaisingOneConnectionLeavesTheOthersAtTheStartingLimits()
    {
        var network = new FakeNetwork();
        var server = QueuingServer(network);
        var (clientA, a) = Client(network, server, "10.0.0.2");
        var (clientB, b) = Client(network, server, "10.0.0.3");
        var serverA = server.Accepted[0];
        var serverB = server.Accepted[1];

        serverA.RaiseStartingLimits();

        DeliverSmallPackets(network, clientA, a, server, 100);
        DeliverSmallPackets(network, clientB, b, server, 100);
        network.Pump(5, server);

        Assert.AreEqual(Status.Connected, serverA.Status);
        Assert.AreEqual(101, serverA.Received.Count);
        Assert.AreEqual(Status.Disconnected, serverB.Status);
        Assert.AreEqual(1, server.Stats.QueueLimitDisconnects);
    }

    [TestMethod]
    public void TheStartingMemoryLimitStopsOneLargePacket()
    {
        var network = new FakeNetwork();
        var server = QueuingServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        Assert.AreEqual(32 * 1024, server.Params.StartingMaxQueuedBytesPerConnection);

        // one packet of 40,000 bytes: under the 64 KiB a packet may be, over what may wait before login
        sender.Send(UdpChannel.Reliable1, new byte[40_000]);

        for (var i = 0; i < 2000 && accepted.Status == Status.Connected && accepted.Received.Count < 2; i++)
            network.Pump(1, server, client);

        Assert.AreEqual(Status.Disconnected, accepted.Status);
        Assert.AreEqual(1, server.Stats.QueueLimitDisconnects);
        Assert.AreEqual(1, accepted.Received.Count);
    }

    [TestMethod]
    public void TheSameLargePacketIsFineOnceRaised()
    {
        var network = new FakeNetwork();
        var server = QueuingServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        accepted.RaiseStartingLimits();

        sender.Send(UdpChannel.Reliable1, new byte[40_000]);

        for (var i = 0; i < 2000 && accepted.Status == Status.Connected && accepted.Received.Count < 2; i++)
            network.Pump(1, server, client);

        Assert.AreEqual(Status.Connected, accepted.Status);
        Assert.AreEqual(40_000, accepted.Received.Last().Length);
        Assert.AreEqual(0, accepted.QueuedBytes);
    }

    [TestMethod]
    public void AStartingLimitAboveTheFullLimitChangesNothing()
    {
        var network = new FakeNetwork();
        var server = QueuingServer(network, udpParams =>
        {
            udpParams.MaxQueuedPacketsPerConnection = 4;
            udpParams.StartingMaxQueuedPacketsPerConnection = 100;

            // the same for memory, so that no starting limit is the one in force
            udpParams.StartingMaxQueuedBytesPerConnection = udpParams.MaxQueuedBytesPerConnection * 2;
        });

        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        DeliverSmallPackets(network, client, sender, server, 5);
        network.Pump(1, server);

        Assert.AreEqual(Status.Disconnected, accepted.Status, "the lower of the two holds: a starting limit never loosens the full one");
        Assert.IsFalse(accepted.AtStartingQueueLimits, "and the limit in force is the full one, not a starting one");
    }

    [TestMethod]
    public void WithNoStartingLimitsTheFullLimitsApplyFromTheStart()
    {
        var network = new FakeNetwork();
        var server = QueuingServer(network, udpParams =>
        {
            udpParams.StartingMaxQueuedPacketsPerConnection = 0;
            udpParams.StartingMaxQueuedBytesPerConnection = 0;
        });

        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        DeliverSmallPackets(network, client, sender, server, 1000);
        network.Pump(20, server);

        Assert.AreEqual(Status.Connected, accepted.Status, "0 turns the starting limits off");
        Assert.AreEqual(1001, accepted.Received.Count);

        // and the full limits are still limits: 5000 small packets in one poll is over both
        DeliverSmallPackets(network, client, sender, server, 5000);
        network.Pump(1, server);

        Assert.AreEqual(Status.Disconnected, accepted.Status);
        Assert.AreEqual(1, server.Stats.QueueLimitDisconnects);
    }

    [TestMethod]
    public void TheWarningListsSizesOnlyForARaisedConnection()
    {
        var log = new ListLoggerProvider();
        var network = new FakeNetwork();
        var server = QueuingServer(network, udpParams => udpParams.MaxQueuedPacketsPerConnection = 40,
            new ServiceCollection().AddSingleton<ILoggerFactory>(log).BuildServiceProvider());

        var (clientA, a) = Client(network, server, "10.0.0.2");
        var (clientB, b) = Client(network, server, "10.0.0.3");

        server.Accepted[1].RaiseStartingLimits();

        DeliverSmallPackets(network, clientA, a, server, 33);
        DeliverSmallPackets(network, clientB, b, server, 41);
        network.Pump(1, server);

        var warnings = log.Messages.Where(message => message.Contains("went over the")).ToList();

        Assert.HasCount(2, warnings);
        StringAssert.Contains(warnings.Single(message => message.Contains("limits: 32 packets")), "not listed at the starting limits");
        StringAssert.Contains(warnings.Single(message => message.Contains("limits: 40 packets")), "(bytes x count): 3 x 40.");
    }

    [TestMethod]
    [DataRow(-1, 0, DisplayName = "a negative packet limit")]
    [DataRow(0, -1, DisplayName = "a negative memory limit")]
    [DataRow(0, 639, DisplayName = "a memory limit that can't hold one packet")]
    public void StartingLimitsThatMakeNoSenseStopStartup(int packets, int bytes)
    {
        var network = new FakeNetwork();

        try
        {
            QueuingServer(network, udpParams =>
            {
                udpParams.StartingMaxQueuedPacketsPerConnection = packets;
                udpParams.StartingMaxQueuedBytesPerConnection = bytes;
            });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            StringAssert.Contains(exception.ParamName, "StartingMaxQueued", "it was one of these settings that stopped it");
            return;
        }

        Assert.Fail("the manager started");
    }

    [TestMethod]
    public void AMemoryLimitOfExactlyOnePacketIsAllowed()
    {
        var network = new FakeNetwork();
        var server = QueuingServer(network, udpParams => udpParams.StartingMaxQueuedBytesPerConnection = 640);

        Client(network, server);

        Assert.AreEqual(Status.Connected, server.Accepted.Single().Status);
        Assert.AreEqual(1, server.Accepted.Single().Received.Count, "one packet waiting at a time is within it");
    }

    [TestMethod]
    public void AStartingLimitWorksWhereThereIsNoFullLimit()
    {
        var network = new FakeNetwork();
        var server = QueuingServer(network, udpParams =>
        {
            udpParams.MaxQueuedPacketsPerConnection = 0;
            udpParams.MaxQueuedBytesPerConnection = 0;
            udpParams.StartingMaxQueuedPacketsPerConnection = 8;
            udpParams.StartingMaxQueuedBytesPerConnection = 0;
        });

        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        DeliverSmallPackets(network, client, sender, server, 9);
        network.Pump(1, server);

        Assert.AreEqual(Status.Disconnected, accepted.Status);
    }

    [TestMethod]
    public void TheWarningNamesTheLimitsThatApplied()
    {
        var log = new ListLoggerProvider();
        var network = new FakeNetwork();
        var server = QueuingServer(network, services: new ServiceCollection().AddSingleton<ILoggerFactory>(log).BuildServiceProvider());
        var (client, sender) = Client(network, server);

        DeliverSmallPackets(network, client, sender, server, 33);
        network.Pump(1, server);

        var warning = log.Messages.Single(message => message.Contains("went over the"));

        StringAssert.Contains(warning, "packet count limit");
        StringAssert.Contains(warning, "(limits: 32 packets, 32768 bytes)");
    }

    [TestMethod]
    public void TheStartingLimitsAreOnForPlayersAndOffByDefault()
    {
        var plain = new UdpParams();

        Assert.AreEqual(0, plain.StartingMaxQueuedPacketsPerConnection, "the library's own default");
        Assert.AreEqual(0, plain.StartingMaxQueuedBytesPerConnection);

        var udpParams = new UdpParams();

        new PlayerUdpOptions().ApplyTo(udpParams);

        Assert.IsTrue(udpParams.StartingMaxQueuedPacketsPerConnection is > 0 and <= 64, "a connection that hasn't logged in has one packet to send");
        Assert.IsTrue(udpParams.StartingMaxQueuedPacketsPerConnection < udpParams.MaxQueuedPacketsPerConnection);
        Assert.IsTrue(udpParams.StartingMaxQueuedBytesPerConnection is >= 4 * 1024 and <= 64 * 1024, "room for a login packet many times over");
        Assert.IsTrue(udpParams.StartingMaxQueuedBytesPerConnection < udpParams.MaxQueuedBytesPerConnection);
    }
}
