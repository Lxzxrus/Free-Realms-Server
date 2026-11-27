using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.UdpLibrary.Tests;

/// <summary>
/// Two more of the limits a connection has until the application calls <see cref="UdpConnection.RaiseStartingLimits"/>
/// (the servers do when a login succeeds). A reliable packet that arrives early is kept, in a buffer of its own, until
/// the ones before it arrive: <see cref="UdpParams.StartingReliableWindow"/> is how far ahead one may be before then.
/// And a connection that connected but never logs in held its place for as long as it kept sending something:
/// <see cref="UdpParams.StartingTimeout"/> is how long it has.
/// </summary>
[TestClass]
public class StartingWindowAndTimeoutTests
{
    private const string ServerIp = "10.0.0.1";

    /// <summary>A server with the player ports' launch settings. Callbacks run inline, as on the Login server.</summary>
    private static FakeManager PlayerServer(FakeNetwork network, Action<UdpParams>? configure = null, IServiceProvider? services = null)
    {
        var udpParams = FakeManager.ServerParams();

        new PlayerUdpOptions().ApplyTo(udpParams);
        configure?.Invoke(udpParams);

        return FakeManager.Create(network, ServerIp, udpParams, services);
    }

    /// <summary>A connected client whose first reliable packet (stamp 0 on the first channel) has arrived.</summary>
    private static (FakeManager Manager, FakeConnection Connection) Client(FakeNetwork network, FakeManager server, string ipAddress = "10.0.0.2")
    {
        var client = FakeManager.Create(network, ipAddress, FakeManager.ClientParams());
        var connection = client.EstablishConnection(ServerIp, server.EndPoint.Port);

        Assert.IsNotNull(connection);

        network.Pump(5, server, client);
        Assert.AreEqual(Status.Connected, connection.Status);

        connection.Send(UdpChannel.Reliable1, new byte[] { 9, 9, 9 });
        network.Pump(10, server, client);

        return (client, connection);
    }

    /// <summary>A reliable packet on the first channel (00 09) with this stamp, as the client would put it on the wire.</summary>
    private static byte[] Reliable(FakeConnection sender, int stamp) =>
        sender.Craft(0, 9, (byte)(stamp >> 8), (byte)stamp, 7, (byte)stamp, (byte)(stamp >> 8));

    /// <summary>
    /// Stamps 3 to 400 arrive while 1 and 2 are still missing, so each is early; then 1 and 2 arrive, and whatever was
    /// kept is handled after them. Returns how many packets reached the application in all, the first one included.
    /// </summary>
    private static int SendEarlyThenTheMissing(FakeNetwork network, FakeManager server, FakeManager client, FakeConnection sender)
    {
        var accepted = server.Accepted.Single(connection => connection.Status == Status.Connected);

        Assert.AreEqual(1, accepted.Received.Count, "the first packet was handled");

        for (var stamp = 3; stamp <= 400; stamp++)
            network.Deliver(client.EndPoint, server.EndPoint, Reliable(sender, stamp));

        network.Pump(1, server);

        Assert.AreEqual(1, accepted.Received.Count, "nothing early is handled before the missing ones arrive");

        network.Deliver(client.EndPoint, server.EndPoint, Reliable(sender, 1));
        network.Deliver(client.EndPoint, server.EndPoint, Reliable(sender, 2));
        network.Pump(1, server);

        Assert.AreEqual(Status.Connected, accepted.Status);

        return accepted.Received.Count;
    }

