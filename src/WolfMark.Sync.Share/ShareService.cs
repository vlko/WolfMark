using System.Net;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using WolfMark.Storage;

namespace WolfMark.Sync.Share;

/// <param name="DeviceId">Names this computer's file in <c>.wolfmark/share</c>.</param>
/// <param name="Key">32 bytes shared with the phone through that file.</param>
public sealed record ShareOptions(string DeviceId, string DeviceName, int Port, byte[] Key);

/// <summary>
/// Receiving shares while the app runs: the listener, and this computer's addresses kept current in the notes
/// folder for the phone to find (rewritten when the network changes).
/// </summary>
public sealed partial class ShareService : IAsyncDisposable
{
    private static readonly TimeSpan NetworkSettleDelay = TimeSpan.FromSeconds(3);

    private readonly string _root;
    private readonly ShareOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ShareService> _logger;
    private readonly ShareListener _listener;
    private readonly SemaphoreSlim _networkChanged = new(0);
    private readonly Func<List<string>> _addresses;

    public ShareService(DirectoryEngine engine, ShareOptions options, TimeProvider time, ILoggerFactory loggers, Func<List<string>>? addresses = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggers);
        _root = engine.Root;
        _options = options;
        _time = time;
        _logger = loggers.CreateLogger<ShareService>();
        _addresses = addresses ?? ShareDeviceFile.LocalAddresses;
        var receiver = new ShareReceiver(engine, time, loggers.CreateLogger<ShareReceiver>());
        receiver.Received += (_, path) => Received?.Invoke(this, path);
        _listener = new ShareListener(receiver, options.Key, IPAddress.Any, options.Port, loggers.CreateLogger<ShareListener>());
    }

    public int Port => _listener.Port;

    /// <summary>A share arrived over the network and is a note now. Background thread.</summary>
    public event EventHandler<WolfMark.Core.Notes.NotePath>? Received;

    /// <summary>Runs until cancelled. A taken port is logged and ends the run – the phone then uses OneDrive.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!System.Security.Cryptography.AesGcm.IsSupported)
        {
            // An old system without AES-GCM: say so once instead of failing on every share.
            LogNoCipher();
            return;
        }

        try
        {
            _listener.Start();
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            LogPortUnavailable(ex, _options.Port);
            return;
        }

        NetworkAddressChangedEventHandler onChanged = (_, _) =>
        {
            if (_networkChanged.CurrentCount == 0)
            {
                _networkChanged.Release();
            }
        };
        NetworkChange.NetworkAddressChanged += onChanged;
        try
        {
            await PublishAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(_listener.RunAsync(cancellationToken), RepublishLoopAsync(cancellationToken)).ConfigureAwait(false);
        }
        finally
        {
            NetworkChange.NetworkAddressChanged -= onChanged;
        }
    }

    private async Task RepublishLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _networkChanged.WaitAsync(cancellationToken).ConfigureAwait(false);
                // Joining a network raises several events and the address comes last.
                await Task.Delay(NetworkSettleDelay, _time, cancellationToken).ConfigureAwait(false);
                while (_networkChanged.CurrentCount > 0)
                {
                    await _networkChanged.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                await PublishAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        var info = new ShareDeviceInfo
        {
            Id = _options.DeviceId,
            Name = _options.DeviceName,
            Port = Port,
            Addresses = _addresses(),
            Key = Convert.ToBase64String(_options.Key),
        };
        try
        {
            if (await ShareDeviceFile.PublishAsync(_root, info, cancellationToken).ConfigureAwait(false))
            {
                LogPublished(string.Join(", ", info.Addresses), info.Port);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sharing through OneDrive still works; the phone just does not find this computer on the network.
            LogPublishFailed(ex);
        }
    }

    public ValueTask DisposeAsync() => _listener.DisposeAsync();

    [LoggerMessage(Level = LogLevel.Error, Message = "Share: this system has no AES-GCM – shares arrive through OneDrive only")]
    private partial void LogNoCipher();

    [LoggerMessage(Level = LogLevel.Error, Message = "Share: port {Port} cannot be used – shares arrive through OneDrive only")]
    private partial void LogPortUnavailable(Exception ex, int port);

    [LoggerMessage(Level = LogLevel.Information, Message = "Share: this computer is announced to the phone at {Addresses}, port {Port}")]
    private partial void LogPublished(string addresses, int port);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Share: the file that announces this computer to the phone could not be written")]
    private partial void LogPublishFailed(Exception ex);
}
