using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;

namespace WolfMark.Sync.Share.Tests;

/// <summary>End to end over loopback: what the phone sends, and what a stranger on the network gets.</summary>
public sealed class ShareListenerTests : IAsyncLifetime
{
    private readonly ShareFixture _f = new();
    private readonly CancellationTokenSource _stop = new();
    private ShareListener _listener = null!;
    private Task _run = Task.CompletedTask;

    public ValueTask InitializeAsync()
    {
        _listener = new ShareListener(_f.Receiver, ShareFixture.Key, IPAddress.Loopback, 0, NullLogger<ShareListener>.Instance);
        _listener.Start();
        _run = _listener.RunAsync(_stop.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _listener.DisposeAsync();
        await _run;
        _stop.Dispose();
        _f.Dispose();
    }

    /// <summary>
    /// Sends raw bytes and returns everything the listener answers before it closes the connection. A listener that
    /// hangs up without a word may do so before our end of the conversation arrives; the caller then sees a reset
    /// instead of a clean end (always on Windows, now and then elsewhere) – either way it got no answer.
    /// </summary>
    private async Task<byte[]> ExchangeAsync(byte[] request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _listener.Port);
        var stream = client.GetStream();
        using var answer = new MemoryStream();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await stream.WriteAsync(request);
            client.Client.Shutdown(SocketShutdown.Send);
            await stream.CopyToAsync(answer, timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or SocketException && IsReset(ex))
        {
        }

        return answer.ToArray();
    }

    private static bool IsReset(Exception ex) =>
        (ex as SocketException ?? ex.InnerException as SocketException)?.SocketErrorCode
            is SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown;

    private static ShareResponse Open(byte[] frame, byte[] key)
    {
        Assert.True(ShareFrame.TryReadHeader(frame.AsSpan(0, ShareFrame.HeaderLength), out var kind, out _));
        Assert.Equal(ShareFrameKind.Response, kind);
        Assert.True(ShareFrame.TryOpen(frame.AsSpan(0, ShareFrame.HeaderLength), frame.AsSpan(ShareFrame.HeaderLength), key, out var plaintext));
        Assert.True(ShareResponse.TryDecode(plaintext, out var response));
        return response;
    }

    [Fact]
    public async Task A_share_sent_over_the_network_becomes_a_note_and_a_retry_does_not_duplicate_it()
    {
        var message = _f.Message("# Photo\n\n![](wolfmark-attachment:0)\n", attachments: new ShareAttachment("png", new byte[300_000]));

        var first = Open(await ExchangeAsync(ShareFrame.Seal(ShareFrameKind.Request, ShareFixture.Key, message.Encode())), ShareFixture.Key);
        var retry = Open(await ExchangeAsync(ShareFrame.Seal(ShareFrameKind.Request, ShareFixture.Key, message.Encode())), ShareFixture.Key);

        Assert.Equal(new ShareResponse(ShareStatus.Created, "2026/11/17/Share_1403.md"), first);
        Assert.Equal(new ShareResponse(ShareStatus.Duplicate, "2026/11/17/Share_1403.md"), retry);
        Assert.Equal("# Photo\n\n![](assets/1403-01.png)\n", _f.Read("2026/11/17/Share_1403.md"));
        Assert.Equal(300_000, new FileInfo(Path.Combine(_f.Root, "2026/11/17/assets/1403-01.png")).Length);
    }

    [Fact]
    public async Task Without_the_key_there_is_no_answer_and_no_note()
    {
        var otherKey = new byte[32];
        var frame = ShareFrame.Seal(ShareFrameKind.Request, otherKey, _f.Message("intruder").Encode());

        Assert.Empty(await ExchangeAsync(frame));
        Assert.Empty(await ExchangeAsync("GET / HTTP/1.1\r\nHost: x\r\n\r\n"u8.ToArray()));
        Assert.Empty(await ExchangeAsync([]));
        Assert.Empty(_f.Notes());
    }

    [Fact]
    public async Task A_response_frame_or_an_unreadable_message_writes_nothing()
    {
        // Our own answer played back at us.
        var reflected = ShareFrame.Seal(ShareFrameKind.Response, ShareFixture.Key, _f.Message("x").Encode());
        Assert.Empty(await ExchangeAsync(reflected));

        var garbage = Open(await ExchangeAsync(ShareFrame.Seal(ShareFrameKind.Request, ShareFixture.Key, new byte[50])), ShareFixture.Key);
        Assert.Equal(ShareStatus.Failed, garbage.Status);
        Assert.Empty(_f.Notes());
    }

    [Fact]
    public async Task A_caller_that_announces_a_frame_and_goes_silent_does_not_block_the_others()
    {
        var silent = new List<TcpClient>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, _listener.Port);
                await client.GetStream().WriteAsync(Convert.FromHexString("574d5331" + "01" + "02000000")); // "32 MB follow"
                silent.Add(client);
            }

            var response = Open(await ExchangeAsync(ShareFrame.Seal(ShareFrameKind.Request, ShareFixture.Key, _f.Message("still served").Encode())), ShareFixture.Key);
            Assert.Equal(ShareStatus.Created, response.Status);
        }
        finally
        {
            silent.ForEach(c => c.Dispose());
        }
    }
}
