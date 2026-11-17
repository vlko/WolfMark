using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace WolfMark.Sync.Share;

/// <summary>
/// The local-network end of sharing (ADR 0017): accepts a connection, reads one request frame, answers with one
/// response frame and closes. Whoever does not have the key gets no answer at all.
/// </summary>
public sealed partial class ShareListener : IAsyncDisposable
{
    private const int MaxConnections = 4;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(60);

    private readonly ShareReceiver _receiver;
    private readonly byte[] _key;
    private readonly TcpListener _listener;
    private readonly ILogger<ShareListener> _logger;
    private readonly SemaphoreSlim _slots = new(MaxConnections, MaxConnections);
    private readonly CancellationTokenSource _stop = new();
    private Task _loop = Task.CompletedTask;
    private bool _started;

    public ShareListener(ShareReceiver receiver, ReadOnlySpan<byte> key, IPAddress address, int port, ILogger<ShareListener> logger)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(key.Length, ShareFrame.KeyLength);
        _receiver = receiver;
        _key = key.ToArray();
        _listener = new TcpListener(address, port);
        _logger = logger;
    }

    /// <summary>The port in use – the one asked for, or the one the system chose for port 0.</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Binds the port. Throws <see cref="SocketException"/> when it is taken.</summary>
    public void Start()
    {
        _listener.Start();
        _started = true;
        LogListening(Port);
    }

    /// <summary>Serves connections until cancelled or disposed; call after <see cref="Start"/>.</summary>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        _loop = AcceptLoopAsync(cancellationToken);
        return _loop;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        var ct = linked.Token;
        try
        {
            while (true)
            {
                // Wait for a free slot before accepting: more callers than slots queue in the system's backlog.
                await _slots.WaitAsync(ct).ConfigureAwait(false);
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                    _slots.Release();
                    throw;
                }

                _ = ServeAsync(client, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using (client)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(ConnectionTimeout);
                var stream = client.GetStream();
                var response = await HandleAsync(stream, timeout.Token).ConfigureAwait(false);
                if (response is not null)
                {
                    await stream.WriteAsync(response, timeout.Token).ConfigureAwait(false);
                    await stream.FlushAsync(timeout.Token).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or EndOfStreamException)
        {
            // A caller that went away, stalled or sent half a frame: nothing to answer.
            LogDropped(ex.GetType().Name);
        }
        catch (Exception ex)
        {
            LogFailed(ex);
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <returns>The sealed response, or null to close without a word (not a frame, or not our key).</returns>
    private async Task<byte[]?> HandleAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[ShareFrame.HeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if (!ShareFrame.TryReadHeader(header, out var kind, out var bodyLength) || kind != ShareFrameKind.Request)
        {
            return null;
        }

        var body = await ReadBodyAsync(stream, bodyLength, cancellationToken).ConfigureAwait(false);
        if (!ShareFrame.TryOpen(header, body, _key, out var plaintext))
        {
            LogWrongKey();
            return null;
        }

        var response = ShareMessage.TryDecode(plaintext, out var message)
            ? await _receiver.ReceiveAsync(message, cancellationToken).ConfigureAwait(false)
            : new ShareResponse(ShareStatus.Failed, string.Empty);
        return ShareFrame.Seal(ShareFrameKind.Response, _key, response.Encode());
    }

    /// <summary>
    /// Reads the body into a buffer that grows with what actually arrives: a stranger announcing 32 MB and sending
    /// nothing costs a few kilobytes, not 32 MB.
    /// </summary>
    private static async Task<byte[]> ReadBodyAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(length, 64 * 1024)];
        var read = 0;
        while (read < length)
        {
            if (read == buffer.Length)
            {
                Array.Resize(ref buffer, (int)Math.Min(length, buffer.Length * 2L));
            }

            var n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                throw new EndOfStreamException();
            }

            read += n;
        }

        return buffer;
    }

    /// <summary>Stops accepting and waits for the shares being written – nothing is written after this returns.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        for (var i = 0; i < MaxConnections; i++)
        {
            await _slots.WaitAsync().ConfigureAwait(false);
        }

        if (_started)
        {
            _listener.Stop();
        }

        _listener.Dispose();
        _stop.Dispose();
        _slots.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Share: listening on port {Port}")]
    private partial void LogListening(int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Share: a frame that does not open with the paired key was ignored")]
    private partial void LogWrongKey();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Share: connection dropped ({Reason})")]
    private partial void LogDropped(string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Share: connection failed")]
    private partial void LogFailed(Exception ex);
}
