using Microsoft.Extensions.Logging;

namespace Zapisnik.Sync.ClaudeCode;

/// <summary>Runs the sync at start, periodically and (optionally) on transcript changes (M7-05).</summary>
public sealed partial class ClaudeCodeSyncScheduler(ClaudeCodeSync sync, TimeSpan interval, bool watch, ILogger<ClaudeCodeSyncScheduler> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _trigger = new(0, 1);
    private FileSystemWatcher? _watcher;

    public event EventHandler<SyncResult>? Synced;

    public async Task RunAsync(CancellationToken ct)
    {
        if (watch && Directory.Exists(sync.ProjectsDirectory))
        {
            _watcher = new FileSystemWatcher(sync.ProjectsDirectory, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            _watcher.Changed += (_, _) => Trigger();
            _watcher.Created += (_, _) => Trigger();
            _watcher.EnableRaisingEvents = true;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await sync.SyncAsync(ct).ConfigureAwait(false);
                Synced?.Invoke(this, result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(ex);
            }

            await _trigger.WaitAsync(interval, ct).ConfigureAwait(false);
            if (watch)
            {
                // Coalesce bursts of transcript writes.
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
        }
    }

    public void Trigger()
    {
        if (_trigger.CurrentCount == 0)
        {
            try
            {
                _trigger.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _watcher?.Dispose();
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claude Code sync failed")]
    private partial void LogFailed(Exception ex);
}
