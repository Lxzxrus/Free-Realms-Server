using System;
using System.Buffers.Binary;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.UdpLibrary.Tests;

/// <summary>
/// A connect request says the largest packet its sender takes, and the two sides use the smaller of their sizes. With no
/// minimum, the size a client asked for decided how many packets every reliable send cost the server.
/// <see cref="UdpParams.MinRawPacketSize"/> is the least a request may ask for; one asking for less is ignored.
/// </summary>
[TestClass]
public class MinRawPacketSizeTests
{
    private const string ServerIp = "10.0.0.1";
    private const string ClientIp = "10.0.0.2";

    private const int Payload = 100_000;

    private static FakeManager PlayerServer(FakeNetwork network, Action<UdpParams>? configure = null, IServiceProvider? services = null)
    {
        var udpParams = FakeManager.ServerParams();

        new PlayerUdpOptions().ApplyTo(udpParams);
        configure?.Invoke(udpParams);

        return FakeManager.Create(network, ServerIp, udpParams, services);
    }

    /// <summary>Rewrites the size in every connect request waiting for the server. A connect request has no CRC.</summary>
    private static int Forge(FakeManager server, int honestSize, int askedSize)
    {
        var forged = 0;

        foreach (var (_, data) in server.Driver.Inbox)
        {
            if (data.Length >= 14 && data[0] == 0 && BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(10)) == honestSize)
            {
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(10), askedSize);
                forged++;
            }
        }

        return forged;
    }

    /// <summary>A client whose connect requests ask for <paramref name="askedSize"/>, whatever its own size is.</summary>
    private static (FakeManager Client, FakeConnection Sender, int Forged) ConnectAskingFor(FakeNetwork network, FakeManager server, int? askedSize, int steps = 20, string ipAddress = ClientIp)
    {
        var client = FakeManager.Create(network, ipAddress, FakeManager.ClientParams());
        var sender = client.EstablishConnection(ServerIp, server.EndPoint.Port);

        Assert.IsNotNull(sender);

        var forged = 0;

        for (var i = 0; i < steps; i++)
        {
            client.GiveTime();

            if (askedSize is not null)
                forged += Forge(server, client.Params.MaxRawPacketSize, askedSize.Value);

            server.GiveTime();
            network.Now += 10;
        }

        return (client, sender, forged);
    }

    /// <summary>How many packets, and how large, the server sends to get one reliable packet of <see cref="Payload"/> bytes across.</summary>
    private static (int Datagrams, int Largest) SendFromServer(FakeNetwork network, FakeManager server, FakeManager client, FakeConnection sender, UdpChannel channel)
    {
        var accepted = server.Accepted.Single();
        var before = network.Sent.Count;

        var payload = new byte[Payload];
        new Random(7).NextBytes(payload);

        accepted.Send(channel, payload);

        for (var i = 0; i < 6000 && sender.Received.Count == 0 && sender.Status == Status.Connected && accepted.Status == Status.Connected; i++)
            network.Pump(1, server, client);

        Assert.AreEqual(Status.Connected, accepted.Status);
        CollectionAssert.AreEqual(payload, sender.Received.Single(), "the packet arrives whole");

        var fromServer = network.Sent.Skip(before).Where(sent => sent.From.Equals(server.EndPoint)).ToList();

        return (fromServer.Count, fromServer.Max(sent => sent.Data.Length));
    }

    /// <summary>A real client talks first, which is what proves it owns its address.</summary>
    private static void SayHello(FakeNetwork network, FakeManager server, FakeManager client, FakeConnection sender)
    {
        sender.Send(UdpChannel.Reliable1, new byte[] { 1, 2, 3 });
        network.Pump(10, server, client);

        Assert.AreEqual(1, server.Accepted.Single().Received.Count);
    }

    [TestMethod]
    [DataRow(255, DisplayName = "one under the minimum")]
    [DataRow(64)]
    [DataRow(11)]
    [DataRow(10)]
    [DataRow(1)]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(int.MinValue)]
    public void ConnectRequestAskingForLessThanTheMinimumIsIgnored(int askedSize)
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);

        Assert.AreEqual(256, server.Params.MinRawPacketSize);

        var (_, sender, forged) = ConnectAskingFor(network, server, askedSize);

        Assert.IsTrue(forged > 0, "the test rewrote a connect request");
        Assert.AreEqual(0, server.Accepted.Count, "no connection is made");
        Assert.AreEqual(0, network.Sent.Count(sent => sent.From.Equals(server.EndPoint)), "and nothing is answered");
        Assert.AreEqual(forged, server.Stats.RefusedPacketSize);
        Assert.AreNotEqual(Status.Connected, sender.Status);
    }

    [TestMethod]
    public void ConnectRequestAskingForTheMinimumIsAccepted()
    {
        // the usual size first, for the comparison
        var usualNetwork = new FakeNetwork();
        var usualServer = PlayerServer(usualNetwork);
        var (usualClient, usualSender, _) = ConnectAskingFor(usualNetwork, usualServer, askedSize: null);

        SayHello(usualNetwork, usualServer, usualClient, usualSender);

        var usual = SendFromServer(usualNetwork, usualServer, usualClient, usualSender, UdpChannel.Reliable1);

        Assert.IsTrue(usual.Largest > 256, "an honest client gets full-size packets");

        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender, forged) = ConnectAskingFor(network, server, server.Params.MinRawPacketSize);

        Assert.IsTrue(forged > 0);
        Assert.AreEqual(1, server.Accepted.Count);
        Assert.AreEqual(0, server.Stats.RefusedPacketSize);

        SayHello(network, server, client, sender);

        var smallest = SendFromServer(network, server, client, sender, UdpChannel.Reliable1);

        Assert.IsTrue(smallest.Largest <= 256, $"the server keeps to the size asked for (largest {smallest.Largest})");
        // 515 against 199 when this was written; with no minimum, a size of 11 cost 20,052
        Assert.IsTrue(smallest.Datagrams < usual.Datagrams * 3,
            $"the smallest size allowed costs {smallest.Datagrams} packets, the usual {usual.Datagrams}: under three times");
    }

    [TestMethod]
    public void IgnoredRequestsUseUpNoConnectionLimit()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);

        Assert.IsTrue(server.Params.ConnectRatePerIp is > 0 and < 50);

        var client = FakeManager.Create(network, ClientIp, FakeManager.ClientParams());
        var sender = client.EstablishConnection(ServerIp, server.EndPoint.Port);

        Assert.IsNotNull(sender);

        client.GiveTime();

        // the client's own request, taken off the wire and sent 50 times asking for 11 bytes
        var request = server.Driver.Inbox.Dequeue();
        Assert.AreEqual(0, server.Driver.Inbox.Count);

        var tiny = (byte[])request.Data.Clone();
        BinaryPrimitives.WriteInt32BigEndian(tiny.AsSpan(10), 11);

        for (var i = 0; i < 50; i++)
            network.Deliver(request.From, server.EndPoint, (byte[])tiny.Clone());

        server.GiveTime();

        Assert.AreEqual(50, server.Stats.RefusedPacketSize);
        Assert.AreEqual(0, server.Stats.RefusedAddressRate, "they were turned away before the per-address rate was counted");
        Assert.AreEqual(0, server.Accepted.Count);

        // the client asks again a second later, honestly this time, and gets in
        network.Pump(300, server, client);

        Assert.AreEqual(1, server.Accepted.Count);
        Assert.AreEqual(Status.Connected, sender.Status);
    }

    [TestMethod]
    public void ALaterRequestCannotShrinkAConnectionThatExists()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender, _) = ConnectAskingFor(network, server, askedSize: null);

        SayHello(network, server, client, sender);

        var accepted = server.Accepted.Single();

        // the connect request this connection was made from, again, now asking for 11 bytes
        network.Deliver(client.EndPoint, server.EndPoint, TinyCopyOfTheConnectRequest(network, client, anotherCode: false));
        network.Pump(2, server, client);

        Assert.AreEqual(Status.Connected, accepted.Status, "the request doesn't end the connection either");
        Assert.AreEqual(1, server.Accepted.Count);

        // a channel takes its packet size when it is first used, and nothing has used this one yet
        var (datagrams, largest) = SendFromServer(network, server, client, sender, UdpChannel.Reliable2);

        Assert.IsTrue(largest > 256, $"still full-size packets (largest {largest})");
        Assert.IsTrue(datagrams < 300, $"{datagrams} packets for {Payload} bytes");
    }

    /// <summary>The connect request a connection was made from, changed to ask for 11 bytes, with another connect code if asked.</summary>
    private static byte[] TinyCopyOfTheConnectRequest(FakeNetwork network, FakeManager client, bool anotherCode)
    {
        var request = network.Sent.First(sent => sent.From.Equals(client.EndPoint));
        var tiny = (byte[])request.Data.Clone();

        Assert.AreEqual(client.Params.MaxRawPacketSize, BinaryPrimitives.ReadInt32BigEndian(tiny.AsSpan(10)), "it is the connect request");
        BinaryPrimitives.WriteInt32BigEndian(tiny.AsSpan(10), 11);

        if (anotherCode)
            BinaryPrimitives.WriteInt32BigEndian(tiny.AsSpan(6), BinaryPrimitives.ReadInt32BigEndian(tiny.AsSpan(6)) + 1);

        return tiny;
    }

    [TestMethod]
    public void ATinyRequestWithAnotherCodeGetsNoAnswerFromAConnectionThatExists()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, sender, _) = ConnectAskingFor(network, server, askedSize: null);

        SayHello(network, server, client, sender);

        var accepted = server.Accepted.Single();
        var sentBefore = network.Sent.Count(sent => sent.From.Equals(server.EndPoint));

        // from the address alone: nothing but the server takes part, so whatever it sends is its answer to the request
        network.Deliver(client.EndPoint, server.EndPoint, TinyCopyOfTheConnectRequest(network, client, anotherCode: true));
        server.GiveTime();

        Assert.AreEqual(sentBefore, network.Sent.Count(sent => sent.From.Equals(server.EndPoint)), "no terminate packet goes back");
        Assert.AreEqual(Status.Connected, accepted.Status);
    }

    [TestMethod]
    public void ATinyRequestDoesNotDropAConnectionStillWaitingForItsFirstPacket()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network);
        var (client, _, _) = ConnectAskingFor(network, server, askedSize: null);

        // accepted, but the client hasn't sent a packet yet, so its address isn't proven
        var accepted = server.Accepted.Single();

        network.Deliver(client.EndPoint, server.EndPoint, TinyCopyOfTheConnectRequest(network, client, anotherCode: true));
        server.GiveTime();

        Assert.AreEqual(Status.Connected, accepted.Status, "a request the server would never accept can't clear the way for itself");
        Assert.AreEqual(1, server.Accepted.Count);
    }

    [TestMethod]
    public void SizeRefusalsDoNotTakeTheLogLineOfTheConnectionLimits()
    {
        var log = new ListLoggerProvider();
        var network = new FakeNetwork();
        var server = PlayerServer(network, udpParams => udpParams.MaxConnections = 1,
            new ServiceCollection().AddSingleton<ILoggerFactory>(log).BuildServiceProvider());

        ConnectAskingFor(network, server, askedSize: null);
        Assert.AreEqual(1, server.Accepted.Count, "the server is now full");

        // a tiny request first, then an honest one from someone else, well within the minute
        ConnectAskingFor(network, server, 11, ipAddress: "10.0.0.3");
        ConnectAskingFor(network, server, askedSize: null, ipAddress: "10.0.0.4");

        Assert.AreEqual(1, log.Messages.Count(message => message.Contains("at most 11 bytes")));
        Assert.AreEqual(1, log.Messages.Count(message => message.Contains("the server is full")), "still said, though a size refusal was logged a moment before");
        Assert.AreEqual(1, server.Accepted.Count);
    }

    [TestMethod]
    public void TheRefusalWarningSaysWhatSizeWasAskedFor()
    {
        var log = new ListLoggerProvider();
        var network = new FakeNetwork();
        var server = PlayerServer(network, services: new ServiceCollection().AddSingleton<ILoggerFactory>(log).BuildServiceProvider());

        ConnectAskingFor(network, server, 11);

        var warning = log.Messages.Single(message => message.Contains("Ignored a connect request"));

        StringAssert.Contains(warning, "at most 11 bytes");
        StringAssert.Contains(warning, "the least allowed is 256");
    }

    [TestMethod]
    [DataRow(64)]
    [DataRow(-1, DisplayName = "even a negative size, as before")]
    public void WithNoMinimumASmallSizeIsStillAccepted(int askedSize)
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network, udpParams => udpParams.MinRawPacketSize = 0);

        var (_, _, forged) = ConnectAskingFor(network, server, askedSize);

        Assert.IsTrue(forged > 0);
        Assert.AreEqual(1, server.Accepted.Count, "0 turns the check off");
        Assert.AreEqual(0, server.Stats.RefusedPacketSize);
    }

    [TestMethod]
    public void TheMinimumIsOnForPlayersAndOffByDefault()
    {
        Assert.AreEqual(0, new UdpParams().MinRawPacketSize, "the library's own default, which the Login-Gateway link keeps");

        var udpParams = new UdpParams();

        new PlayerUdpOptions().ApplyTo(udpParams);

        Assert.AreEqual(256, udpParams.MinRawPacketSize);
        Assert.IsTrue(udpParams.MinRawPacketSize <= udpParams.MaxRawPacketSize, "or every connect request would be ignored");
    }

    [TestMethod]
    public void AMinimumEqualToTheServersOwnSizeIsAllowed()
    {
        var network = new FakeNetwork();
        var server = PlayerServer(network, udpParams => udpParams.MinRawPacketSize = udpParams.MaxRawPacketSize);

        ConnectAskingFor(network, server, askedSize: null);

        Assert.AreEqual(1, server.Accepted.Count, "a client asking for the server's size gets in");
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(513, DisplayName = "more than the server's own size")]
    public void AMinimumThatCouldNeverBeMetIsRefusedAtStartup(int minimum)
    {
        var network = new FakeNetwork();

        try
        {
            PlayerServer(network, udpParams => udpParams.MinRawPacketSize = minimum);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            StringAssert.Contains(exception.ParamName, nameof(UdpParams.MinRawPacketSize), "it was this setting that stopped it");
            return;
        }

        Assert.Fail("the manager started");
    }
}
