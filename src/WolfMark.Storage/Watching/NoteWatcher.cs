using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WolfMark.Core.Events;
using WolfMark.Core.Notes;
using WolfMark.Core.Text;

namespace WolfMark.Storage.Watching;

/// <summary>
/// Turns raw <see cref="FileSystemWatcher"/> notifications into debounced domain events (SPEC §4.1):
/// watcher → channel → per-path debounce (300 ms) → <see cref="NoteEvent"/> on the hub.
/// Echoes of our own writes are dropped; buffer overflow triggers a full rescan. A repeated notification for
/// content already reported (Windows can deliver a late <c>Changed</c> after the debounce) is dropped too.
/// </summary>
public sealed partial class NoteWatcher : IAsyncDisposable
{
    public const string TagsRelativePath = ".wolfmark/tags.json";

    private readonly DirectoryEngine _engine;
    private readonly NoteEventHub _hub;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TimeSpan _debounce;
    private readonly Channel<RawChange> _raw = Channel.CreateUnbounded<RawChange>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();

    // Hash of the content last reported as an external create/change, per note; touched only by the loop.
    private readonly Dictionary<string, string> _reported = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _watcherGate = new();
    private FileSystemWatcher? _watcher;
    private Task? _loop;
    private int _rearming;

    /// <summary>How long after a watcher error the folder is watched again (and between attempts while it is away).</summary>
    internal TimeSpan RearmDelay { get; set; } = TimeSpan.FromSeconds(2);

    public NoteWatcher(DirectoryEngine engine, NoteEventHub hub, TimeProvider time, ILogger<NoteWatcher> logger, TimeSpan? debounce = null)
    {
        _engine = engine;
        _hub = hub;
        _time = time;
        _logger = logger;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
    }

