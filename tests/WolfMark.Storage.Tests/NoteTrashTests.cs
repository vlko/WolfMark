using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WolfMark.Core.Events;
using WolfMark.Storage.Trash;

namespace WolfMark.Storage.Tests;

/// <summary>ADR 0016: one trash on every system – a folder with the notes, listed, restored and emptied by the app.</summary>
public sealed class NoteTrashTests : IDisposable
{
    private static readonly DateTime At = new(2026, 10, 15, 14, 3, 0);
    private readonly TempDirectory _dir = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 17, 13, 53, 57, TimeSpan.Zero));
    private readonly NoteEventHub _hub = new();
    private readonly NoteTrash _trash;
    private readonly DirectoryEngine _engine;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public NoteTrashTests()
    {
        _trash = new NoteTrash(_dir.Path, _time);
        _engine = new DirectoryEngine(_dir.Path, _trash, _hub, new SelfWriteRegistry(), NullLogger<DirectoryEngine>.Instance);
    }

    public void Dispose() => _dir.Dispose();

    private string[] Notes() =>
        [.. Directory.EnumerateFiles(_dir.Path, "*.md", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(_dir.Path, p).Replace('\\', '/'))
            .Where(p => !p.StartsWith(".wolfmark", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    [Fact]
    public async Task A_deleted_note_is_listed_with_its_place_title_and_time()
    {
        var path = await _engine.CreateAsync(At, "praca", "# Plán na týždeň\n\ntext", Ct);

        await _engine.DeleteAsync(path, Ct);

        Assert.Empty(Notes());
        var note = Assert.Single(_trash.List());
        Assert.Equal("2026/10/15/praca_1403.md", note.OriginalRelativePath);
        Assert.Equal("Plán na týždeň", note.Title);
        Assert.Equal(_time.GetUtcNow().UtcDateTime, note.DeletedAtUtc);
        Assert.EndsWith(Path.Combine("2026", "10", "15", "praca_1403.20261117-135357.md"), note.FullPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restoring_puts_the_note_back_where_it_was()
    {
        var path = await _engine.CreateAsync(At, "praca", "# Späť", Ct);
        await _engine.DeleteAsync(path, Ct);
        var events = _hub.Subscribe();

        var restored = await _engine.RestoreAsync(Assert.Single(_trash.List()), Ct);
        _trash.RemoveEmptyFolders();

        Assert.Equal(path, restored);
        Assert.Equal("# Späť", await File.ReadAllTextAsync(_engine.GetFullPath(restored), Ct));
        Assert.Empty(_trash.List());
        Assert.False(Directory.Exists(Path.Combine(_trash.TrashDirectory, "2026")));
        Assert.True(events.TryRead(out var e));
        Assert.Equal(new NoteCreated(restored, ChangeOrigin.Local), e);
    }

    [Fact]
    public async Task Restoring_next_to_a_note_that_took_the_name_keeps_both()
    {
        var path = await _engine.CreateAsync(At, "praca", "old", Ct);
        await _engine.DeleteAsync(path, Ct);
        await _engine.CreateAsync(At, "praca", "new", Ct);

        var restored = await _engine.RestoreAsync(Assert.Single(_trash.List()), Ct);

        Assert.NotEqual(path, restored);
        Assert.Equal(2, Notes().Length);
        Assert.Equal("new", await File.ReadAllTextAsync(_engine.GetFullPath(path), Ct));
        Assert.Equal("old", await File.ReadAllTextAsync(_engine.GetFullPath(restored), Ct));
    }

    [Fact]
    public async Task The_same_name_deleted_twice_is_kept_twice_the_last_first()
    {
        await _engine.DeleteAsync(await _engine.CreateAsync(At, "praca", "first", Ct), Ct);
        await _engine.DeleteAsync(await _engine.CreateAsync(At, "praca", "second, the same second", Ct), Ct);
        _time.Advance(TimeSpan.FromMinutes(5));
        await _engine.DeleteAsync(await _engine.CreateAsync(At, "praca", "third", Ct), Ct);

        var list = _trash.List();

        Assert.Equal(3, list.Count);
        Assert.Equal("third", list[0].Title);
        Assert.All(list, n => Assert.Equal("2026/10/15/praca_1403.md", n.OriginalRelativePath));
    }

    [Fact]
    public async Task A_note_not_named_by_the_convention_comes_back_under_its_name()
    {
        var folder = Path.Combine(_dir.Path, "ideas");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "someday.md"), "by hand", Ct);
        await _trash.MoveToRecycleBinAsync(Path.Combine(folder, "someday.md"), Ct);
        await File.WriteAllTextAsync(Path.Combine(folder, "someday.md"), "another one", Ct);

        var restored = await _engine.RestoreAsync(Assert.Single(_trash.List()), Ct);

        Assert.Equal("ideas/someday-2.md", restored.RelativePath);
        Assert.Equal("by hand", await File.ReadAllTextAsync(_engine.GetFullPath(restored), Ct));
    }

    [Fact]
    public async Task Emptying_removes_only_what_is_older_than_the_limit()
    {
        await _engine.DeleteAsync(await _engine.CreateAsync(At, "praca", "old", Ct), Ct);
        _time.Advance(TimeSpan.FromDays(40));
        await _engine.DeleteAsync(await _engine.CreateAsync(At.AddDays(1), "praca", "recent", Ct), Ct);

        var removed = await _trash.EmptyAsync(_time.GetUtcNow().UtcDateTime.AddDays(-30), systemBin: null, Ct);

        Assert.Equal(1, removed);
        Assert.Equal("recent", Assert.Single(_trash.List()).Title);
        Assert.Equal(1, await _trash.EmptyAsync(null, systemBin: null, Ct));
        Assert.Empty(_trash.List());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_trash.TrashDirectory));
    }

    [Fact]
    public async Task Where_the_system_has_a_bin_removed_notes_go_there()
    {
        await _engine.DeleteAsync(await _engine.CreateAsync(At, "praca", "keep me somewhere", Ct), Ct);
        var system = new FolderRecycleBin(_dir.Path, Path.Combine(_dir.Path, ".system-bin"));

        await _trash.EmptyAsync(null, system, Ct);

        Assert.Empty(_trash.List());
        var kept = Assert.Single(Directory.EnumerateFiles(system.TrashDirectory, "*.md", SearchOption.AllDirectories));
        Assert.Equal("keep me somewhere", await File.ReadAllTextAsync(kept, Ct));
    }

    [Theory]
    [InlineData("praca_1403.20261117-135357.md", "praca_1403.md", true)]
    [InlineData("praca_1403.20261117-135357-2.md", "praca_1403.md", true)]
    [InlineData("notes.v2.20261117-135357.md", "notes.v2.md", true)]
    [InlineData("praca_1403.md", "praca_1403.md", false)] // put there by an older version
    [InlineData("praca_1403.20261116200501123.md", "praca_1403.md", false)] // an older version's collision name
    public void Names_in_the_trash_carry_the_time_of_deletion(string inTrash, string original, bool stamped)
    {
        var fallback = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var (name, deleted) = NoteTrash.ParseName(inTrash, fallback);

        Assert.Equal(original, name);
        Assert.Equal(stamped ? new DateTime(2026, 11, 17, 13, 53, 57, DateTimeKind.Utc) : fallback, deleted);
    }

    [Fact]
    public async Task The_title_of_a_claude_code_note_is_the_line_after_its_front_matter()
    {
        var folder = Path.Combine(_dir.Path, "2026", "10", "15");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "ClaudeCode_1655.md");
        await File.WriteAllTextAsync(file, "---\nsource: claudecode\nsession: s1\n---\n# billing-api\n\n> Claude Code", Ct);

        await _trash.MoveToRecycleBinAsync(file, Ct);

        Assert.Equal("billing-api", Assert.Single(_trash.List()).Title);
    }
}
