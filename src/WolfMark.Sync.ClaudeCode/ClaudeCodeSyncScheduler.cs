using Microsoft.Extensions.Logging;

namespace WolfMark.Sync.ClaudeCode;

/// <summary>
/// Runs the sync at start, periodically and (optionally) on transcript changes (M7-05). Transcript writes come in
/// bursts while Claude Code works, so a watcher wake-up waits <see cref="CoalesceDelay"/> and covers every write
/// in that window with one run; a manual <see cref="Trigger"/> ("Sync now") runs right away.
/// </summary>
public sealed partial class ClaudeCodeSyncScheduler : IAsyncDisposable
{
    internal static readonly TimeSpan CoalesceDelay = TimeSpan.FromSeconds(5);

    private readonly Func<CancellationToken, Task<SyncResult>> _sync;
    private readonly string _projectsDirectory;
    private readonly TimeSpan _interval;
    private readonly bool _watch;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private int _manual;
    private FileSystemWatcher? _watcher;

    public ClaudeCodeSyncScheduler(ClaudeCodeSync sync, TimeSpan interval, bool watch, ILogger<ClaudeCodeSyncScheduler> logger)
        : this(sync.SyncAsync, sync.ProjectsDirectory, interval, watch, TimeProvider.System, logger)
    {
    }

    internal ClaudeCodeSyncScheduler(
        Func<CancellationToken, Task<SyncResult>> sync,
        string projectsDirectory,
        TimeSpan interval,
        bool watch,
        TimeProvider time,
        ILogger logger)
    {
        _sync = sync;
        _projectsDirectory = projectsDirectory;
        _interval = interval;
        _watch = watch;
        _time = time;
        _logger = logger;
    }

    public event EventHandler<SyncResult>? Synced;

    /// <summary>Raised (on a background thread) when a sync run failed.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>True while the transcript folder is being watched.</summary>
    internal bool IsWatching => _watcher is not null;

    public async Task RunAsync(CancellationToken ct)
    {
        if (_watch && Directory.Exists(_projectsDirectory))
        {
            _watcher = new FileSystemWatcher(_projectsDirectory, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _watcher.Changed += (_, _) => Wake();
            _watcher.Created += (_, _) => Wake();
            _watcher.EnableRaisingEvents = true;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _sync(ct).ConfigureAwait(false);
                Synced?.Invoke(this, result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
                Failed?.Invoke(this, ex);
            }

            var woken = await WaitForWakeAsync(ct).ConfigureAwait(false);
            if (woken && Interlocked.Exchange(ref _manual, 0) == 0)
            {
                await Task.Delay(CoalesceDelay, _time, ct).ConfigureAwait(false);
                // Writes during the window are covered by the run below; a manual trigger meanwhile is too.
                await _wake.WaitAsync(0, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Exchange(ref _manual, 0);
            }
        }
    }

    /// <summary>Sync now (status bar, Settings) – no coalescing delay.</summary>
    public void Trigger()
    {
        Interlocked.Exchange(ref _manual, 1);
        Wake();
    }

    private void Wake()
    {
        try
        {
            _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already pending – one run covers both.
        }
    }

    /// <summary>True when woken by a trigger, false when the interval elapsed.</summary>
    private async Task<bool> WaitForWakeAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var wake = _wake.WaitAsync(cts.Token);
        var elapsed = Task.Delay(_interval, _time, cts.Token);
        await Task.WhenAny(wake, elapsed).ConfigureAwait(false);
        await cts.CancelAsync().ConfigureAwait(false); // the losing wait must not swallow a later wake-up
        try
        {
            await wake.ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        // _wake stays undisposed: a late "Sync now" click during shutdown must not throw.
        _watcher?.Dispose();
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claude Code sync failed")]
    private partial void LogFailed(Exception ex);
}
