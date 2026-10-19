using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace WolfMark.Desktop.Infrastructure;

/// <summary>
/// One window per data folder (the Windows app uses AppInstance redirection): the first process holds an
/// exclusive lock file and listens on a local socket; a later launch pokes that socket – the first window comes
/// to the front – and exits.
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

    /// <summary>Raised on a thread-pool thread when another launch asked for the window.</summary>
    public event EventHandler? Activated;

    /// <summary>Null when another instance owns <paramref name="directory"/> (it has been asked to show itself).</summary>
    public static SingleInstance? TryAcquire(string directory)
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
            Poke(socketPath);
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
                Activated?.Invoke(this, EventArgs.Empty);
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

    private static void Poke(string socketPath)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
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
