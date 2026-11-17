using System.Text.Json;

namespace WolfMark.Sync.Share.Tests;

/// <summary>The bytes on the wire: the vectors shared with the Android app, and what must be refused.</summary>
public sealed class ShareProtocolTests
{
    private static readonly JsonElement Vectors = Load();
    private static readonly byte[] Key = Convert.FromHexString(Vectors.GetProperty("keyHex").GetString()!);

    private static JsonElement Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "share-frames.json")));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Requests_encode_to_the_shared_vectors_and_decode_back()
    {
        var count = 0;
        foreach (var v in Vectors.GetProperty("requests").EnumerateArray())
        {
            var attachments = v.GetProperty("attachments").EnumerateArray()
                .Select(a => new ShareAttachment(a.GetProperty("extension").GetString()!, Convert.FromHexString(a.GetProperty("dataHex").GetString()!)))
                .ToArray();
            var message = new ShareMessage(
                Guid.Parse(v.GetProperty("id").GetString()!),
                DateTimeOffset.FromUnixTimeMilliseconds(v.GetProperty("sentUnixMs").GetInt64()),
                DateTime.Parse(v.GetProperty("createdLocal").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                v.GetProperty("markdown").GetString()!,
                attachments);

            var plaintext = message.Encode();
            Assert.Equal(v.GetProperty("plaintextHex").GetString(), Convert.ToHexStringLower(plaintext));
            var frame = ShareFrame.Seal(ShareFrameKind.Request, Key, Convert.FromHexString(v.GetProperty("nonceHex").GetString()!), plaintext);
            Assert.Equal(v.GetProperty("frameHex").GetString(), Convert.ToHexStringLower(frame));

            Assert.True(ShareFrame.TryReadHeader(frame.AsSpan(0, ShareFrame.HeaderLength), out var kind, out var bodyLength));
            Assert.Equal(ShareFrameKind.Request, kind);
            Assert.Equal(frame.Length - ShareFrame.HeaderLength, bodyLength);
            Assert.True(ShareFrame.TryOpen(frame.AsSpan(0, ShareFrame.HeaderLength), frame.AsSpan(ShareFrame.HeaderLength), Key, out var opened));
            Assert.True(ShareMessage.TryDecode(opened, out var decoded));
            Assert.Equal(message.Id, decoded.Id);
            Assert.Equal(message.SentAt, decoded.SentAt);
            Assert.Equal(message.CreatedLocal, decoded.CreatedLocal);
            Assert.Equal(message.Markdown, decoded.Markdown);
            Assert.Equal(attachments.Select(a => (a.Extension, Convert.ToHexString(a.Data.Span))), decoded.Attachments.Select(a => (a.Extension, Convert.ToHexString(a.Data.Span))));
            count++;
        }

        Assert.Equal(2, count);
    }

    [Fact]
    public void Responses_encode_to_the_shared_vectors_and_decode_back()
    {
        foreach (var v in Vectors.GetProperty("responses").EnumerateArray())
        {
            var response = new ShareResponse((ShareStatus)v.GetProperty("status").GetInt32(), v.GetProperty("notePath").GetString()!);
            var plaintext = response.Encode();
            Assert.Equal(v.GetProperty("plaintextHex").GetString(), Convert.ToHexStringLower(plaintext));
            var frame = ShareFrame.Seal(ShareFrameKind.Response, Key, Convert.FromHexString(v.GetProperty("nonceHex").GetString()!), plaintext);
            Assert.Equal(v.GetProperty("frameHex").GetString(), Convert.ToHexStringLower(frame));
            Assert.True(ShareResponse.TryDecode(plaintext, out var decoded));
            Assert.Equal(response, decoded);
        }
    }

    [Fact]
    public void A_frame_does_not_open_with_another_key_or_after_any_change()
    {
        var frame = ShareFrame.Seal(ShareFrameKind.Request, Key, "hello"u8);
        var header = frame.AsSpan(0, ShareFrame.HeaderLength).ToArray();
        var body = frame.AsSpan(ShareFrame.HeaderLength).ToArray();
        Assert.True(ShareFrame.TryOpen(header, body, Key, out var plaintext));
        Assert.Equal("hello"u8.ToArray(), plaintext);

        var otherKey = (byte[])Key.Clone();
        otherKey[0] ^= 1;
        Assert.False(ShareFrame.TryOpen(header, body, otherKey, out _));

        for (var i = 0; i < body.Length; i++)
        {
            var changed = (byte[])body.Clone();
            changed[i] ^= 0x80;
            Assert.False(ShareFrame.TryOpen(header, changed, Key, out _));
        }

        // The kind is authenticated: a request cannot be passed off as a response.
        var asResponse = (byte[])header.Clone();
        asResponse[4] = (byte)ShareFrameKind.Response;
        Assert.False(ShareFrame.TryOpen(asResponse, body, Key, out _));
        Assert.False(ShareFrame.TryOpen(header, body.AsSpan(0, body.Length - 1), Key, out _));
    }

    [Fact]
    public void Two_frames_of_the_same_message_differ()
    {
        Assert.NotEqual(ShareFrame.Seal(ShareFrameKind.Request, Key, "x"u8), ShareFrame.Seal(ShareFrameKind.Request, Key, "x"u8));
    }

    [Theory]
    [InlineData("574d5332" + "01" + "00000020")] // another magic
    [InlineData("574d5331" + "03" + "00000020")] // unknown kind
    [InlineData("574d5331" + "01" + "0000001b")] // shorter than nonce + tag
    [InlineData("574d5331" + "01" + "02000001")] // over the limit
    [InlineData("574d5331" + "01" + "ffffffff")] // negative
    [InlineData("574d5331" + "01" + "000020")] // short header
    public void Bad_headers_are_refused_before_anything_is_read(string hex)
    {
        Assert.False(ShareFrame.TryReadHeader(Convert.FromHexString(hex), out _, out _));
    }

    [Fact]
    public void Malformed_messages_are_refused()
    {
        var good = new ShareMessage(Guid.NewGuid(), DateTimeOffset.UnixEpoch, new DateTime(2026, 1, 1), "text", [new ShareAttachment("png", new byte[] { 1, 2, 3 })]).Encode();
        Assert.True(ShareMessage.TryDecode(good, out _));

        Assert.False(ShareMessage.TryDecode(Array.Empty<byte>(), out _));
        Assert.False(ShareMessage.TryDecode(good.AsMemory(0, good.Length - 1), out _)); // image cut short
        Assert.False(ShareMessage.TryDecode((byte[])[.. good, 0], out _)); // trailing byte
        Assert.False(ShareMessage.TryDecode(With(good, 0, 2), out _)); // another version
        Assert.False(ShareMessage.TryDecode(With(good, 33, 0x7f), out _)); // text longer than the message
        Assert.False(ShareMessage.TryDecode(With(good, 37, 0xff), out _)); // invalid UTF-8
        Assert.False(ShareMessage.TryDecode(With(good, 41, 21), out _)); // more images than allowed

        // Only image types are stored: nothing executable lands in the notes folder.
        var exe = new ShareMessage(Guid.NewGuid(), DateTimeOffset.UnixEpoch, new DateTime(2026, 1, 1), "text", [new ShareAttachment("exe", new byte[] { 1 })]).Encode();
        Assert.False(ShareMessage.TryDecode(exe, out _));
        var traversal = new ShareMessage(Guid.NewGuid(), DateTimeOffset.UnixEpoch, new DateTime(2026, 1, 1), "text", [new ShareAttachment("/../x.png", new byte[] { 1 })]).Encode();
        Assert.False(ShareMessage.TryDecode(traversal, out _));
    }

    private static byte[] With(byte[] source, int index, byte value)
    {
        var copy = (byte[])source.Clone();
        copy[index] = value;
        return copy;
    }
}
