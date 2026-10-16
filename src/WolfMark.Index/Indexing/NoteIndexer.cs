using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WolfMark.Core.Calendar;
using WolfMark.Core.Events;
using WolfMark.Core.Notes;
using WolfMark.Core.Settings;
using WolfMark.Core.Tags;
using WolfMark.Core.Text;
using WolfMark.Index.Database;
using WolfMark.Index.Queries;
using WolfMark.Storage;

namespace WolfMark.Index.Indexing;

/// <summary>
/// Keeps the index in sync with the directory (SPEC §4.2, M2-02, M3-01): initial/partial scans compare
/// mtime + size (+ content hash), domain events update single notes, and <c>calendar_days</c> is
/// recomputed for every touched day in the same transaction.
/// </summary>
public sealed partial class NoteIndexer
{
    private const int BatchSize = 200;

    private readonly IndexDatabase _db;
    private readonly DirectoryEngine _engine;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _scanLock = new(1, 1);

    public NoteIndexer(IndexDatabase db, DirectoryEngine engine, ILogger<NoteIndexer> logger)
    {
        _db = db;
        _engine = engine;
        _logger = logger;
    }

    /// <summary>Raised on a background thread after each committed change.</summary>
    public event EventHandler<IndexChangedEventArgs>? Changed;

    /// <summary>Number of notes (re)indexed since start – diagnostics and tests.</summary>
    public int IndexedCount { get; private set; }

    // ------------------------------------------------------------------ scans

    public Task FullScanAsync(IProgress<IndexProgress>? progress, CancellationToken cancellationToken) =>
        ScanAsync(null, _engine.EnumerateAsync(cancellationToken), progress, isFull: true, cancellationToken);

    public Task ScanMonthAsync(int year, int month, CancellationToken cancellationToken) =>
        ScanAsync(
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{year:D4}/{month:D2}/"),
            _engine.EnumerateMonthAsync(year, month, cancellationToken),
            null,
            isFull: false,
            cancellationToken);

    public Task ScanDirectoryAsync(string relativeDirectory, CancellationToken cancellationToken) =>
        ScanAsync(
            relativeDirectory.TrimEnd('/') + "/",
            _engine.EnumerateDirectoryAsync(relativeDirectory, recursive: true, cancellationToken),
            null,
            isFull: false,
            cancellationToken);

    private async Task ScanAsync(string? prefix, IAsyncEnumerable<NoteFile> files, IProgress<IndexProgress>? progress, bool isFull, CancellationToken ct)
    {
        await _scanLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var known = await _db.ReadAsync(c => LoadKnown(c, prefix), ct).ConfigureAwait(false);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var candidates = new List<(NoteFile File, KnownNote? Known)>();

            await foreach (var file in files.WithCancellation(ct).ConfigureAwait(false))
            {
                seen.Add(file.Path.RelativePath);
                known.TryGetValue(file.Path.RelativePath, out var k);
                if (k is not null && k.Mtime == file.MtimeTicks && k.Size == file.Length && (k.Hash.Length > 0 || file.IsCloudPlaceholder))
                {
                    continue; // unchanged
                }

                candidates.Add((file, k));
            }

            var removed = known.Keys.Where(p => !seen.Contains(p)).ToList();
            var total = candidates.Count;
            var processed = 0;
            progress?.Report(new IndexProgress("scan", 0, total));

            var days = new HashSet<DateOnly>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var batch in candidates.Chunk(BatchSize))
            {
                var rows = await PrepareRowsAsync(batch, ct).ConfigureAwait(false);
                var touched = await _db.WriteAsync(c => WriteRows(c, rows, []), ct).ConfigureAwait(false);
                days.UnionWith(touched);
                paths.UnionWith(rows.Select(r => r.File.Path.RelativePath));
                processed += batch.Length;
                progress?.Report(new IndexProgress("scan", processed, total));
            }

            if (removed.Count > 0)
            {
                var touched = await _db.WriteAsync(c => WriteRows(c, [], removed), ct).ConfigureAwait(false);
                days.UnionWith(touched);
                paths.UnionWith(removed);
            }

