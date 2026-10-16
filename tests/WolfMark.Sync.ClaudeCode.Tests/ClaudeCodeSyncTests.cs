using Microsoft.Extensions.Logging.Abstractions;
using WolfMark.Index.Database;
using WolfMark.Storage;
using WolfMark.Storage.Trash;

namespace WolfMark.Sync.ClaudeCode.Tests;

public sealed class ClaudeCodeSyncTests : IAsyncLifetime
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "wolfmark-sync-tests", Guid.NewGuid().ToString("N"));
    private IndexDatabase _db = null!;
    private DirectoryEngine _engine = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Root => Path.Combine(_base, "notes");

    private string Claude => Path.Combine(_base, ".claude");

    private string ProjectDir => Path.Combine(Claude, "projects", "-work-WolfMark");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(ProjectDir);
        _engine = new DirectoryEngine(Root, new FolderRecycleBin(Root), new NoteEventHub(), new SelfWriteRegistry(), NullLogger<DirectoryEngine>.Instance);
        await OpenDbAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        try
        {
            Directory.Delete(_base, true);
        }
        catch (IOException)
        {
        }
    }

    private async Task OpenDbAsync()
    {
        _db = new IndexDatabase(Path.Combine(_base, "index.db"), NullLogger<IndexDatabase>.Instance);
        await _db.OpenAsync(Ct);
    }

    private ClaudeCodeSync CreateSync(params string[] excluded) => new(
        _engine,
        _db,
        new ClaudeCodeSyncOptions { ClaudeDirectory = Claude, ExcludedProjects = excluded },
        new UtcTimeProvider(),
        NullLogger<ClaudeCodeSync>.Instance);

    private string Transcript => Path.Combine(ProjectDir, Samples.Session + ".jsonl");

    private string[] NoteFiles() =>
        Directory.Exists(Root) ? [.. Directory.GetFiles(Root, "*.md", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/')).Order()] : [];

    [Fact]
    public async Task Demo_data_is_valid_notes_and_imports_as_claude_projects()
    {
        var now = new DateTime(2026, 10, 16, 15, 30, 0, DateTimeKind.Local);
        Assert.True(await WolfMark.Storage.Demo.DemoDataSeeder.SeedAsync(Root, Claude, now, Ct));
        Assert.False(await WolfMark.Storage.Demo.DemoDataSeeder.SeedAsync(Root, Claude, now, Ct)); // only into an empty folder

        var userNotes = NoteFiles();
        Assert.True(userNotes.Length > 40);
        Assert.All(userNotes, f => Assert.True(WolfMark.Core.Notes.NotePath.Parse(f).IsStandard, f));
        Assert.Contains(userNotes, f => f.StartsWith("2026/10/16/work_", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(Root, ".wolfmark", "tags.json")));

        var result = await CreateSync().SyncAsync(Ct);
        Assert.True(result.NotesCreated >= 10);
        var claude = NoteFiles().Where(f => f.Contains("/ClaudeCode_", StringComparison.Ordinal)).ToList();
        var texts = await Task.WhenAll(claude.Select(f => File.ReadAllTextAsync(Path.Combine(Root, f), Ct)));
        Assert.Contains(texts, t => t.Contains("# WolfMark", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("# billing-api", StringComparison.Ordinal));
        Assert.Contains(texts, t => t.Contains("# infra-terraform", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("sk-ant-api03-DEMO", StringComparison.Ordinal) || t.Contains("Caveat:", StringComparison.Ordinal));
        Assert.Contains(claude, f => f.StartsWith("2026/10/16/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Creates_one_note_per_session_and_day()
    {
        await File.WriteAllLinesAsync(Transcript, Samples.MixedTranscript, Ct);
        var result = await CreateSync().SyncAsync(Ct);

        Assert.Equal(1, result.NotesCreated);
        Assert.Equal(["2026/10/15/ClaudeCode_1203.md"], NoteFiles());
        var content = await File.ReadAllTextAsync(Path.Combine(Root, "2026", "10", "15", "ClaudeCode_1203.md"), Ct);
        Assert.Contains("session: " + Samples.Session, content);
        Assert.Contains("# WolfMark", content);
        Assert.Contains("### 12:03\n\nRefaktoruj indexer", content);
        Assert.Contains("### 12:20\n\n/review PR 42", content);
        Assert.DoesNotContain("tool_result", content);
    }

    [Fact]
    public async Task Repeated_sync_is_idempotent_and_appends_new_prompts()
    {
        await File.WriteAllLinesAsync(Transcript, Samples.MixedTranscript, Ct);
        var sync = CreateSync();
        await sync.SyncAsync(Ct);

        var second = await sync.SyncAsync(Ct);
        Assert.Equal(0, second.FilesParsed);

        await File.AppendAllLinesAsync(Transcript, [Samples.User("n1", "2026-10-15T13:00:00Z", Samples.Text("Nový prompt"))], Ct);
        var third = await sync.SyncAsync(Ct);
        Assert.Equal((0, 1), (third.NotesCreated, third.NotesUpdated));
        Assert.Single(NoteFiles());
        Assert.Contains("Nový prompt", await File.ReadAllTextAsync(Path.Combine(Root, NoteFiles()[0]), Ct));
    }

    [Fact]
    public async Task Lost_index_does_not_duplicate_notes()
    {
        await File.WriteAllLinesAsync(Transcript, Samples.MixedTranscript, Ct);
        await CreateSync().SyncAsync(Ct);

        // Index is a cache – wipe it completely.
        await _db.ResetAsync(Ct);
        var again = await CreateSync().SyncAsync(Ct);

        Assert.Equal(1, again.FilesParsed);
        Assert.Equal(0, again.NotesCreated);
        Assert.Single(NoteFiles());
    }

    [Fact]
    public async Task Session_over_midnight_is_split_into_days()
    {
        await File.WriteAllLinesAsync(Transcript,
        [
            Samples.User("a", "2026-10-15T23:50:00Z", Samples.Text("pred polnocou")),
            Samples.User("b", "2026-10-16T00:10:00Z", Samples.Text("po polnoci")),
        ], Ct);

        await CreateSync().SyncAsync(Ct);
        Assert.Equal(["2026/10/15/ClaudeCode_2350.md", "2026/10/16/ClaudeCode_0010.md"], NoteFiles());
    }

    [Fact]
    public async Task Modification_time_is_the_last_prompt_of_each_day()
    {
        await File.WriteAllLinesAsync(Transcript,
        [
            Samples.User("a", "2026-10-15T09:00:00Z", Samples.Text("ráno")),
            Samples.User("b", "2026-10-15T23:50:00Z", Samples.Text("pred polnocou")),
            Samples.User("c", "2026-10-16T00:10:00Z", Samples.Text("po polnoci")),
            Samples.User("d", "2026-10-16T02:30:00Z", Samples.Text("neskoro")),
        ], Ct);

        var sync = CreateSync();
        await sync.SyncAsync(Ct);
        DateTime Mtime(string rel) => File.GetLastWriteTimeUtc(Path.Combine(Root, rel));
        Assert.Equal(new DateTime(2026, 10, 15, 23, 50, 0, DateTimeKind.Utc), Mtime("2026/10/15/ClaudeCode_0900.md"));
        Assert.Equal(new DateTime(2026, 10, 16, 2, 30, 0, DateTimeKind.Utc), Mtime("2026/10/16/ClaudeCode_0010.md"));

        // A new prompt moves the date of that day's note along.
        await File.AppendAllLinesAsync(Transcript, [Samples.User("e", "2026-10-16T04:00:00Z", Samples.Text("ešte"))], Ct);
        await sync.SyncAsync(Ct);
        Assert.Equal(new DateTime(2026, 10, 16, 4, 0, 0, DateTimeKind.Utc), Mtime("2026/10/16/ClaudeCode_0010.md"));

        // A wrong date on unchanged content is corrected on the next import.
        File.SetLastWriteTimeUtc(Path.Combine(Root, "2026/10/15/ClaudeCode_0900.md"), DateTime.UtcNow);
        await _db.ResetAsync(Ct); // forget transcript state so it is parsed again
        await CreateSync().SyncAsync(Ct);
        Assert.Equal(new DateTime(2026, 10, 15, 23, 50, 0, DateTimeKind.Utc), Mtime("2026/10/15/ClaudeCode_0900.md"));
    }

    [Fact]
    public async Task Excluded_projects_and_secrets()
    {
        var secretDir = Path.Combine(Claude, "projects", "-secret");
        Directory.CreateDirectory(secretDir);
        await File.WriteAllLinesAsync(Path.Combine(secretDir, "s2.jsonl"),
            [Samples.User("s", "2026-10-15T10:00:00Z", Samples.Text("tajné"), session: "s2", cwd: "/work/secret/app")], Ct);
        await File.WriteAllLinesAsync(Transcript,
            [Samples.User("a", "2026-10-15T10:00:00Z", Samples.Text("token ghp_abcdefghijklmnopqrstuvwxyz0123456789 here"))], Ct);

        await CreateSync("/work/secret").SyncAsync(Ct);
        var file = Assert.Single(NoteFiles());
        var content = await File.ReadAllTextAsync(Path.Combine(Root, file), Ct);
        Assert.Contains("token [REDACTED] here", content);
    }

    [Fact]
    public async Task Cleanup_period_check_and_update()
    {
        var settings = Path.Combine(Claude, "settings.json");
        Assert.True((await CleanupPeriodCheck.CheckAsync(settings, Ct)).IsRisky);

        await File.WriteAllTextAsync(settings, """{ "model": "opus", "cleanupPeriodDays": 20 }""", Ct);
        var status = await CleanupPeriodCheck.CheckAsync(settings, Ct);
        Assert.Equal((20, true, true), (status.Days, status.IsExplicit, status.IsRisky));

        await CleanupPeriodCheck.SetAsync(settings, 365, Ct);
        Assert.False((await CleanupPeriodCheck.CheckAsync(settings, Ct)).IsRisky);
        Assert.Contains("\"model\": \"opus\"", await File.ReadAllTextAsync(settings, Ct));
    }

    private sealed class UtcTimeProvider : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
