using System;
using System.Net;

using Sanctuary.Core.Cryptography;
using Sanctuary.Core.IO;
using Sanctuary.UdpLibrary;
using Sanctuary.UdpLibrary.Configuration;
using Sanctuary.UdpLibrary.Enumerations;

namespace Sanctuary.LoadTest.Net;

public interface IBotConnectionHandler
{
    void OnConnectComplete(BotConnection connection);
    void OnPacket(BotConnection connection, ReadOnlySpan<byte> data);
    void OnTerminated(BotConnection connection, DisconnectReason reason);
    void OnCorrupt(BotConnection connection, string what);
}

/// <summary>
/// One socket per bot, as each real client has its own address and the servers key connections by address.
/// </summary>
public sealed class BotUdpManager : UdpManager<BotConnection>
{
    public BotUdpManager(UdpParams udpParams, IServiceProvider serviceProvider) : base(udpParams, serviceProvider)
    {
    }

    public static UdpParams CreateParams(string protocolName)
    {
        return new UdpParams(ManagerRole.ExternalClient)
        {
            ProtocolName = protocolName,
            KeepAliveDelay = 10000,

            // The servers negotiate compression (UserSupplied, one byte of expansion) in their confirm packet.
            UserSuppliedEncryptExpansionBytes = 1,

            // Flush every send at once, so the latency figure measures the server, not the bot.
            MaxDataHoldTime = 0,

            // Room for a burst while a worker thread is busy with other bots, so packets drop at the server if
            // anywhere. The kernel caps this at net.core.rmem_max.
            IncomingBufferSize = 1024 * 1024,
        };
    }
}

public sealed class BotConnection : UdpConnection
{
    private ICipher? _cipher;

    public IBotConnectionHandler? Handler { get; set; }

    public BotConnection(BotUdpManager manager, SocketAddress socketAddress, long timeout) : base(manager, socketAddress, timeout)
    {
    }

    /// <summary>
    /// The Login server encrypts every packet with RC4 under a key hardcoded in the client (see Sanctuary.Login.LoginConnection).
    /// </summary>
    public void UseLoginCipher()
    {
        _cipher = new CipherRC4();
        _cipher.Initialize(Convert.FromBase64String("F70IaxuU8C/w7FPXY1ibXw=="));
    }

    public void SendReliable(byte[] data)
    {
        if (_cipher is not null)
        {
            using var writer = new PacketWriter();

            if (!_cipher.Encrypt(data, writer))
                return;

            data = writer.Buffer;
        }

        Send(UdpChannel.Reliable1, data);
    }

    public override void OnConnectComplete() => Handler?.OnConnectComplete(this);

    public override void OnRoutePacket(Span<byte> data)
    {
        var length = data.Length;

        if (_cipher is not null && !_cipher.Decrypt(data, out length))
        {
            Handler?.OnCorrupt(this, "decrypt");
            return;
        }

        Handler?.OnPacket(this, data[..length]);
    }

    public override void OnTerminated()
    {
        var reason = DisconnectReason == DisconnectReason.OtherSideTerminated
            ? OtherSideDisconnectReason
            : DisconnectReason;

        Handler?.OnTerminated(this, reason);
    }

    public override void OnCrcReject(Span<byte> data) => Handler?.OnCorrupt(this, "crc");

    public override void OnPacketCorrupt(Span<byte> data, UdpCorruptionReason reason) => Handler?.OnCorrupt(this, reason.ToString());

    // The same one-byte compression framing as the servers' LoginConnection and GatewayConnection.

    protected override int DecryptUserSupplied(Span<byte> destData, Span<byte> sourceData)
    {
        if (sourceData[0] == 1)
            return ZLib.Decompress(sourceData.Slice(1), destData);

        sourceData.Slice(1).CopyTo(destData);

        return sourceData.Length - 1;
    }

    protected override int EncryptUserSupplied(Span<byte> destData, Span<byte> sourceData)
    {
        if (sourceData.Length >= 24)
        {
            var compressedLength = ZLib.Compress(sourceData, destData.Slice(1));

            if (compressedLength > 0 && compressedLength < sourceData.Length)
            {
                destData[0] = 1;

                return compressedLength + 1;
            }
        }

        destData[0] = 0;

        sourceData.CopyTo(destData.Slice(1));

        return sourceData.Length + 1;
    }
}