            if (isFull)
            {
                await _db.WriteAsync(c => SetMeta(c, "last_full_scan", DateTimeOffset.UtcNow.ToString("O")), ct).ConfigureAwait(false);
            }

            LogScanFinished(prefix ?? "*", total, removed.Count);
            if (days.Count > 0 || isFull)
            {
                Changed?.Invoke(this, new IndexChangedEventArgs(days, paths, isFull));
            }
        }
        finally
        {
            _scanLock.Release();
        }
    }

    // ------------------------------------------------------------------ events

    /// <summary>Consumes domain events until cancelled. Bursts are coalesced per path.</summary>
    public async Task RunAsync(ChannelReader<NoteEvent> events, CancellationToken cancellationToken)
    {
        while (await events.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var upserts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deletes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rescans = new List<string?>();

            while (events.TryRead(out var e))
            {
                switch (e)
                {
                    case NoteCreated c:
                        deletes.Remove(c.Path.RelativePath);
                        upserts.Add(c.Path.RelativePath);
                        break;
                    case NoteChanged c:
                        deletes.Remove(c.Path.RelativePath);
                        upserts.Add(c.Path.RelativePath);
                        break;
                    case NoteRenamed r:
                        upserts.Remove(r.OldPath.RelativePath);
                        deletes.Add(r.OldPath.RelativePath);
                        deletes.Remove(r.NewPath.RelativePath);
                        upserts.Add(r.NewPath.RelativePath);
                        break;
                    case NoteDeleted d:
                        upserts.Remove(d.Path.RelativePath);
                        deletes.Add(d.Path.RelativePath);
                        break;
                    case RescanRequested r:
                        rescans.Add(r.RelativeDirectory);
                        break;
                }
            }

            try
            {
                if (rescans.Contains(null))
                {
                    await FullScanAsync(null, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var dir in rescans.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    await ScanDirectoryAsync(dir!, cancellationToken).ConfigureAwait(false);
                }

                if (upserts.Count > 0 || deletes.Count > 0)
                {
                    await ApplyAsync(upserts, deletes, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogEventFailed(ex);
            }
        }
    }

    /// <summary>Index specific notes now (also used right after the editor saves).</summary>
    public async Task ApplyAsync(IReadOnlyCollection<string> upserts, IReadOnlyCollection<string> deletes, CancellationToken ct)
    {
        var files = new List<(NoteFile File, KnownNote? Known)>();
        var missing = new List<string>(deletes);
        foreach (var rel in upserts)
        {
            if (NotePath.TryParse(rel, out var path) && _engine.GetFile(path) is { } file)
            {
                files.Add((file, null));
            }
            else
            {
                missing.Add(rel);
            }
        }

        var rows = await PrepareRowsAsync(files, ct).ConfigureAwait(false);
        var days = await _db.WriteAsync(c => WriteRows(c, rows, missing), ct).ConfigureAwait(false);
        var paths = new HashSet<string>(upserts.Concat(deletes), StringComparer.OrdinalIgnoreCase);
        if (days.Count > 0 || paths.Count > 0)
        {
            Changed?.Invoke(this, new IndexChangedEventArgs(days, paths, isFullScan: false));
        }
    }

    // ------------------------------------------------------------------ placeholders

    /// <summary>
    /// Indexes content of OneDrive placeholders slowly in the background (this downloads them).
    /// </summary>
    public async Task IndexPendingContentAsync(TimeSpan delayBetweenFiles, CancellationToken ct)
    {
        var pending = await _db.ReadAsync(
            c =>
            {
                using var cmd = c.Command("SELECT rel_path FROM notes WHERE content_hash = '' ORDER BY created_at DESC LIMIT 500;");
                using var r = cmd.ExecuteReader();
                var list = new List<string>();
                while (r.Read())
                {
                    list.Add(r.GetString(0));
                }

                return list;
            },
            ct).ConfigureAwait(false);

        foreach (var rel in pending)
        {
            ct.ThrowIfCancellationRequested();
            if (!NotePath.TryParse(rel, out var path) || _engine.GetFile(path) is not { } file)
            {
                continue;
            }

            var row = await PrepareRowAsync(file, forceContent: true, ct).ConfigureAwait(false);
            var days = await _db.WriteAsync(c => WriteRows(c, [row], []), ct).ConfigureAwait(false);
            Changed?.Invoke(this, new IndexChangedEventArgs(days, new HashSet<string> { rel }, isFullScan: false));
            await Task.Delay(delayBetweenFiles, ct).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ preparation (I/O, no DB)

    private async Task<List<PreparedRow>> PrepareRowsAsync(IReadOnlyList<(NoteFile File, KnownNote? Known)> batch, CancellationToken ct)
    {
        var rows = new PreparedRow[batch.Count];
        await Parallel.ForAsync(
            0,
            batch.Count,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 },
            async (i, token) =>
            {
                var (file, known) = batch[i];
                rows[i] = await PrepareRowAsync(file, forceContent: false, token, known).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return [.. rows.Where(r => r is not null)];
    }

    private async Task<PreparedRow> PrepareRowAsync(NoteFile file, bool forceContent, CancellationToken ct, KnownNote? known = null)
    {
        if (file.IsCloudPlaceholder && !forceContent)
        {
            // Metadata only – the calendar needs just day + tag from the path. Content comes later.
            return new PreparedRow(file, null, string.Empty, TouchOnly: false);
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(file.FullPath, ct).ConfigureAwait(false);
            var hash = ContentHash.Compute(bytes);
            if (known is not null && known.Hash == hash)
            {
                return new PreparedRow(file, null, hash, TouchOnly: true);
            }

            var info = NoteContentAnalyzer.Analyze(DirectoryEngine.Decode(bytes));
            Interlocked.Increment(ref _indexedCount);
            return new PreparedRow(file, info, hash, TouchOnly: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(ex, file.Path.RelativePath);
            return new PreparedRow(file, null, string.Empty, TouchOnly: false);
        }
    }

    private int _indexedCount;

    // ------------------------------------------------------------------ DB (writer thread only)

    private static Dictionary<string, KnownNote> LoadKnown(SqliteConnection c, string? prefix)
    {
        using var cmd = c.Command(prefix is null
            ? "SELECT rel_path, file_mtime, file_size, content_hash FROM notes;"
            : "SELECT rel_path, file_mtime, file_size, content_hash FROM notes WHERE rel_path LIKE $p ESCAPE '\\';");
        if (prefix is not null)
        {
            cmd.With("$p", prefix.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        }

        using var r = cmd.ExecuteReader();
        var map = new Dictionary<string, KnownNote>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
        {
            map[r.GetString(0)] = new KnownNote(r.GetInt64(1), r.GetInt64(2), r.GetString(3));
        }

        return map;
    }

    private HashSet<DateOnly> WriteRows(SqliteConnection c, IReadOnlyList<PreparedRow> rows, IReadOnlyList<string> deletes)
    {
        var days = new HashSet<DateOnly>();
        using var tx = c.BeginTransaction();

        foreach (var rel in deletes)
        {
            using var find = c.Command("SELECT id, day FROM notes WHERE rel_path = $p;", tx).With("$p", rel);
            using var r = find.ExecuteReader();
            if (!r.Read())
            {
                continue;
            }

            var id = r.GetInt64(0);
            days.Add(SqliteExtensions.ParseDay(r.GetString(1)));
            r.Close();
            DeleteNote(c, tx, id);
        }

        foreach (var row in rows)
        {
            UpsertNote(c, tx, row, days);
        }

        foreach (var day in days)
        {
            RecomputeDay(c, tx, day);
        }

        tx.Commit();
        IndexedCount = _indexedCount;
        return days;
    }

    private static void DeleteNote(SqliteConnection c, SqliteTransaction tx, long id)
    {
        using (var cmd = c.Command("DELETE FROM notes_fts WHERE rowid = $id;", tx).With("$id", id))
        {
            cmd.ExecuteNonQuery();
        }

        DeleteVectors(c, tx, id);
        using (var cmd = c.Command("DELETE FROM notes WHERE id = $id;", tx).With("$id", id))
        {
            cmd.ExecuteNonQuery();
        }
    }

    internal static void DeleteVectors(SqliteConnection c, SqliteTransaction tx, long noteId)
    {
        var hasVec = c.Scalar("SELECT count(*) FROM sqlite_master WHERE name = 'note_chunks_vec';", tx) == "1";
        if (hasVec)
        {
            using var cmd = c.Command("DELETE FROM note_chunks_vec WHERE rowid IN (SELECT id FROM note_chunks WHERE note_id = $id);", tx).With("$id", noteId);
            cmd.ExecuteNonQuery();
        }

        using var del = c.Command("DELETE FROM note_chunks WHERE note_id = $id;", tx).With("$id", noteId);
        del.ExecuteNonQuery();
    }

    private static void UpsertNote(SqliteConnection c, SqliteTransaction tx, PreparedRow row, HashSet<DateOnly> days)
    {
        var file = row.File;
        var path = file.Path;
        var day = file.Day;
        days.Add(day);

        long? existingId = null;
        using (var find = c.Command("SELECT id, day FROM notes WHERE rel_path = $p;", tx).With("$p", path.RelativePath))
        using (var r = find.ExecuteReader())
        {
            if (r.Read())
            {
                existingId = r.GetInt64(0);
                days.Add(SqliteExtensions.ParseDay(r.GetString(1)));
            }
        }

        if (row.TouchOnly && existingId is { } touchId)
        {
            using var touch = c.Command("UPDATE notes SET file_mtime = $m, file_size = $s, is_placeholder = $ph WHERE id = $id;", tx)
                .With("$m", file.MtimeTicks).With("$s", file.Length).With("$ph", file.IsCloudPlaceholder ? 1 : 0).With("$id", touchId);
            touch.ExecuteNonQuery();
            return;
        }

        var info = row.Info;
        var tag = path.Tag;
        var isClaude = string.Equals(info?.Source, NoteSources.ClaudeCode, StringComparison.OrdinalIgnoreCase)
                       || (tag is not null && TagName.Comparer.Equals(tag, TagName.ClaudeCode));

        const string Sql = """
            INSERT INTO notes (rel_path, created_at, day, primary_tag, primary_tag_key, title, preview, content_hash,
                               file_mtime, file_size, word_count, open_tasks, done_tasks, source, session_id,
                               is_standard, is_conflict, conflict_of, is_placeholder)
            VALUES ($rel, $created, $day, $tag, $tagKey, $title, $preview, $hash, $mtime, $size, $words, $open, $done,
                    $source, $session, $std, $conflict, $conflictOf, $ph)
            ON CONFLICT(rel_path) DO UPDATE SET
              rel_path = excluded.rel_path, created_at = excluded.created_at, day = excluded.day,
              primary_tag = excluded.primary_tag, primary_tag_key = excluded.primary_tag_key,
              title = excluded.title, preview = excluded.preview, content_hash = excluded.content_hash,
              file_mtime = excluded.file_mtime, file_size = excluded.file_size, word_count = excluded.word_count,
              open_tasks = excluded.open_tasks, done_tasks = excluded.done_tasks, source = excluded.source,
              session_id = excluded.session_id, is_standard = excluded.is_standard, is_conflict = excluded.is_conflict,
              conflict_of = excluded.conflict_of, is_placeholder = excluded.is_placeholder
            RETURNING id;
            """;

        long id;
        using (var cmd = c.Command(Sql, tx))
        {
            cmd.With("$rel", path.RelativePath)
               .With("$created", SqliteExtensions.FormatTimestamp(file.CreatedAtLocal))
               .With("$day", SqliteExtensions.FormatDay(day))
               .With("$tag", tag)
               .With("$tagKey", tag is null ? null : TagName.ToKey(tag))
               .With("$title", info?.Title.Length > 0 ? info.Title : FallbackTitle(path))
               .With("$preview", info?.Preview ?? string.Empty)
               .With("$hash", row.Hash)
               .With("$mtime", file.MtimeTicks)
               .With("$size", file.Length)
               .With("$words", info?.WordCount ?? 0)
               .With("$open", info?.OpenTasks ?? 0)
               .With("$done", info?.DoneTasks ?? 0)
               .With("$source", isClaude ? NoteSources.ClaudeCode : NoteSources.User)
               .With("$session", info?.SessionId)
               .With("$std", path.IsStandard ? 1 : 0)
               .With("$conflict", path.IsConflictCopy ? 1 : 0)
               .With("$conflictOf", path.ConflictOriginal?.RelativePath)
               .With("$ph", file.IsCloudPlaceholder ? 1 : 0);
            id = (long)cmd.ExecuteScalar()!;
        }

        // Tags: primary (file name) + body/front matter.
        using (var del = c.Command("DELETE FROM note_tags WHERE note_id = $id;", tx).With("$id", id))
        {
            del.ExecuteNonQuery();
        }

        var allTags = new List<string>();
        if (tag is not null)
        {
            allTags.Add(tag);
        }

        if (info is not null)
        {
            allTags.AddRange(info.Tags);
        }

        foreach (var t in allTags.DistinctBy(TagName.ToKey))
        {
            using var ins = c.Command("INSERT OR IGNORE INTO note_tags (note_id, tag, tag_key) VALUES ($id, $t, $k);", tx)
                .With("$id", id).With("$t", t).With("$k", TagName.ToKey(t));
            ins.ExecuteNonQuery();
        }

        // Full text.
        using (var del = c.Command("DELETE FROM notes_fts WHERE rowid = $id;", tx).With("$id", id))
        {
            del.ExecuteNonQuery();
        }

        if (info is not null)
        {
            using var fts = c.Command("INSERT INTO notes_fts (rowid, title, body, tags) VALUES ($id, $title, $body, $tags);", tx)
                .With("$id", id).With("$title", info.Title).With("$body", info.Body).With("$tags", string.Join(' ', allTags));
            fts.ExecuteNonQuery();

            // Content changed → embeddings are stale.
            DeleteVectors(c, tx, id);
        }
    }

    private static string FallbackTitle(NotePath path) => Path.GetFileNameWithoutExtension(path.FileName);

    internal static void RecomputeDay(SqliteConnection c, SqliteTransaction tx, DateOnly day)
    {
        var dayText = SqliteExtensions.FormatDay(day);
        var tags = new List<TagCount>();
        int total = 0, open = 0, done = 0;
        using (var cmd = c.Command(
            """
            SELECT COALESCE(MIN(primary_tag), ''), COUNT(*), SUM(open_tasks), SUM(done_tasks)
            FROM notes WHERE day = $d GROUP BY COALESCE(primary_tag_key, '')
            ORDER BY COUNT(*) DESC, MIN(primary_tag);
            """,
            tx).With("$d", dayText))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var count = r.GetInt32(1);
                tags.Add(new TagCount(r.GetString(0), count));
                total += count;
                open += r.GetInt32(2);
                done += r.GetInt32(3);
            }
        }

        if (total == 0)
        {
            using var del = c.Command("DELETE FROM calendar_days WHERE day = $d;", tx).With("$d", dayText);
            del.ExecuteNonQuery();
            return;
        }

        using var up = c.Command(
            """
            INSERT INTO calendar_days (day, note_count, tag_summary, open_tasks, done_tasks) VALUES ($d, $n, $s, $o, $x)
            ON CONFLICT(day) DO UPDATE SET note_count = excluded.note_count, tag_summary = excluded.tag_summary,
              open_tasks = excluded.open_tasks, done_tasks = excluded.done_tasks;
            """,
            tx)
            .With("$d", dayText)
            .With("$n", total)
            .With("$s", JsonSerializer.Serialize(tags, WolfMarkJsonContext.Default.ListTagCount))
            .With("$o", open)
            .With("$x", done);
        up.ExecuteNonQuery();
    }

    private static object? SetMeta(SqliteConnection c, string key, string value)
    {
        using var cmd = c.Command("INSERT INTO meta(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value;")
            .With("$k", key).With("$v", value);
        cmd.ExecuteNonQuery();
        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scan {Scope}: {Changed} changed, {Removed} removed")]
    private partial void LogScanFinished(string scope, int changed, int removed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Processing note events failed")]
    private partial void LogEventFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read {Path}")]
    private partial void LogReadFailed(Exception ex, string path);

    private sealed record KnownNote(long Mtime, long Size, string Hash);

    private sealed record PreparedRow(NoteFile File, NoteContentInfo? Info, string Hash, bool TouchOnly);
}
