using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using WolfMark.Core.Events;
using WolfMark.Core.Notes;
using WolfMark.Core.Text;

namespace WolfMark.Storage.Watching;

/// <summary>
/// Turns raw <see cref="FileSystemWatcher"/> notifications into debounced domain events (SPEC §4.1):
/// watcher → channel → per-path debounce (300 ms) → <see cref="NoteEvent"/> on the hub.
/// Echoes of our own writes are dropped; buffer overflow triggers a full rescan.
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
    private FileSystemWatcher? _watcher;
    private Task? _loop;

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
        _watcher = new FileSystemWatcher(_engine.Root)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += (_, e) => Enqueue(RawKind.Created, e.FullPath, null);
        _watcher.Changed += (_, e) => Enqueue(RawKind.Changed, e.FullPath, null);
        _watcher.Deleted += (_, e) => Enqueue(RawKind.Deleted, e.FullPath, null);
        _watcher.Renamed += (_, e) => Enqueue(RawKind.Renamed, e.FullPath, e.OldFullPath);
        _watcher.Error += (_, e) =>
        {
            LogWatcherError(e.GetException());
            _raw.Writer.TryWrite(new RawChange(RawKind.Overflow, string.Empty, null, _time.GetUtcNow()));
        };
        _watcher.EnableRaisingEvents = true;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

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

        if (rel.StartsWith('.') || rel.Contains("/.", StringComparison.Ordinal) || rel.Contains("/assets/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isNoteFile = NotePath.IsCandidateFileName(fileName);
        var isDirectory = !isNoteFile && (Directory.Exists(change.FullPath) || !Path.HasExtension(fileName));

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
                    pending.Remove(oldRel);
                    pending[rel] = new PendingChange(rel, PendingKind.Renamed, oldRel, null, change.At);
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
                    if (!await IsOwnContentAsync(p.Key, ct).ConfigureAwait(false))
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
                if (!_engine.SelfWrites.ConsumeExpectedRemoval(path.RelativePath))
                {
                    _hub.Publish(new NoteDeleted(path, ChangeOrigin.External));
                }

                return;
            }

            if (_engine.SelfWrites.ConsumeExpectedRemoval(path.RelativePath))
            {
                return; // target of our own rename
            }

            if (await IsOwnContentAsync(path.RelativePath, ct).ConfigureAwait(false))
            {
                return;
            }

            _hub.Publish(p.Kind == PendingKind.Created ? new NoteCreated(path, ChangeOrigin.External) : new NoteChanged(path, ChangeOrigin.External));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEmitFailed(ex, p.Key);
        }
    }

    private async Task<bool> IsOwnContentAsync(string relativePath, CancellationToken ct)
    {
        var full = _engine.GetFullPath(relativePath);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var info = new FileInfo(full);
                if (!info.Exists || CloudFileAttributes.IsPlaceholder(info.Attributes))
                {
                    return false; // never hydrate a placeholder just to compare
                }

                var bytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
                return _engine.SelfWrites.IsOwnContent(relativePath, ContentHash.Compute(bytes));
            }
            catch (IOException)
            {
                await Task.Delay(50 * (attempt + 1), ct).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }

        _raw.Writer.TryComplete();
        await _cts.CancelAsync().ConfigureAwait(false);
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