    [TestMethod]
    public void BeforeLoginOnlyAFewEarlyPacketsAreKept()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender) = Client(network, server);

        Assert.AreEqual(16, server.Params.StartingReliableWindow);

        // expected next is 1, so with a window of 16 stamps 1 to 16 are taken: 3 to 16 were kept, 17 to 400 were not
        Assert.AreEqual(1 + 16, SendEarlyThenTheMissing(network, server, client, sender));
    }

    [TestMethod]
    public void OnceRaisedTheChannelsOwnWindowApplies()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender) = Client(network, server);

        server.Accepted.Single().RaiseStartingLimits();

        Assert.AreEqual(1 + 400, SendEarlyThenTheMissing(network, server, client, sender), "all 398 early ones were kept");
    }

    [TestMethod]
    public void WithNoStartingWindowTheChannelsOwnAppliesFromTheStart()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network, udpParams => udpParams.StartingReliableWindow = 0);
        var (client, sender) = Client(network, server);

        Assert.AreEqual(1 + 400, SendEarlyThenTheMissing(network, server, client, sender), "0 turns it off");
    }

    [TestMethod]
    public void AStartingWindowAboveTheChannelsOwnChangesNothing()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network, udpParams => udpParams.StartingReliableWindow = 100_000);
        var (client, sender) = Client(network, server);

        Assert.AreEqual(1 + 400, SendEarlyThenTheMissing(network, server, client, sender));
    }

    [TestMethod]
    public void PacketsThatArriveInOrderAreNotHeldBackByTheStartingWindow()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        // far more reliable packets than the window, but each arrives when it is the next one expected
        var payload = new byte[20_000];
        new Random(11).NextBytes(payload);
        payload[0] = 1;

        sender.Send(UdpChannel.Reliable1, payload);

        for (var i = 0; i < 2000 && accepted.Received.Count < 2 && accepted.Status == Status.Connected; i++)
            network.Pump(1, server, client);

        Assert.AreEqual(Status.Connected, accepted.Status);
        CollectionAssert.AreEqual(payload, accepted.Received.Last());
    }

    /// <summary>Keeps the client talking, a small packet a second, for this many simulated seconds.</summary>
    private static void ChatFor(FakeNetwork network, FakeManager server, FakeManager client, FakeConnection sender, int seconds)
    {
        for (var second = 0; second < seconds; second++)
        {
            if (sender.Status == Status.Connected)
                sender.Send(UdpChannel.Reliable1, new byte[] { 5, (byte)second });

            network.Pump(10, 100, server, client);
        }
    }

    [TestMethod]
    public void AConnectionThatNeverLogsInIsDroppedHoweverMuchItSends()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        Assert.AreEqual(60000, server.Params.StartingTimeout);

        ChatFor(network, server, client, sender, 55);

        Assert.AreEqual(Status.Connected, accepted.Status, "not before the time is up");
        Assert.IsTrue(accepted.Received.Count > 50, "and what it sent in the meantime was handled");

        ChatFor(network, server, client, sender, 10);

        Assert.AreEqual(Status.Disconnected, accepted.Status);
        Assert.AreEqual(DisconnectReason.Timeout, accepted.DisconnectReason);
        Assert.AreEqual(1, server.Stats.StartingTimeoutDisconnects);
    }

    [TestMethod]
    public void AConnectionThatLoggedInStays()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        ChatFor(network, server, client, sender, 10);
        accepted.RaiseStartingLimits();
        ChatFor(network, server, client, sender, 120);

        Assert.AreEqual(Status.Connected, accepted.Status);
        Assert.AreEqual(0, server.Stats.StartingTimeoutDisconnects);
    }

    [TestMethod]
    public void WithNoStartingTimeoutNothingIsDropped()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network, udpParams => udpParams.StartingTimeout = 0);
        var (client, sender) = Client(network, server);

        ChatFor(network, server, client, sender, 150);

        Assert.AreEqual(Status.Connected, server.Accepted.Single().Status, "0 turns it off");
        Assert.AreEqual(0, server.Stats.StartingTimeoutDisconnects);
    }

    /// <summary>Keeps every client talking, a small packet a second each, for this many simulated seconds.</summary>
    private static void ChatFor(FakeNetwork network, FakeManager server, System.Collections.Generic.List<(FakeManager Manager, FakeConnection Connection)> clients, int seconds)
    {
        var managers = new IGiveTime[] { server }.Concat(clients.Select(found => (IGiveTime)found.Manager)).ToArray();

        for (var second = 0; second < seconds; second++)
        {
            foreach (var (_, sender) in clients)
            {
                if (sender.Status == Status.Connected)
                    sender.Send(UdpChannel.Reliable1, new byte[] { 5, (byte)second });
            }

            network.Pump(10, 100, managers);
        }
    }

    [TestMethod]
    public void DroppedConnectionsShareOneLogLineAMinute()
    {
        var log = new ListLoggerProvider();
        var network = new FakeNetwork();
        var server = PlayerServer(network, services: new ServiceCollection().AddSingleton<ILoggerFactory>(log).BuildServiceProvider());

        var clients = new[] { "10.0.0.2", "10.0.0.3", "10.0.0.4" }.Select(address => Client(network, server, address)).ToList();

        ChatFor(network, server, clients, 65);

        Assert.AreEqual(3, server.Stats.StartingTimeoutDisconnects);
        Assert.IsTrue(server.Accepted.All(connection => connection.Status == Status.Disconnected));

        var line = log.Messages.Single(message => message.Contains("still hasn't logged in"));

        StringAssert.Contains(line, "it connected 60 s ago");
        StringAssert.Contains(line, "(the limit is 60 s)");
        Assert.IsFalse(line.Contains("other connections"), "the first line has nobody to count");

        // a fourth, well over a minute after that line: its own line says how many went without one
        var late = new[] { Client(network, server, "10.0.0.5") }.ToList();

        ChatFor(network, server, late, 65);

        Assert.AreEqual(4, server.Stats.StartingTimeoutDisconnects);

        var lines = log.Messages.Where(message => message.Contains("still hasn't logged in")).ToList();

        Assert.HasCount(2, lines);
        StringAssert.Contains(lines[1], "2 other connections went the same way");
    }

    [TestMethod]
    public void AConnectionWithAPacketStillWaitingForItsHandlerIsNotCutOff()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        // as on the Gateway when its one thread is held up: the login has arrived and waits in the queue for its handler
        accepted.QueuedPackets = 1;

        ChatFor(network, server, client, sender, 90);

        Assert.AreEqual(Status.Connected, accepted.Status, "well past the time, but its packet hasn't been handled yet");
        Assert.AreEqual(0, server.Stats.StartingTimeoutDisconnects);

        // the handler ran and it was a login
        accepted.QueuedPackets = 0;
        accepted.RaiseStartingLimits();

        ChatFor(network, server, client, sender, 5);

        Assert.AreEqual(Status.Connected, accepted.Status);
    }

    [TestMethod]
    public void OnceItsWaitingPacketHasBeenHandledAConnectionThatDidNotLogInIsDropped()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        accepted.QueuedPackets = 1;

        ChatFor(network, server, client, sender, 90);

        Assert.AreEqual(Status.Connected, accepted.Status);

        // the handler ran and it wasn't a login
        accepted.QueuedPackets = 0;

        ChatFor(network, server, client, sender, 2);

        Assert.AreEqual(Status.Disconnected, accepted.Status, "within the second, not after another full wait");
        Assert.AreEqual(DisconnectReason.Timeout, accepted.DisconnectReason);
    }

    [TestMethod]
    public void AnEarlyPacketDroppedBeforeLoginIsTakenWhenItIsSentAgainAfter()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        // 100 is far ahead of 1: dropped, as if lost
        network.Deliver(client.EndPoint, server.EndPoint, Reliable(sender, 100));
        network.Pump(1, server);

        accepted.RaiseStartingLimits();

        // the client sends it again, and the ones before it arrive
        network.Deliver(client.EndPoint, server.EndPoint, Reliable(sender, 100));

        for (var stamp = 1; stamp <= 99; stamp++)
            network.Deliver(client.EndPoint, server.EndPoint, Reliable(sender, stamp));

        network.Pump(1, server);

        Assert.AreEqual(Status.Connected, accepted.Status);
        Assert.AreEqual(1 + 100, accepted.Received.Count);
    }

    [TestMethod]
    public void BeforeLoginOnlyALittleMayWaitToBeSentToAConnection()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (_, _) = Client(network, server);
        var accepted = server.Accepted.Single();

        Assert.AreEqual(64 * 1024, server.Params.StartingReliableOverflowBytes);

        // answers the other side never takes: 100,000 bytes waiting, and the client is given no time to acknowledge any
        accepted.Send(UdpChannel.Reliable1, new byte[100_000]);
        network.Pump(3, server);

        Assert.AreEqual(Status.Disconnected, accepted.Status);
        Assert.AreEqual(DisconnectReason.ReliableOverflow, accepted.DisconnectReason);
    }

    [TestMethod]
    public void OnceRaisedAsMuchMayWaitAsForAnyPlayer()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (_, _) = Client(network, server);
        var accepted = server.Accepted.Single();

        accepted.RaiseStartingLimits();

        accepted.Send(UdpChannel.Reliable1, new byte[100_000]);
        network.Pump(3, server);

        Assert.AreEqual(Status.Connected, accepted.Status, "far under the 16 MiB a player may have waiting");
    }

    [TestMethod]
    public void ASmallAnswerBeforeLoginIsFine()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender) = Client(network, server);
        var accepted = server.Accepted.Single();

        // what a server sends before it knows who is there: a refusal, a server list
        accepted.Send(UdpChannel.Reliable1, new byte[300]);
        network.Pump(10, server, client);

        Assert.AreEqual(Status.Connected, accepted.Status);
        Assert.AreEqual(300, sender.Received.Single().Length);
    }

    [TestMethod]
    [DataRow(-1, 0, 0, nameof(UdpParams.StartingReliableWindow), DisplayName = "a negative window")]
    [DataRow(0, -1, 0, nameof(UdpParams.StartingTimeout), DisplayName = "a negative timeout")]
    [DataRow(0, 5000, 0, nameof(UdpParams.StartingTimeout), DisplayName = "a timeout inside the handshake's own 10 s")]
    [DataRow(0, 0, -1, nameof(UdpParams.StartingReliableOverflowBytes), DisplayName = "a negative amount waiting")]
    [DataRow(0, 0, 1000, nameof(UdpParams.StartingReliableOverflowBytes), DisplayName = "too little waiting for even a few small answers")]
    public void StartingValuesThatMakeNoSenseStopStartup(int window, int timeout, int overflowBytes, string setting)
    {
        var network = new FakeNetwork();

        try
        {
            PlayerServer(network, udpParams =>
            {
                udpParams.StartingReliableWindow = window;
                udpParams.StartingTimeout = timeout;
                udpParams.StartingReliableOverflowBytes = overflowBytes;
            });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            StringAssert.Contains(exception.ParamName, setting);
            return;
        }

        Assert.Fail("the manager started");
    }

    [TestMethod]
    public void ATimeoutEqualToTheHandshakesOwnIsAllowed()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network, udpParams => udpParams.StartingTimeout = udpParams.HandshakeTimeout);

        Assert.AreEqual(10000, server.Params.StartingTimeout);
    }

    [TestMethod]
    public void BothAreOnForPlayersAndOffByDefault()
    {
        var plain = new UdpParams();

        Assert.AreEqual(0, plain.StartingReliableWindow, "the library's own default, which the Login-Gateway link keeps");
        Assert.AreEqual(0, plain.StartingTimeout);

        var udpParams = new UdpParams();

        new PlayerUdpOptions().ApplyTo(udpParams);

        Assert.IsTrue(udpParams.StartingReliableWindow is >= 4 and <= 64, "room for the packet or two a client sends before login, and little more");
        Assert.IsTrue(udpParams.StartingTimeout >= udpParams.HandshakeTimeout + 10000, "well after the handshake's own time");
        Assert.IsTrue(udpParams.StartingTimeout <= 120000, "but not so long that a place is held for minutes");
        Assert.IsTrue(udpParams.StartingReliableOverflowBytes is >= 16 * 1024 and <= 256 * 1024, "room for small answers, far under a player's");
        Assert.IsTrue(udpParams.StartingReliableOverflowBytes < udpParams.ReliableOverflowBytes);
        Assert.AreEqual(0, plain.StartingReliableOverflowBytes);
    }
}
