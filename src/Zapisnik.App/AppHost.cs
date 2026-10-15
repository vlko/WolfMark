using Microsoft.Extensions.Logging;
using Zapisnik.Core.Events;
using Zapisnik.Index.Database;
using Zapisnik.Index.Embeddings;
using Zapisnik.Index.Indexing;
using Zapisnik.Index.Queries;
using Zapisnik.Storage;
using Zapisnik.Storage.Diagnostics;
using Zapisnik.Storage.Settings;
using Zapisnik.Storage.Tags;
using Zapisnik.Storage.Trash;
using Zapisnik.Storage.Watching;
using Zapisnik.Sync.ClaudeCode;

namespace Zapisnik.App;

/// <summary>
/// Composition root (M0-03). Plain constructor wiring – no container, no reflection, fast start.
/// Everything that touches disk or the database is started from <see cref="StartBackground"/>
/// after the window is visible.
/// </summary>
internal sealed partial class AppHost : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _databaseReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILogger _logger;
    private NoteWatcher? _watcher;
    private ClaudeCodeSyncScheduler? _claudeScheduler;

    private AppHost(LocalPaths paths, LocalSettingsStore settings, FileLoggerProvider fileLogger, ILoggerFactory loggers)
    {
        Paths = paths;
        Settings = settings;
        FileLogger = fileLogger;
        Loggers = loggers;
        _logger = loggers.CreateLogger<AppHost>();
        Time = TimeProvider.System;
        Background = new BackgroundRunner(loggers.CreateLogger<BackgroundRunner>());

        Root = settings.ResolveRoot();
        Hub = new NoteEventHub();
        SelfWrites = new SelfWriteRegistry(Time);
        IRecycleBin recycleBin = OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000) ? new WindowsRecycleBin() : new FolderRecycleBin(Root);
        Engine = new DirectoryEngine(Root, recycleBin, Hub, SelfWrites, loggers.CreateLogger<DirectoryEngine>());
        Tags = new TagStore(Root, SelfWrites, loggers.CreateLogger<TagStore>());
        Db = new IndexDatabase(paths.IndexDatabase, loggers.CreateLogger<IndexDatabase>());
        Indexer = new NoteIndexer(Db, Engine, loggers.CreateLogger<NoteIndexer>());
        Calendar = new CalendarService(Db);
        History = new HistoryService(Db);
        Search = new SearchService(Db, EmbeddingProvider);
    }

    public LocalPaths Paths { get; }

    public LocalSettingsStore Settings { get; }

    public FileLoggerProvider FileLogger { get; }

    public ILoggerFactory Loggers { get; }

    public TimeProvider Time { get; }

    public BackgroundRunner Background { get; }

    public string Root { get; }

    public NoteEventHub Hub { get; }

    public SelfWriteRegistry SelfWrites { get; }

    public DirectoryEngine Engine { get; }

    public TagStore Tags { get; }

    public IndexDatabase Db { get; }

    public NoteIndexer Indexer { get; }

    public CalendarService Calendar { get; }

    public HistoryService History { get; }

    public SearchService Search { get; }

    public ClaudeCodeSync? ClaudeSync { get; private set; }

    /// <summary>No local embedding model ships yet (M2-05 is open) – semantic search stays off.</summary>
    public IEmbeddingProvider? EmbeddingProvider => null;

    public CancellationToken ShutdownToken => _shutdown.Token;

    /// <summary>Completes when the index database is open and readable.</summary>
    public Task DatabaseReady => _databaseReady.Task;

    public event EventHandler<IndexProgress>? IndexProgressChanged;

    public event EventHandler<SyncResult>? ClaudeSynced;

    public static Task<AppHost> CreateAsync() => Task.Run(async () =>
    {
        var paths = LocalPaths.Default;
        var fileLogger = new FileLoggerProvider(paths.LogsDirectory);
        var loggers = LoggerFactory.Create(b => b.AddProvider(fileLogger).SetMinimumLevel(LogLevel.Information));
        var settings = new LocalSettingsStore(paths, loggers.CreateLogger<LocalSettingsStore>());
        await settings.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        return new AppHost(paths, settings, fileLogger, loggers);
    });

    /// <summary>Starts index, watcher and sync in the background; never blocks the caller.</summary>
    public void StartBackground()
    {
        var ct = _shutdown.Token;
        _ = Background.Run("startup", async token =>
        {
            Directory.CreateDirectory(Root);
            await Tags.LoadAsync(token).ConfigureAwait(false);
            await Db.OpenAsync(token).ConfigureAwait(false);
            _databaseReady.TrySetResult();

            // Subscribe before scanning so no change between scan and loop is lost.
            var events = Hub.Subscribe();
            _watcher = new NoteWatcher(Engine, Hub, Time, Loggers.CreateLogger<NoteWatcher>());
            _watcher.Start();

            var progress = new Progress<IndexProgress>(p => IndexProgressChanged?.Invoke(this, p));
            await Indexer.FullScanAsync(progress, token).ConfigureAwait(false);
            IndexProgressChanged?.Invoke(this, new IndexProgress("done", 0, 0));

            _ = Background.Run("index-events", t => Indexer.RunAsync(events, t), token);
            _ = Background.Run("placeholders", t => Indexer.IndexPendingContentAsync(TimeSpan.FromSeconds(2), t), token);
            StartClaudeSync(token);

            if (EmbeddingProvider is { } provider)
            {
                var embedder = new EmbeddingIndexer(Db, provider, Indexer, Loggers.CreateLogger<EmbeddingIndexer>());
                _ = Background.Run("embeddings", embedder.RunAsync, token);
            }
        }, ct);

        Db.Reset += (_, _) => _ = Background.Run("rescan-after-reset", t => Indexer.FullScanAsync(null, t), ct);
    }

    private void StartClaudeSync(CancellationToken ct)
    {
        var s = Settings.Current.ClaudeCode;
        if (!s.Enabled)
        {
            return;
        }

        ClaudeSync = new ClaudeCodeSync(
            Engine,
            Db,
            new ClaudeCodeSyncOptions
            {
                ClaudeDirectory = ClaudeDirectory,
                ExcludedProjects = s.ExcludedProjects.ToArray(),
                RedactSecrets = s.RedactSecrets,
            },
            Time,
            Loggers.CreateLogger<ClaudeCodeSync>());
        _claudeScheduler = new ClaudeCodeSyncScheduler(
            ClaudeSync,
            TimeSpan.FromMinutes(Math.Max(1, s.IntervalMinutes)),
            s.WatchInRealTime,
            Loggers.CreateLogger<ClaudeCodeSyncScheduler>());
        _claudeScheduler.Synced += (_, r) => ClaudeSynced?.Invoke(this, r);
        _ = Background.Run("claude-sync", _claudeScheduler.RunAsync, ct);
    }

    public string ClaudeDirectory => string.IsNullOrWhiteSpace(Settings.Current.ClaudeCode.ClaudeDirectory)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
        : Settings.Current.ClaudeCode.ClaudeDirectory!;

    public void TriggerClaudeSync() => _claudeScheduler?.Trigger();

    /// <summary>Rebuild index (M2-07): wipe and rescan; progress is reported via <see cref="IndexProgressChanged"/>.</summary>
    public Task RebuildIndexAsync() => Task.Run(async () =>
    {
        await DatabaseReady.ConfigureAwait(false);
        await Db.ResetAsync(ShutdownToken).ConfigureAwait(false);
    });

    public Task SaveSettingsAsync() => Task.Run(() => Settings.SaveAsync(CancellationToken.None));

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        if (_watcher is not null)
        {
            await _watcher.DisposeAsync().ConfigureAwait(false);
        }

        if (_claudeScheduler is not null)
        {
            await _claudeScheduler.DisposeAsync().ConfigureAwait(false);
        }

        Hub.Complete();
        await Db.DisposeAsync().ConfigureAwait(false);
        LogShutdown();
        Loggers.Dispose();
        await FileLogger.FlushAsync().ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Shut down")]
    private partial void LogShutdown();
}
