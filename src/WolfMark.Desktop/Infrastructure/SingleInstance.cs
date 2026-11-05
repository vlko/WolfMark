using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WolfMark.Desktop.Infrastructure;

/// <summary>
/// One instance per data folder (the Windows app uses AppInstance redirection): the first process holds an
/// exclusive lock file and listens on a local socket; a later launch sends that socket the file it was started
/// with (or nothing) – the first instance opens it or comes to the front – and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly FileStream _lock;
    private readonly Socket _listener;
    private readonly string _socketPath;

    private SingleInstance(FileStream fileLock, Socket listener, string socketPath)
    {
        _lock = fileLock;
        _listener = listener;
        _socketPath = socketPath;
        _ = AcceptLoopAsync();
    }

    /// <summary>Raised on a thread-pool thread when another launch asked for the window; the file it was started with, or null.</summary>
    public event EventHandler<string?>? Activated;

    /// <summary>
    /// Null when another instance owns <paramref name="directory"/> (it has been handed <paramref name="file"/>, or
    /// asked to show itself).
    /// </summary>
    public static SingleInstance? TryAcquire(string directory, string? file)
    {
        Directory.CreateDirectory(directory);
        var socketPath = SocketPath(directory);
        FileStream fileLock;
        try
        {
            fileLock = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            Poke(socketPath, file);
            return null;
        }

        File.Delete(socketPath); // left over from a crash – the lock proves nobody listens
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(4);
        return new SingleInstance(fileLock, listener, socketPath);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _lock.Dispose();
        try
        {
            File.Delete(_socketPath);
        }
        catch (IOException)
        {
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            try
            {
                using var client = await _listener.AcceptAsync();
                Activated?.Invoke(this, await ReadFileAsync(client));
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }
        }
    }

    /// <summary>The path a launch sent (UTF-8 up to the end of the stream), null for none or when it took too long.</summary>
    private static async Task<string?> ReadFileAsync(Socket client)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var stream = new NetworkStream(client, ownsSocket: false);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, timeout.Token);
            var path = Encoding.UTF8.GetString(buffer.ToArray()).Trim();
            return path.Length > 0 ? path : null;
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
        {
            return null;
        }
    }

    private static void Poke(string socketPath, string? file)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            if (file is not null)
            {
                socket.Send(Encoding.UTF8.GetBytes(file));
            }

            socket.Shutdown(SocketShutdown.Send);
        }
        catch (SocketException)
        {
            // the owner is starting or ending – nothing to bring to the front
        }
    }

    /// <summary>Unix socket paths are limited to ~100 bytes; long data folders get a hashed name in the temp folder.</summary>
    private static string SocketPath(string directory)
    {
        var path = Path.Combine(directory, "instance.sock");
        if (Encoding.UTF8.GetByteCount(path) < 100)
        {
            return path;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(directory)), 0, 8);
        return Path.Combine(Path.GetTempPath(), $"wolfmark-{hash}.sock");
    }
}
