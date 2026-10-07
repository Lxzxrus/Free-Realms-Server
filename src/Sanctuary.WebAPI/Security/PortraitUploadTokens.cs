using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Sanctuary.WebAPI.Security;

/// <summary>
/// Signed tokens that put a user's identity into the portrait upload URL the client is given at login. The client
/// posts portraits to that URL with no credentials of its own, so the token is what proves who is uploading.
/// <para>
/// A token is 64 hex characters: user id, expiry (unix seconds) and a truncated HMAC-SHA256 over both. The key is
/// random per process, so restarting WebAPI invalidates every token; players get a fresh one on their next login.
/// </para>
/// </summary>
public sealed class PortraitUploadTokens
{
    private const int PayloadLength = 16;
    private const int MacLength = 16;
    private const int TokenLength = (PayloadLength + MacLength) * 2;

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly TimeProvider _timeProvider;

    public PortraitUploadTokens(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public string Create(ulong userId, TimeSpan lifetime)
    {
        Span<byte> token = stackalloc byte[PayloadLength + MacLength];

        var expiry = (_timeProvider.GetUtcNow() + lifetime).ToUnixTimeSeconds();

        BinaryPrimitives.WriteUInt64BigEndian(token, userId);
        BinaryPrimitives.WriteInt64BigEndian(token[8..], expiry);

        ComputeMac(token[..PayloadLength], token[PayloadLength..]);

        return Convert.ToHexStringLower(token);
    }

    public bool TryValidate(string? token, out ulong userId)
    {
        userId = 0;

        if (token is null || token.Length != TokenLength)
            return false;

        Span<byte> bytes = stackalloc byte[PayloadLength + MacLength];

        if (Convert.FromHexString(token, bytes, out _, out var written) != System.Buffers.OperationStatus.Done
            || written != bytes.Length)
        {
            return false;
        }

        Span<byte> expectedMac = stackalloc byte[MacLength];

        ComputeMac(bytes[..PayloadLength], expectedMac);

        if (!CryptographicOperations.FixedTimeEquals(expectedMac, bytes[PayloadLength..]))
            return false;

        var expiry = BinaryPrimitives.ReadInt64BigEndian(bytes[8..]);

        if (_timeProvider.GetUtcNow().ToUnixTimeSeconds() >= expiry)
            return false;

        userId = BinaryPrimitives.ReadUInt64BigEndian(bytes);

        return true;
    }

    private void ComputeMac(ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        Span<byte> mac = stackalloc byte[HMACSHA256.HashSizeInBytes];

        HMACSHA256.HashData(_key, payload, mac);

        mac[..MacLength].CopyTo(destination);
    }
}
