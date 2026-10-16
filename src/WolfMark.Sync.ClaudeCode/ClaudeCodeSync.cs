using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using WolfMark.Core.Notes;
using WolfMark.Core.Tags;
using WolfMark.Core.Text;
using WolfMark.Index.Database;
using WolfMark.Storage;

namespace WolfMark.Sync.ClaudeCode;

public sealed class ClaudeCodeSyncOptions
{
    /// <summary>The <c>.claude</c> directory.</summary>
    public required string ClaudeDirectory { get; init; }

    public IReadOnlyList<string> ExcludedProjects { get; init; } = [];

    public bool RedactSecrets { get; init; } = true;
}

public sealed record SyncResult(int FilesParsed, int NotesCreated, int NotesUpdated, int PromptsSeen);

/// <summary>
/// Imports human prompts from Claude Code transcripts as <c>ClaudeCode_HHmm.md</c> notes – one per
/// session and day (SPEC §4.7). Idempotent: unchanged transcripts are skipped by (size, mtime); notes
/// are regenerated deterministically and only written when their content differs. If the index is
/// lost, existing notes are found again via the <c>session:</c> front matter in the day directory.
/// </summary>
public sealed partial class ClaudeCodeSync
{
    private readonly DirectoryEngine _engine;
    private readonly IndexDatabase _db;
    private readonly ClaudeCodeSyncOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _tablesReady;

    public ClaudeCodeSync(DirectoryEngine engine, IndexDatabase db, ClaudeCodeSyncOptions options, TimeProvider time, ILogger<ClaudeCodeSync> logger)
    {
        _engine = engine;
        _db = db;
        _options = options;
        _time = time;
        _logger = logger;
        _db.Reset += (_, _) => _tablesReady = false;
    }

    public string ProjectsDirectory => Path.Combine(_options.ClaudeDirectory, "projects");

    public string SettingsPath => Path.Combine(_options.ClaudeDirectory, "settings.json");

    public async Task<SyncResult> SyncAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await SyncCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SyncResult> SyncCoreAsync(CancellationToken ct)
    {
        if (!Directory.Exists(ProjectsDirectory))
        {
            return new SyncResult(0, 0, 0, 0);
        }

        await EnsureTablesAsync(ct).ConfigureAwait(false);
        var known = await _db.ReadAsync(LoadFileState, ct).ConfigureAwait(false);

        int parsed = 0, created = 0, updated = 0, prompts = 0;
        var transcripts = Directory.EnumerateFiles(ProjectsDirectory, "*.jsonl", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true })
            .Concat(Directory.EnumerateDirectories(ProjectsDirectory).SelectMany(d => Directory.EnumerateFiles(d, "*.jsonl")))
            .Select(p => new FileInfo(p))
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();

        foreach (var file in transcripts)
        {
            ct.ThrowIfCancellationRequested();
            var key = file.FullName;
            if (known.TryGetValue(key, out var state) && state.Size == file.Length && state.Mtime == file.LastWriteTimeUtc.Ticks)
            {
                continue;
            }

            var sessionPrompts = new List<ClaudePrompt>();
            try
            {
                await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                await foreach (var prompt in TranscriptParser.ReadAsync(stream, ct).ConfigureAwait(false))
                {
                    if (!IsExcluded(prompt.Cwd))
                    {
                        sessionPrompts.Add(_options.RedactSecrets ? prompt with { Text = SecretRedactor.Redact(prompt.Text) } : prompt);
                    }
                }
            }
            catch (IOException ex)
            {
                LogReadFailed(ex, file.FullName);
                continue;
            }

            parsed++;
            prompts += sessionPrompts.Count;

            var tz = _time.LocalTimeZone;
            var groups = sessionPrompts
                .DistinctBy(p => p.Uuid.Length > 0 ? p.Uuid : p.Timestamp.ToString("O", CultureInfo.InvariantCulture) + p.Text)
                .OrderBy(p => p.Timestamp)
                .Select(p => (Local: TimeZoneInfo.ConvertTime(p.Timestamp, tz).DateTime, Prompt: p))
                .GroupBy(p => (p.Prompt.SessionId, Day: DateOnly.FromDateTime(p.Local)));

            foreach (var group in groups)
            {
                var items = group.ToList();
                var content = SessionNoteRenderer.Render(group.Key.SessionId, items);
                var result = await WriteSessionNoteAsync(group.Key.SessionId, group.Key.Day, items[0].Local, content, ct).ConfigureAwait(false);
                if (result == WriteResult.Created)
                {
                    created++;
                }
                else if (result == WriteResult.Updated)
                {
                    updated++;
                }
            }

            await _db.WriteAsync(c => SaveFileState(c, key, file.Length, file.LastWriteTimeUtc.Ticks), ct).ConfigureAwait(false);
        }

