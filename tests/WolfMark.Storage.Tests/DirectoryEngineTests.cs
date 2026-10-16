using System.Diagnostics;
using WolfMark.Core.Events;
using WolfMark.Core.Notes;

namespace WolfMark.Storage.Tests;

public sealed class DirectoryEngineTests
{
    private static readonly DateTime At = new(2026, 10, 15, 14, 3, 12);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_writes_file_in_day_directory_and_publishes_event()
    {
        using var f = new EngineFixture();
        var path = await f.Engine.CreateAsync(At, "praca", "# Ahoj", Ct);

        Assert.Equal("2026/10/15/praca_1403.md", path.RelativePath);
        Assert.Equal("# Ahoj", await File.ReadAllTextAsync(Path.Combine(f.Root, "2026", "10", "15", "praca_1403.md"), Ct));
        Assert.IsType<NoteCreated>(Assert.Single(f.DrainEvents()));
    }

    [Fact]
    public async Task Collisions_add_seconds_then_suffix()
    {
        using var f = new EngineFixture();
        var p1 = await f.Engine.CreateAsync(At, "praca", "1", Ct);
        var p2 = await f.Engine.CreateAsync(At, "praca", "2", Ct);
        var p3 = await f.Engine.CreateAsync(At, "praca", "3", Ct);
        var p4 = await f.Engine.CreateAsync(At, null, "4", Ct);

        Assert.Equal("praca_1403.md", p1.FileName);
        Assert.Equal("praca_140312.md", p2.FileName);
        Assert.Equal("praca_140312-2.md", p3.FileName);
        Assert.Equal("1403.md", p4.FileName);
    }

    [Fact]
    public async Task Save_is_atomic_and_leaves_no_temp_file()
    {
        using var f = new EngineFixture();
        var path = await f.Engine.CreateAsync(At, null, "old", Ct);
        await f.Engine.SaveAsync(path, "new content ľščťž", Ct);

        var full = f.Engine.GetFullPath(path);
        Assert.Equal("new content ľščťž", await File.ReadAllTextAsync(full, Ct));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(full)!));
    }

    [Fact]
    public async Task Interrupted_write_keeps_original_intact()
    {
        using var f = new EngineFixture();
        var path = await f.Engine.CreateAsync(At, null, "original", Ct);
        var full = f.Engine.GetFullPath(path);

        // Simulate a crash: a half-written temp file is left behind, the target was never replaced.
        await File.WriteAllTextAsync(AtomicFile.GetTempPath(full), "half-writ", Ct);
        Assert.Equal("original", await File.ReadAllTextAsync(full, Ct));

        // Cancelled write: target untouched, temp removed.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Engine.SaveAsync(path, "new", cts.Token));
        Assert.Equal("original", await File.ReadAllTextAsync(full, Ct));

        // Temp files are never enumerated as notes.
        var files = await f.Engine.EnumerateAsync(Ct).ToListAsync(Ct);
        Assert.Equal(path, Assert.Single(files).Path);
    }

    [Fact]
    public async Task Change_tag_renames_and_keeps_time()
    {
        using var f = new EngineFixture();
        var path = await f.Engine.CreateAsync(new DateTime(2026, 10, 15, 9, 27, 0), null, "x", Ct);
        f.DrainEvents();

        var renamed = await f.Engine.ChangeTagAsync(path, "praca", Ct);
        Assert.Equal("2026/10/15/praca_0927.md", renamed.RelativePath);
        Assert.False(File.Exists(f.Engine.GetFullPath(path)));
        Assert.True(File.Exists(f.Engine.GetFullPath(renamed)));
        var e = Assert.IsType<NoteRenamed>(Assert.Single(f.DrainEvents()));
        Assert.Equal(path, e.OldPath);

        var back = await f.Engine.ChangeTagAsync(renamed, null, Ct);
        Assert.Equal("0927.md", back.FileName);
    }

    [Fact]
    public async Task Change_tag_resolves_collision()
    {
        using var f = new EngineFixture();
        await f.Engine.CreateAsync(At, "praca", "a", Ct);
        var other = await f.Engine.CreateAsync(At, null, "b", Ct);
        var renamed = await f.Engine.ChangeTagAsync(other, "praca", Ct);
        Assert.Equal("praca_140300.md", renamed.FileName); // seconds unknown for HHmm names → 00
    }

    [Fact]
    public async Task Delete_moves_to_trash()
    {
        using var f = new EngineFixture();
        var path = await f.Engine.CreateAsync(At, null, "bye", Ct);
        await f.Engine.DeleteAsync(path, Ct);

        Assert.False(File.Exists(f.Engine.GetFullPath(path)));
        var trashed = Path.Combine(f.RecycleBin.TrashDirectory, "2026", "10", "15", "1403.md");
        Assert.Equal("bye", await File.ReadAllTextAsync(trashed, Ct));
        Assert.Empty(await f.Engine.EnumerateAsync(Ct).ToListAsync(Ct));
    }

    [Fact]
    public async Task Enumeration_includes_non_standard_and_skips_internal_folders()
    {
        using var f = new EngineFixture();
        Directory.CreateDirectory(Path.Combine(f.Root, "2026", "10", "15", "assets"));
        Directory.CreateDirectory(Path.Combine(f.Root, ".wolfmark"));
        await File.WriteAllTextAsync(Path.Combine(f.Root, "2026", "10", "15", "praca_1403.md"), "a", Ct);
        await File.WriteAllTextAsync(Path.Combine(f.Root, "2026", "10", "15", "nakupy.md"), "b", Ct);
        await File.WriteAllTextAsync(Path.Combine(f.Root, "2026", "10", "15", "assets", "x.md"), "c", Ct);
        await File.WriteAllTextAsync(Path.Combine(f.Root, ".wolfmark", "y.md"), "d", Ct);
        await File.WriteAllTextAsync(Path.Combine(f.Root, "README.md"), "e", Ct);
        await File.WriteAllTextAsync(Path.Combine(f.Root, "2026", "10", "15", "pic.png"), "f", Ct);

        var files = (await f.Engine.EnumerateAsync(Ct).ToListAsync(Ct)).Select(x => x.Path.RelativePath).Order().ToList();
        Assert.Equal(["2026/10/15/nakupy.md", "2026/10/15/praca_1403.md", "README.md"], files);
    }

    [Fact]
    public async Task Month_enumeration_is_scoped()
    {
        using var f = new EngineFixture();
        await f.Engine.CreateAsync(new DateTime(2026, 10, 1, 8, 0, 0), null, "a", Ct);
        await f.Engine.CreateAsync(new DateTime(2026, 11, 1, 8, 0, 0), null, "b", Ct);
        var october = await f.Engine.EnumerateMonthAsync(2026, 10, Ct).ToListAsync(Ct);
        Assert.Equal("2026/10/01/0800.md", Assert.Single(october).Path.RelativePath);
    }

    [Fact]
    public async Task Enumerates_10000_files_quickly()
    {
        using var f = new EngineFixture();
        for (var d = 0; d < 100; d++)
        {
            var day = new DateOnly(2026, 1, 1).AddDays(d);
            var dir = f.Engine.GetDayDirectory(day);
            Directory.CreateDirectory(dir);
            for (var i = 0; i < 100; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(dir, $"{i / 60:D2}{i % 60:D2}.md"), "x", Ct);
            }
        }

        var sw = Stopwatch.StartNew();
        var count = 0;
        await foreach (var _ in f.Engine.EnumerateAsync(Ct))
        {
            count++;
        }

        sw.Stop();
        Assert.Equal(10_000, count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Enumeration took {sw.Elapsed}");
    }

    [Fact]
    public async Task Save_asset_returns_relative_link()
    {
        using var f = new EngineFixture();
        var day = new DateOnly(2026, 10, 15);
        var l1 = await f.Engine.SaveAssetAsync(day, At, "png", new byte[] { 1, 2, 3 }, Ct);
        var l2 = await f.Engine.SaveAssetAsync(day, At, ".png", new byte[] { 4 }, Ct);
        Assert.Equal("assets/1403-01.png", l1);
        Assert.Equal("assets/1403-02.png", l2);
    }

    [Fact]
    public void Placeholder_attributes_are_detected()
    {
        Assert.True(CloudFileAttributes.IsPlaceholder(FileAttributes.Archive | CloudFileAttributes.RecallOnDataAccess));
        Assert.True(CloudFileAttributes.IsPlaceholder(FileAttributes.Offline));
        Assert.False(CloudFileAttributes.IsPlaceholder(FileAttributes.Archive | CloudFileAttributes.Pinned));
    }

    [Fact]
    public void Decode_strips_bom()
    {
        Assert.Equal("ab", DirectoryEngine.Decode([0xEF, 0xBB, 0xBF, (byte)'a', (byte)'b']));
    }
}

