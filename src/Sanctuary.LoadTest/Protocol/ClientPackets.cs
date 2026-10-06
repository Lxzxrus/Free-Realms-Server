using System;
using System.Collections.Generic;
using System.Numerics;

using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Chat;

namespace Sanctuary.LoadTest.Protocol;

/// <summary>
/// The client side of the packets the bot needs. The server's packet classes only read what the client
/// sends and only write what the server sends, so these mirror them field for field: every writer here
/// matches a server <c>TryDeserialize</c>, and every reader matches a server <c>Serialize</c>.
/// </summary>
public static class ClientPackets
{
    // Login server (Sanctuary.Login), RC4-encrypted.

    public static byte[] LoginRequest(string session)
    {
        using var writer = new PacketWriter();

        writer.Write(Sanctuary.Packet.LoginRequest.OpCode);
        writer.Write(session);
        writer.Write("loadtest"); // Fingerprint

        return writer.Buffer;
    }

    public static byte[] CharacterSelectInfoRequest()
    {
        return [Sanctuary.Packet.CharacterSelectInfoRequest.OpCode];
    }

    /// <summary>
    /// A plain human male in starter clothes. Every id is one the Login server checks against its resources.
    /// </summary>
    public static byte[] CharacterCreateRequest(string firstName)
    {
        using var data = new PacketWriter();

        data.Write(firstName);      // FirstName
        data.Write(string.Empty);   // LastName
        data.Write(string.Empty);   // TemporaryFirstName
        data.Write(string.Empty);   // TemporaryLastName
        data.Write(1);              // Model: human_m.adr
        data.Write(1);              // Head: HeadMappings.txt
        data.Write(1);              // Hair: HairMappings.txt
        data.Write(0);              // ModelCustomization: none
        data.Write(0);              // FacePaint: none
        data.Write(1);              // SkinTone: SkinToneMappings.txt
        data.Write(0);              // ItemHands
        data.Write(101);            // ItemFeet: caster squaretip
        data.Write(103);            // ItemLegs: caster slacks
        data.Write(104);            // ItemChest: caster shortcloak
        data.Write(0);              // ItemHead
        data.Write(0);              // ItemShoulders
        data.Write(0);              // TintHands
        data.Write(0);              // TintFeet
        data.Write(0);              // TintLegs
        data.Write(0);              // TintChest
        data.Write(0);              // TintHead
        data.Write(0);              // TintShoulders
        data.Write(0);              // EyeColor
        data.Write(0);              // HairColor
        data.Write(0);              // Location

        using var writer = new PacketWriter();

        writer.Write(Sanctuary.Packet.CharacterCreateRequest.OpCode);
        writer.Write(1); // ServerId
        writer.WritePayload(data.Buffer);

        return writer.Buffer;
    }

    public static byte[] CharacterLoginRequest(ulong guid)
    {
        using var data = new PacketWriter();

        data.Write("en_US");    // Locale
        data.Write(8);          // LocaleId, as run_client.py passes Internationalization:Locale=8
        data.Write(false);
        data.Write((byte)0);
        data.Write(0L);

        using var writer = new PacketWriter();

        writer.Write(Sanctuary.Packet.CharacterLoginRequest.OpCode);
        writer.Write(guid);
        writer.Write(1); // ServerId
        writer.WritePayload(data.Buffer);

        return writer.Buffer;
    }

    public static bool TryReadLoginReply(ReadOnlySpan<byte> data, out bool loggedIn, out int status)
    {
        loggedIn = false;
        status = 0;

        var reader = new PacketReader(data);

        return reader.TryRead(out byte opCode) && opCode == LoginReply.OpCode
            && reader.TryRead(out loggedIn)
            && reader.TryRead(out status);
    }

    public static bool TryReadCharacterSelectInfoReply(ReadOnlySpan<byte> data, out List<ulong> guids)
    {
        guids = [];

        var reader = new PacketReader(data);

        if (!reader.TryRead(out byte opCode) || opCode != CharacterSelectInfoReply.OpCode)
            return false;

        if (!reader.TryRead(out int _) || !reader.TryRead(out bool _) || !reader.TryRead(out int count))
            return false;

        for (var i = 0; i < count; i++)
        {
            if (!reader.TryRead(out ulong guid)
                || !reader.TryRead(out int _)               // LastServerId
                || !reader.TryRead(out int _)               // Status
                || !reader.TryRead(out int payloadSize)
                || !reader.TryReadExact(payloadSize, out _)) // ApplicationData
                return false;

            guids.Add(guid);
        }

        return true;
    }

    public static bool TryReadCharacterCreateReply(ReadOnlySpan<byte> data, out int result, out ulong guid)
    {
        result = 0;
        guid = 0;

        var reader = new PacketReader(data);

        return reader.TryRead(out byte opCode) && opCode == CharacterCreateReply.OpCode
            && reader.TryRead(out result)
            && reader.TryRead(out guid);
    }

    public static bool TryReadCharacterLoginReply(ReadOnlySpan<byte> data, out int status, out string serverAddress, out string ticket)
    {
        status = 0;
        serverAddress = string.Empty;
        ticket = string.Empty;

        var reader = new PacketReader(data);

        if (!reader.TryRead(out byte opCode) || opCode != CharacterLoginReply.OpCode)
            return false;

        if (!reader.TryRead(out ulong _) || !reader.TryRead(out int _) || !reader.TryRead(out status))
            return false;

        if (status != 1)
            return true;

        // ClientCharacterData, wrapped as a payload.
        return reader.TryRead(out int _)
            && reader.TryRead(out serverAddress)
            && reader.TryRead(out ticket);
    }

    // Gateway server (Sanctuary.Gateway), unencrypted.

    public static byte[] PacketLogin(string ticket, ulong guid, string clientVersion)
    {
        using var writer = new PacketWriter();

        writer.Write(Sanctuary.Packet.PacketLogin.OpCode);
        writer.Write(ticket);
        writer.Write(guid);
        writer.Write(clientVersion);
        writer.Write(string.Empty); // Unknown

        return writer.Buffer;
    }

    public static bool TryReadPacketLoginReply(ReadOnlySpan<byte> data, out bool success)
    {
        success = false;

        var reader = new PacketReader(data);

        return reader.TryRead(out short opCode) && opCode == PacketLoginReply.OpCode
            && reader.TryRead(out success);
    }

    public static byte[] Tunneled(byte[] payload)
    {
        return new PacketTunneledClientPacket { Payload = payload }.Serialize();
    }

    public static byte[] ClientIsReady()
    {
        return Tunneled(BitConverter.GetBytes(PacketClientIsReady.OpCode));
    }

    public static byte[] ClientFinishedLoading()
    {
        return Tunneled(BitConverter.GetBytes(PacketClientFinishedLoading.OpCode));
    }

    public static byte[] UpdatePosition(ulong guid, Vector4 position, Quaternion rotation, byte state)
    {
        return Tunneled(new PlayerUpdatePacketUpdatePosition
        {
            Guid = guid,
            Position = position,
            Rotation = rotation,
            State = state
        }.Serialize());
    }

    public static byte[] Jump(ulong guid, Vector4 position, Quaternion rotation, float verticalVelocity)
    {
        return Tunneled(new PlayerUpdatePacketJump
        {
            Guid = guid,
            Position = position,
            Rotation = rotation,
            State = 1,
            VerticalVelocity = verticalVelocity
        }.Serialize());
    }

    public static byte[] Say(string message)
    {
        return Tunneled(new PacketChat
        {
            Channel = ChatChannel.WorldSay,
            Message = message
        }.Serialize());
    }
}
