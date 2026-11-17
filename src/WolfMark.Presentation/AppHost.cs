using Microsoft.Extensions.Logging;
using WolfMark.Core.Events;
using WolfMark.Index.Database;
using WolfMark.Index.Embeddings;
using WolfMark.Index.Indexing;
using WolfMark.Index.Queries;
using WolfMark.Storage;
using WolfMark.Storage.Diagnostics;
using WolfMark.Storage.Settings;
using WolfMark.Storage.Tags;
using WolfMark.Storage.Trash;
using WolfMark.Storage.Watching;
using WolfMark.Sync.ClaudeCode;

namespace WolfMark.App;

/// <summary>
/// Composition root (M0-03). Plain constructor wiring – no container, no reflection, fast start.
/// Everything that touches disk or the database is started from <see cref="StartBackground"/>
/// after the window is visible.
/// </summary>
internal sealed partial class AppHost : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _databaseReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstClaudeSync = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILogger _logger;
    private Exception? _startupError;
    private NoteWatcher? _watcher;
    private ClaudeCodeSyncScheduler? _claudeScheduler;

    private AppHost(LocalPaths paths, LocalSettingsStore settings, FileLoggerProvider fileLogger, ILoggerFactory loggers, bool isDemo)
    {
        Paths = paths;
        Settings = settings;
        FileLogger = fileLogger;
        Loggers = loggers;
        _logger = loggers.CreateLogger<AppHost>();
        Infrastructure.Debouncer.ErrorHandler = LogDebouncedFailed;
        Time = TimeProvider.System;
        Background = new BackgroundRunner(loggers.CreateLogger<BackgroundRunner>());

        Root = settings.ResolveRoot();
        Hub = new NoteEventHub();
        SelfWrites = new SelfWriteRegistry(Time);
        // One trash on every system (ADR 0016): a folder with the notes, listed and restored in Settings → Trash.
        Trash = new NoteTrash(Root, Time);
        // What leaves the trash goes to the Windows Recycle Bin where there is one for these notes – not in portable
        // mode (nothing is written to the computer) nor in a demo (its notes must not land in the user's bin).
        SystemBin = OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000) && paths.PortableDataDirectory is null && !isDemo
            && WindowsRecycleBin.CanRecycle(Root)
            ? new WindowsRecycleBin()
            : null;
        Engine = new DirectoryEngine(Root, Trash, Hub, SelfWrites, loggers.CreateLogger<DirectoryEngine>());
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

    /// <summary>Where deleted notes go, on every system.</summary>
    public NoteTrash Trash { get; }

    /// <summary>
    /// Takes what is removed from the <see cref="Trash"/> (emptying it, the retention): the system's own recycle bin.
    /// Null where there is none for these notes – then removing from the trash is final.
    /// </summary>
    public IRecycleBin? SystemBin { get; }

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

    /// <summary>
    /// Completes when the index database is open and readable. It stays pending when the start failed
    /// (<see cref="StartupError"/>): history, calendar and search have nothing to show then.
    /// </summary>
    public Task DatabaseReady => _databaseReady.Task;

    /// <summary>Why notes, tags or the index could not be opened at start; null while all is well.</summary>
    public Exception? StartupError => Volatile.Read(ref _startupError);

    /// <summary>Raised (on a background thread) when <see cref="StartupError"/> is set.</summary>
    public event EventHandler<Exception>? StartupFailed;

    /// <summary>
    /// All tags for the pickers and Settings → Tags: the ones in note file names with their counts (most used first),
    /// then tags known only from tags.json (added in Settings, no notes yet) with 0, by name.
    /// </summary>
    public async Task<IReadOnlyList<TagUsage>> GetTagListAsync(CancellationToken ct)
    {
        await DatabaseReady.ConfigureAwait(false);
        var used = await Calendar.GetTagUsageAsync(null, null, ct).ConfigureAwait(false);
        var seen = new HashSet<string>(used.Select(u => u.Tag), Core.Tags.TagName.Comparer);
        var unused = Tags.DefinedTags.Where(seen.Add).Order(StringComparer.CurrentCultureIgnoreCase).Select(t => new TagUsage(t, 0));
        return [.. used, .. unused];
    }

    public event EventHandler<IndexProgress>? IndexProgressChanged;

    public event EventHandler<SyncResult>? ClaudeSynced;

    public event EventHandler<Exception>? ClaudeSyncFailed;

    /// <summary>
    /// Completes after the first Claude Code import of this run, successful or not (never when the sync is off).
    /// Unlike the events it cannot be missed by someone who starts listening after a fast start.
    /// </summary>
    public Task FirstClaudeSync => _firstClaudeSync.Task;

    /// <param name="freshDemo">
    /// Screenshots: the demo is generated anew, so the images always show today – an older demo folder would show an
    /// empty today and last week's notes.
    /// </param>
    public static Task<AppHost> CreateAsync(string? demoDirectory = null, bool freshDemo = false) => Task.Run(async () =>
    {
        var paths = demoDirectory is null ? LocalPaths.Default : new LocalPaths(Path.Combine(demoDirectory, "local"));
        if (demoDirectory is not null && freshDemo)
        {
            ResetDemo(demoDirectory, paths);
        }

        var fileLogger = new FileLoggerProvider(paths.LogsDirectory);
        var loggers = LoggerFactory.Create(b => b.AddProvider(fileLogger).SetMinimumLevel(LogLevel.Information));
        var settings = new LocalSettingsStore(paths, loggers.CreateLogger<LocalSettingsStore>());
        await settings.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        if (demoDirectory is not null)
        {
            await PrepareDemoAsync(demoDirectory, settings).ConfigureAwait(false);
        }

        return new AppHost(paths, settings, fileLogger, loggers, isDemo: demoDirectory is not null);
    });

    /// <summary>
    /// Removes the generated demo content – only the demo's own subfolders (notes, Claude Code transcripts, the
    /// sample project) and its index and settings; never anything outside <paramref name="directory"/>.
    /// </summary>
    private static void ResetDemo(string directory, LocalPaths paths)
    {
        foreach (var folder in new[] { "notes", "claude", "projects" })
        {
            var path = Path.Combine(directory, folder);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        foreach (var file in new[] { paths.IndexDatabase, paths.IndexDatabase + "-wal", paths.IndexDatabase + "-shm", paths.SettingsFile })
        {
            // File.Delete tolerates a missing file but not a missing folder – a brand-new demo has no local/ yet.
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>Demo mode: everything inside <paramref name="directory"/>, English UI, seeded on first run.</summary>
    private static async Task PrepareDemoAsync(string directory, LocalSettingsStore settings)
    {
        var notes = Path.Combine(directory, "notes");
        var claude = Path.Combine(directory, "claude");
        await WolfMark.Storage.Demo.DemoDataSeeder.SeedAsync(notes, claude, DateTime.Now, CancellationToken.None).ConfigureAwait(false);

        var s = settings.Current;
        s.RootDirectory = notes;
        s.Language = "en-US";
        s.CloseToTray = false;
        s.HistoryPaneHeight = 250; // more room for the editor in the screenshots
        s.ClaudeCode.Enabled = true;
        s.ClaudeCode.ClaudeDirectory = claude;
        s.ClaudeCode.RedactSecrets = true;
        // Start on today's work note like a real user on their last one (M4-09 measures opening it), not on a new note.
        s.LastNotePath ??= TodaysWorkNote(notes);
        // A README outside the notes, opened an hour ago: an external file in the history.
        var external = await WolfMark.Storage.Demo.DemoDataSeeder.WriteExternalFileAsync(Path.Combine(directory, "projects", "billing-api"), CancellationToken.None).ConfigureAwait(false);
        if (s.ExternalFiles.Count == 0)
        {
            WolfMark.Core.Settings.ExternalFileHistory.Touch(s.ExternalFiles, external, DateTime.UtcNow.AddMinutes(-70));
        }

        await settings.SaveAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static string? TodaysWorkNote(string notes)
    {
        var day = WolfMark.Core.Notes.NotePath.FormatDirectory(DateOnly.FromDateTime(DateTime.Now));
        var dir = Path.Combine(notes, day);
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var file = Directory.EnumerateFiles(dir, "work_*.md").Order(StringComparer.Ordinal).FirstOrDefault()
                   ?? Directory.EnumerateFiles(dir, "*.md").Order(StringComparer.Ordinal).LastOrDefault();
        return file is null ? null : day + "/" + Path.GetFileName(file);
    }

    /// <summary>Starts index, watcher and sync in the background; never blocks the caller.</summary>
    public void StartBackground()
    {
        var ct = _shutdown.Token;
        _ = Background.Run("startup", async token =>
        {
            try
            {
                Directory.CreateDirectory(Root);
                await Tags.LoadAsync(token).ConfigureAwait(false);
                await Db.OpenAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Without this the app would sit there with empty lists and no word about why.
                Volatile.Write(ref _startupError, ex);
                StartupFailed?.Invoke(this, ex);
                throw; // logged by the runner
            }

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
            if (Settings.Current.TrashRetentionDays is > 0 and var days)
            {
                // The retention the user chose in Settings → Trash; off by default.
                var limit = Time.GetUtcNow().UtcDateTime.AddDays(-days);
                _ = Background.Run("trash-retention", async t =>
                {
                    var removed = await Trash.EmptyAsync(limit, SystemBin, t).ConfigureAwait(false);
                    if (removed > 0)
                    {
                        LogTrashRetention(removed, days);
                    }
                }, token);
            }

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
        _claudeScheduler.Synced += (_, r) =>
        {
            _firstClaudeSync.TrySetResult();
            ClaudeSynced?.Invoke(this, r);
        };
        _claudeScheduler.Failed += (_, ex) =>
        {
            _firstClaudeSync.TrySetResult();
            ClaudeSyncFailed?.Invoke(this, ex);
        };
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

    /// <summary>Portable mode of this run (ADR 0009): the data folder, or null in standard mode.</summary>
    public string? PortableDataDirectory => Paths.PortableDataDirectory;

    /// <summary>
    /// Copies everything into the portable folder next to the app and writes the marker (ADR 0009). Call on the UI
    /// thread after flushing the editor; watcher and Claude Code sync stop first, so the app must restart afterwards.
    /// </summary>
    /// <param name="moveNotes">Send the original notes folder to the Recycle Bin after the verified copy.</param>
    public Task<PortableMigrator.Result> EnablePortableAsync(bool moveNotes, IProgress<int>? progress)
    {
        var source = RelocationSource();
        return Task.Run(async () =>
        {
            await StopForRelocationAsync().ConfigureAwait(false);
            var app = PortableMode.AppDirectory;
            return await PortableMigrator.EnableAsync(
                app,
                PortableMode.DefaultDataDirectory(app),
                source,
                moveNotes ? RecycleBinForFolder(source.NotesRoot) : null,
                progress,
                CancellationToken.None).ConfigureAwait(false);
        });
    }

    /// <summary>Copies the portable data back to the user profile and removes the marker; the app must restart.</summary>
    public Task<PortableMigrator.Result> DisablePortableAsync(IProgress<int>? progress)
    {
        var source = RelocationSource();
        return Task.Run(async () =>
        {
            await StopForRelocationAsync().ConfigureAwait(false);
            var target = LocalPaths.Standard;
            var notes = PortableMigrator.StandardNotesRoot(target);
            IRecycleBin replaced = OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000) && WindowsRecycleBin.CanRecycle(notes)
                ? new WindowsRecycleBin()
                : new FolderRecycleBin(notes);
            return await PortableMigrator.DisableAsync(PortableMode.AppDirectory, source, target, notes, replaced, progress, CancellationToken.None).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Moves the notes to <paramref name="newRoot"/> (M8-04): copy, verify, then the old folder goes to the recycle
    /// bin. Call on the UI thread after flushing the editor; watcher and Claude Code sync stop first – nothing may
    /// write into the old folder any more – so the app must restart afterwards, also after a failure.
    /// </summary>
    public Task<NotesMover.Result> MoveNotesAsync(string newRoot, IProgress<int>? progress)
    {
        var root = Root;
        return Task.Run(async () =>
        {
            await StopForRelocationAsync().ConfigureAwait(false);
            return await NotesMover.MoveAsync(root, newRoot, RecycleBinForFolder(root), progress, CancellationToken.None).ConfigureAwait(false);
        });
    }

    private PortableMigrator.Source RelocationSource() => new(
        Paths,
        Root,
        Settings.CreateSnapshot(),
        async (path, ct) =>
        {
            if (StartupError is { } error)
            {
                throw new IOException("The index is not open.", error);
            }

            await DatabaseReady.WaitAsync(ct).ConfigureAwait(false);
            await Db.BackupToAsync(path, ct).ConfigureAwait(false);
        });

    /// <summary>
    /// No new files while the data is copied: the watcher and the Claude Code import stop – a running import is
    /// awaited, it must not write a note into the folder after it was copied.
    /// </summary>
    private async Task StopForRelocationAsync()
    {
        if (_claudeScheduler is { } scheduler)
        {
            _claudeScheduler = null;
            await scheduler.DisposeAsync().ConfigureAwait(false);
        }

        if (_watcher is { } watcher)
        {
            _watcher = null;
            await watcher.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>A recycle bin that can take a whole notes folder (the folder trash cannot hold its own folder).</summary>
    private static IRecycleBin RecycleBinForFolder(string folder)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(6, 0, 6000) && WindowsRecycleBin.CanRecycle(folder))
        {
            return new WindowsRecycleBin();
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar))!;
        return new FolderRecycleBin(parent, Path.Combine(parent, ".wolfmark-trash"));
    }

    /// <summary>Call on the UI thread (which owns the settings object); only the write runs in the background.</summary>
    public Task SaveSettingsAsync()
    {
        var json = Settings.CreateSnapshot();
        return Task.Run(() => Settings.SaveSnapshotAsync(json, CancellationToken.None));
    }

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

    [LoggerMessage(Level = LogLevel.Error, Message = "A delayed action (autosave, search, settings) failed")]
    private partial void LogDebouncedFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trash: {Count} notes deleted more than {Days} days ago were removed")]
    private partial void LogTrashRetention(int count, int days);

    [LoggerMessage(Level = LogLevel.Information, Message = "Shut down")]
    private partial void LogShutdown();
}