    public void Start()
    {
        Directory.CreateDirectory(_engine.Root);
        lock (_watcherGate)
        {
            _watcher = CreateWatcher();
        }

        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private FileSystemWatcher CreateWatcher()
    {
        var watcher = new FileSystemWatcher(_engine.Root)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        try
        {
            watcher.Created += (_, e) => Enqueue(RawKind.Created, e.FullPath, null);
            watcher.Changed += (_, e) => Enqueue(RawKind.Changed, e.FullPath, null);
            watcher.Deleted += (_, e) => Enqueue(RawKind.Deleted, e.FullPath, null);
            watcher.Renamed += (_, e) => Enqueue(RawKind.Renamed, e.FullPath, e.OldFullPath);
            watcher.Error += (_, e) => OnWatcherError(e.GetException());
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch
        {
            watcher.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A buffer overflow only loses events (a rescan covers them), but after a lost folder – a network share or
    /// OneDrive going away – the watcher stays silent for good. So besides the rescan it is created anew.
    /// </summary>
    internal void OnWatcherError(Exception error)
    {
        LogWatcherError(error);
        _raw.Writer.TryWrite(new RawChange(RawKind.Overflow, string.Empty, null, _time.GetUtcNow()));
        if (Interlocked.Exchange(ref _rearming, 1) == 0)
        {
            _ = Task.Run(() => RearmAsync(_cts.Token));
        }
    }

    private async Task RearmAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await Task.Delay(RearmDelay, _time, ct).ConfigureAwait(false);
                FileSystemWatcher fresh;
                try
                {
                    fresh = CreateWatcher();
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
                {
                    continue; // the folder is still away
                }

                FileSystemWatcher? old;
                lock (_watcherGate)
                {
                    if (ct.IsCancellationRequested)
                    {
                        fresh.Dispose();
                        return;
                    }

                    old = _watcher;
                    _watcher = fresh;
                }

                old?.Dispose();
                Volatile.Write(ref _rearming, 0);
                // Whatever happened while nobody watched.
                _raw.Writer.TryWrite(new RawChange(RawKind.Overflow, string.Empty, null, _time.GetUtcNow()));
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsIgnored(string rel) =>
        rel.StartsWith('.') || rel.Contains("/.", StringComparison.Ordinal) || rel.Contains("/assets/", StringComparison.OrdinalIgnoreCase);

    private void Enqueue(RawKind kind, string fullPath, string? oldFullPath) =>
        _raw.Writer.TryWrite(new RawChange(kind, fullPath, oldFullPath, _time.GetUtcNow()));

    private async Task RunAsync(CancellationToken ct)
    {
        var pending = new Dictionary<string, PendingChange>(StringComparer.OrdinalIgnoreCase);
        var tick = TimeSpan.FromMilliseconds(Math.Max(20, _debounce.TotalMilliseconds / 3));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Wait for the first item, or tick while something is pending.
                if (pending.Count == 0)
                {
                    if (!await _raw.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                    {
                        return;
                    }
                }
                else
                {
                    using var delay = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    delay.CancelAfter(tick);
                    try
                    {
                        await _raw.Reader.WaitToReadAsync(delay.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                    }
                }

                while (_raw.Reader.TryRead(out var change))
                {
                    Accumulate(pending, change);
                }

                var now = _time.GetUtcNow();
                List<PendingChange>? ready = null;
                foreach (var p in pending.Values)
                {
                    if (now - p.LastSeen >= _debounce)
                    {
                        (ready ??= []).Add(p);
                    }
                }

                if (ready is null)
                {
                    continue;
                }

                foreach (var p in ready)
                {
                    pending.Remove(p.Key);
                    await EmitAsync(p, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private void Accumulate(Dictionary<string, PendingChange> pending, RawChange change)
    {
        if (change.Kind == RawKind.Overflow)
        {
            pending.Clear();
            pending["*"] = new PendingChange("*", PendingKind.Rescan, null, null, change.At);
            return;
        }

        if (pending.TryGetValue("*", out var rescan))
        {
            pending["*"] = rescan with { LastSeen = change.At };
            return;
        }

        var rel = _engine.GetRelativePath(change.FullPath);
        var fileName = Path.GetFileName(change.FullPath);
        if (AtomicFile.IsTempFile(fileName) || fileName.EndsWith(".rename" + AtomicFile.TempSuffix, StringComparison.OrdinalIgnoreCase))
        {
            // The atomic-save temp file itself; the final Renamed/Changed event of the target follows.
            return;
        }

        if (string.Equals(rel, TagsRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            Upsert(pending, rel, PendingKind.Tags, change.At);
            return;
        }

        if (IsIgnored(rel))
        {
            // A note moved out of sight (into .archive/, assets/) is gone as far as the index is concerned.
            if (change.Kind == RawKind.Renamed && change.OldFullPath is not null
                && NotePath.IsCandidateFileName(Path.GetFileName(change.OldFullPath))
                && _engine.GetRelativePath(change.OldFullPath) is var hidden && !IsIgnored(hidden))
            {
                Upsert(pending, hidden, PendingKind.Deleted, change.At);
            }

            return;
        }

        var isNoteFile = NotePath.IsCandidateFileName(fileName);
        // A deleted folder can no longer be asked what it was, and its name may well have a dot ("v1.2"): anything
        // deleted that is not a note is rescanned as a folder – for a plain file that finds nothing.
        var isDirectory = !isNoteFile
            && (Directory.Exists(change.FullPath) || !Path.HasExtension(fileName) || change.Kind == RawKind.Deleted);

        switch (change.Kind)
        {
            case RawKind.Renamed when change.OldFullPath is not null:
            {
                var oldRel = _engine.GetRelativePath(change.OldFullPath);
                var oldName = Path.GetFileName(change.OldFullPath);
                if (AtomicFile.IsTempFile(oldName))
                {
                    // temp → target: this is an atomic save completing.
                    if (isNoteFile)
                    {
                        Upsert(pending, rel, PendingKind.Changed, change.At);
                    }

                    return;
                }

                if (isDirectory)
                {
                    Upsert(pending, "*", PendingKind.Rescan, change.At);
                    return;
                }

                var oldIsNote = NotePath.IsCandidateFileName(oldName);
                if (oldIsNote && isNoteFile)
                {
                    // A → B → C within the debounce is one rename A → C; forgetting A would leave it in the index.
                    var origin = pending.TryGetValue(oldRel, out var earlier) && earlier is { Kind: PendingKind.Renamed, OldKey: not null }
                        ? earlier.OldKey
                        : oldRel;
                    pending.Remove(oldRel);
                    if (string.Equals(origin, rel, StringComparison.OrdinalIgnoreCase))
                    {
                        Upsert(pending, rel, PendingKind.Changed, change.At); // renamed there and back
                    }
                    else
                    {
                        pending[rel] = new PendingChange(rel, PendingKind.Renamed, origin, null, change.At);
                    }
                }
                else if (oldIsNote)
                {
                    Upsert(pending, oldRel, PendingKind.Deleted, change.At);
                }
                else if (isNoteFile)
                {
                    Upsert(pending, rel, PendingKind.Created, change.At);
                }

                return;
            }

            case RawKind.Created or RawKind.Changed or RawKind.Deleted when isDirectory:
                if (change.Kind != RawKind.Changed)
                {
                    Upsert(pending, "dir:" + rel, PendingKind.RescanDirectory, change.At, rel);
                }

                return;

            case RawKind.Created when isNoteFile:
                Upsert(pending, rel, PendingKind.Created, change.At);
                return;

            case RawKind.Changed when isNoteFile:
                Upsert(pending, rel, PendingKind.Changed, change.At);
                return;

            case RawKind.Deleted when isNoteFile:
                Upsert(pending, rel, PendingKind.Deleted, change.At);
                return;
        }
    }

    private static void Upsert(Dictionary<string, PendingChange> pending, string key, PendingKind kind, DateTimeOffset at, string? directory = null)
    {
        if (pending.TryGetValue(key, out var existing))
        {
            // Created followed by Changed stays Created; Created followed by Deleted becomes Deleted (resolved on emit).
            var merged = existing.Kind == PendingKind.Created && kind == PendingKind.Changed ? PendingKind.Created : kind;
            if (existing.Kind == PendingKind.Renamed && kind is PendingKind.Changed or PendingKind.Created)
            {
                merged = PendingKind.Renamed;
            }

            pending[key] = existing with { Kind = merged, LastSeen = at };
        }
        else
        {
            pending[key] = new PendingChange(key, kind, null, directory, at);
        }
    }

    private async Task EmitAsync(PendingChange p, CancellationToken ct)
    {
        try
        {
            switch (p.Kind)
            {
                case PendingKind.Rescan:
                    _hub.Publish(new RescanRequested(null, ChangeOrigin.External));
                    return;

                case PendingKind.RescanDirectory:
                    _hub.Publish(new RescanRequested(p.Directory, ChangeOrigin.External));
                    return;

                case PendingKind.Tags:
                    var tagsHash = await ReadHashAsync(p.Key, ct).ConfigureAwait(false);
                    if (tagsHash is null || !_engine.SelfWrites.IsOwnContent(p.Key, tagsHash))
                    {
                        _hub.Publish(new TagsChanged(ChangeOrigin.External));
                    }

                    return;
            }

            if (!NotePath.TryParse(p.Key, out var path))
            {
                return;
            }

            var exists = File.Exists(_engine.GetFullPath(path));

            if (p.Kind == PendingKind.Renamed && p.OldKey is not null && NotePath.TryParse(p.OldKey, out var oldPath))
            {
                _reported.Remove(oldPath.RelativePath);
                _reported.Remove(path.RelativePath);
                var ownOld = _engine.SelfWrites.ConsumeExpectedRemoval(oldPath.RelativePath);
                var ownNew = _engine.SelfWrites.ConsumeExpectedRemoval(path.RelativePath);
                if (!(ownOld || ownNew))
                {
                    _hub.Publish(exists ? new NoteRenamed(oldPath, path, ChangeOrigin.External) : new NoteDeleted(oldPath, ChangeOrigin.External));
                }

                return;
            }

            if (!exists)
            {
                _reported.Remove(path.RelativePath);
                if (!_engine.SelfWrites.ConsumeExpectedRemoval(path.RelativePath))
                {
                    _hub.Publish(new NoteDeleted(path, ChangeOrigin.External));
                }

                return;
            }

            if (_engine.SelfWrites.ConsumeExpectedRemoval(path.RelativePath))
            {
                _reported.Remove(path.RelativePath);
                return; // target of our own rename
            }

            var hash = await ReadHashAsync(path.RelativePath, ct).ConfigureAwait(false);
            if (hash is not null && _engine.SelfWrites.IsOwnContent(path.RelativePath, hash))
            {
                _reported.Remove(path.RelativePath); // our save came in between: the same external content again is news
                return;
            }

            if (hash is not null && _reported.TryGetValue(path.RelativePath, out var last) && last == hash)
            {
                return; // a late duplicate notification of a change already reported
            }

            if (hash is null)
            {
                _reported.Remove(path.RelativePath);
            }
            else
            {
                _reported[path.RelativePath] = hash;
            }

            // The file is someone else's now: if it later returns to what we last wrote, that is a change again.
            _engine.SelfWrites.Forget(path.RelativePath);
            _hub.Publish(p.Kind == PendingKind.Created ? new NoteCreated(path, ChangeOrigin.External) : new NoteChanged(path, ChangeOrigin.External));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEmitFailed(ex, p.Key);
        }
    }

    /// <summary>Content hash of the file; null when it is gone, a cloud placeholder or unreadable.</summary>
    private async Task<string?> ReadHashAsync(string relativePath, CancellationToken ct)
    {
        var full = _engine.GetFullPath(relativePath);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var info = new FileInfo(full);
                if (!info.Exists || CloudFileAttributes.IsPlaceholder(info.Attributes))
                {
                    return null; // never hydrate a placeholder just to compare
                }

                var bytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
                return ContentHash.Compute(bytes);
            }
            catch (IOException)
            {
                await Task.Delay(50 * (attempt + 1), ct).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        lock (_watcherGate)
        {
            _watcher?.Dispose();
            _watcher = null;
        }

        _raw.Writer.TryComplete();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "File watcher error – scheduling full rescan")]
    private partial void LogWatcherError(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process file change {Path}")]
    private partial void LogEmitFailed(Exception ex, string path);

    private enum RawKind
    {
        Created,
        Changed,
        Deleted,
        Renamed,
        Overflow,
    }

    private enum PendingKind
    {
        Created,
        Changed,
        Deleted,
        Renamed,
        Rescan,
        RescanDirectory,
        Tags,
    }

    private readonly record struct RawChange(RawKind Kind, string FullPath, string? OldFullPath, DateTimeOffset At);

    private sealed record PendingChange(string Key, PendingKind Kind, string? OldKey, string? Directory, DateTimeOffset LastSeen);
}
