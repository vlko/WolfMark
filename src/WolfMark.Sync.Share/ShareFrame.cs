using System.Buffers.Binary;
using System.Security.Cryptography;

namespace WolfMark.Sync.Share;

public enum ShareFrameKind : byte
{
    Request = 1,
    Response = 2,
}

/// <summary>
/// One message on the wire (ADR 0017): <c>"WMS1" | kind (1) | body length (4, big-endian) | nonce (12) |
/// ciphertext | tag (16)</c>, AES-256-GCM with the paired key and the 9 header bytes as associated data. The kind
/// is authenticated, so a request cannot be played back as a response. The Android app implements the same bytes
/// (<c>testdata/share-frames.json</c> is checked by both).
/// </summary>
public static class ShareFrame
{
    public const int HeaderLength = 9;
    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int KeyLength = 32;

    /// <summary>Largest body accepted: room for a few photos, small enough to hold in memory.</summary>
    public const int MaxBodyLength = 32 * 1024 * 1024;

    private static ReadOnlySpan<byte> Magic => "WMS1"u8;

    public static byte[] Seal(ShareFrameKind kind, ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext)
    {
        Span<byte> nonce = stackalloc byte[NonceLength];
        RandomNumberGenerator.Fill(nonce);
        return Seal(kind, key, nonce, plaintext);
    }

    /// <summary>With a given nonce – for the shared test vectors only; a nonce must never repeat under one key.</summary>
    internal static byte[] Seal(ShareFrameKind kind, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> plaintext)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(key.Length, KeyLength);
        ArgumentOutOfRangeException.ThrowIfNotEqual(nonce.Length, NonceLength);
        var bodyLength = NonceLength + plaintext.Length + TagLength;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bodyLength, MaxBodyLength);

        var frame = new byte[HeaderLength + bodyLength];
        var header = frame.AsSpan(0, HeaderLength);
        Magic.CopyTo(header);
        header[4] = (byte)kind;
        BinaryPrimitives.WriteInt32BigEndian(header[5..], bodyLength);

        var body = frame.AsSpan(HeaderLength);
        nonce.CopyTo(body);
        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, plaintext, body.Slice(NonceLength, plaintext.Length), body[^TagLength..], header);
        return frame;
    }

    /// <summary>False for anything that is not a frame header of an acceptable size – checked before reading the body.</summary>
    public static bool TryReadHeader(ReadOnlySpan<byte> header, out ShareFrameKind kind, out int bodyLength)
    {
        kind = default;
        bodyLength = 0;
        if (header.Length != HeaderLength || !header[..4].SequenceEqual(Magic))
        {
            return false;
        }

        if (header[4] is not ((byte)ShareFrameKind.Request or (byte)ShareFrameKind.Response))
        {
            return false;
        }

        kind = (ShareFrameKind)header[4];
        bodyLength = BinaryPrimitives.ReadInt32BigEndian(header[5..]);
        return bodyLength is >= NonceLength + TagLength and <= MaxBodyLength;
    }

    /// <summary>Decrypts a body read after <paramref name="header"/>; false when the key or any byte is wrong.</summary>
    public static bool TryOpen(ReadOnlySpan<byte> header, ReadOnlySpan<byte> body, ReadOnlySpan<byte> key, out byte[] plaintext)
    {
        plaintext = [];
        if (key.Length != KeyLength || !TryReadHeader(header, out _, out var bodyLength) || body.Length != bodyLength)
        {
            return false;
        }

        var result = new byte[body.Length - NonceLength - TagLength];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(body[..NonceLength], body[NonceLength..^TagLength], body[^TagLength..], result, header);
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }

        plaintext = result;
        return true;
    }
}
