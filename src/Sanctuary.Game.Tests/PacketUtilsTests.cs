using System;
using System.Linq;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.Cryptography;
using Sanctuary.Core.IO;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The Gateway unwraps every packet a client sends and decrypts the ones marked encrypted. A marked packet too short to
/// hold the cipher's tag and nonce threw out of the decryption, and with the Gateway's callbacks queued that stopped the
/// whole server (stage 1 finding core-1).
/// </summary>
[TestClass]
public sealed class PacketUtilsTests
{
    // the Gateway keys a player's cipher with their login ticket, a Guid in "N" form
    private const string Ticket = "0123456789abcdef0123456789abcdef";

    private static CipherCCM Cipher()
    {
        var cipher = new CipherCCM();
        cipher.Initialize(Encoding.ASCII.GetBytes(Ticket));
        return cipher;
    }

    [TestMethod]
    public void EncryptedPacket_TooShortForTagAndNonce_IsRefused()
    {
        var cipher = Cipher();

        // a tag and a nonce take 29 bytes; then the encrypted flag and the wrapper's magic
        for (var length = 0; length < 29; length++)
        {
            byte[] packet = [.. Enumerable.Repeat((byte)0xAB, length), 0x01, 0xC9, 0xA5, 0xD4, 0xF3];

            Assert.IsFalse(PacketUtils.UnwrapPacket(packet, out _, cipher), $"a {length} byte body");
        }
    }

    [TestMethod]
    public void EncryptedPacket_RoundTrips()
    {
        byte[] payload = [1, 2, 3, 4, 5];

        using var writer = new PacketWriter();

        Assert.IsTrue(PacketUtils.WrapPacket(payload, writer, true, Cipher()));

        var packet = writer.Buffer;

        Assert.IsTrue(PacketUtils.UnwrapPacket(packet, out var length, Cipher()));
        CollectionAssert.AreEqual(payload, packet.AsSpan(0, length).ToArray());
    }
}