        LogSynced(parsed, created, updated);
        return new SyncResult(parsed, created, updated, prompts);
    }

    private bool IsExcluded(string? cwd) =>
        cwd is not null && _options.ExcludedProjects.Any(p =>
            !string.IsNullOrWhiteSpace(p) && cwd.StartsWith(p.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase));

    private async Task<WriteResult> WriteSessionNoteAsync(string sessionId, DateOnly day, DateTime firstLocal, string content, CancellationToken ct)
    {
        var dayText = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var mapped = await _db.ReadAsync(c => LoadNoteMapping(c, sessionId, dayText), ct).ConfigureAwait(false);
        NotePath? existing = null;
        if (mapped is not null && NotePath.TryParse(mapped, out var mappedPath) && File.Exists(_engine.GetFullPath(mappedPath)))
        {
            existing = mappedPath;
        }

        existing ??= await FindBySessionAsync(day, sessionId, ct).ConfigureAwait(false);

        if (existing is not null)
        {
            var current = await _engine.ReadAsync(existing, ct).ConfigureAwait(false);
            if (existing.RelativePath != mapped)
            {
                await _db.WriteAsync(c => SaveNoteMapping(c, sessionId, dayText, existing.RelativePath), ct).ConfigureAwait(false);
            }

            if (string.Equals(current, content, StringComparison.Ordinal))
            {
                return WriteResult.Unchanged;
            }

            await _engine.SaveAsync(existing, content, ct).ConfigureAwait(false);
            return WriteResult.Updated;
        }

        var path = await _engine.CreateAsync(firstLocal, TagName.ClaudeCode, content, ct).ConfigureAwait(false);
        await _db.WriteAsync(c => SaveNoteMapping(c, sessionId, dayText, path.RelativePath), ct).ConfigureAwait(false);
        return WriteResult.Created;
    }

    /// <summary>Source of truth fallback: scan the day directory for a note with this session in its front matter.</summary>
    private async Task<NotePath?> FindBySessionAsync(DateOnly day, string sessionId, CancellationToken ct)
    {
        await foreach (var file in _engine.EnumerateDirectoryAsync(NotePath.FormatDirectory(day), recursive: false, ct).ConfigureAwait(false))
        {
            if (file.Path.Tag is not { } tag || !TagName.Comparer.Equals(tag, TagName.ClaudeCode) || file.IsCloudPlaceholder)
            {
                continue;
            }

            var text = await _engine.ReadAsync(file.Path, ct).ConfigureAwait(false);
            var lines = text.Split('\n', 12);
            if (string.Equals(FrontMatter.Parse(lines).Get("session"), sessionId, StringComparison.Ordinal))
            {
                return file.Path;
            }
        }

        return null;
    }

    private async Task EnsureTablesAsync(CancellationToken ct)
    {
        if (_tablesReady)
        {
            return;
        }

        await _db.WriteAsync(
            c => c.Execute(
                """
                CREATE TABLE IF NOT EXISTS claude_sync_files (path TEXT PRIMARY KEY, size INTEGER NOT NULL, mtime INTEGER NOT NULL) WITHOUT ROWID;
                CREATE TABLE IF NOT EXISTS claude_sync_notes (session_id TEXT NOT NULL, day TEXT NOT NULL, rel_path TEXT NOT NULL, PRIMARY KEY (session_id, day)) WITHOUT ROWID;
                """),
            ct).ConfigureAwait(false);
        _tablesReady = true;
    }

    private static Dictionary<string, (long Size, long Mtime)> LoadFileState(SqliteConnection c)
    {
        var result = new Dictionary<string, (long, long)>(StringComparer.OrdinalIgnoreCase);
        using var cmd = c.Command("SELECT path, size, mtime FROM claude_sync_files;");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result[r.GetString(0)] = (r.GetInt64(1), r.GetInt64(2));
        }

        return result;
    }

    private static object? SaveFileState(SqliteConnection c, string path, long size, long mtime)
    {
        using var cmd = c.Command("INSERT INTO claude_sync_files (path, size, mtime) VALUES ($p, $s, $m) ON CONFLICT(path) DO UPDATE SET size = excluded.size, mtime = excluded.mtime;")
            .With("$p", path).With("$s", size).With("$m", mtime);
        cmd.ExecuteNonQuery();
        return null;
    }

    private static string? LoadNoteMapping(SqliteConnection c, string sessionId, string day)
    {
        using var cmd = c.Command("SELECT rel_path FROM claude_sync_notes WHERE session_id = $s AND day = $d;").With("$s", sessionId).With("$d", day);
        return cmd.ExecuteScalar() as string;
    }

    private static object? SaveNoteMapping(SqliteConnection c, string sessionId, string day, string relPath)
    {
        using var cmd = c.Command("INSERT INTO claude_sync_notes (session_id, day, rel_path) VALUES ($s, $d, $p) ON CONFLICT(session_id, day) DO UPDATE SET rel_path = excluded.rel_path;")
            .With("$s", sessionId).With("$d", day).With("$p", relPath);
        cmd.ExecuteNonQuery();
        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Claude Code sync: {Parsed} transcripts parsed, {Created} notes created, {Updated} updated")]
    private partial void LogSynced(int parsed, int created, int updated);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read transcript {Path}")]
    private partial void LogReadFailed(Exception ex, string path);

    private enum WriteResult
    {
        Unchanged,
        Created,
        Updated,
    }
}