public sealed class NotesMoverTests
{
    [Fact]
    public async Task Moves_notes_and_recycles_old_folder()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "old");
        var target = Path.Combine(dir.Path, "new");
        var trash = Path.Combine(dir.Path, "trash");
        Directory.CreateDirectory(Path.Combine(source, "2026", "10", "15", "assets"));
        Directory.CreateDirectory(Path.Combine(source, ".wolfmark"));
        await File.WriteAllTextAsync(Path.Combine(source, "2026", "10", "15", "praca_1403.md"), "a", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(source, "2026", "10", "15", "assets", "1403-01.png"), "png", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(source, ".wolfmark", "tags.json"), "{}", TestContext.Current.CancellationToken);

        var result = await NotesMover.MoveAsync(source, target, new Trash.FolderRecycleBin(dir.Path, trash), null, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.FilesCopied);
        Assert.True(File.Exists(Path.Combine(target, "2026", "10", "15", "praca_1403.md")));
        Assert.True(File.Exists(Path.Combine(target, ".wolfmark", "tags.json")));
        Assert.False(Directory.Exists(source));
        Assert.True(Directory.Exists(Path.Combine(trash, "old")));
    }

    [Fact]
    public async Task Refuses_target_inside_source()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(dir.Path, "a"));
        await Assert.ThrowsAsync<IOException>(() =>
            NotesMover.MoveAsync(Path.Combine(dir.Path, "a"), Path.Combine(dir.Path, "a", "b"), new Trash.FolderRecycleBin(dir.Path), null, TestContext.Current.CancellationToken));
        Assert.True(Directory.Exists(Path.Combine(dir.Path, "a")));
    }
}
