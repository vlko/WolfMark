using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WolfMark.Core.Settings;
using WolfMark.Storage.Settings;
using WolfMark.Storage.Trash;

namespace WolfMark.Storage.Tests;

public sealed class PortableModeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void No_marker_means_standard_mode()
    {
        using var app = new TempDirectory();
        Assert.Null(PortableMode.Detect(app.Path));
    }

    [Fact]
    public async Task Relative_data_directory_resolves_against_the_app_folder()
    {
        using var app = new TempDirectory();
        await File.WriteAllTextAsync(PortableMode.MarkerPath(app.Path), """{ "version": 1, "dataDirectory": "Data" }""", Ct);
        Assert.Equal(Path.Combine(app.Path, "Data"), PortableMode.Detect(app.Path));
    }

    [Fact]
    public async Task Absolute_data_directory_and_empty_marker()
    {
        using var app = new TempDirectory();
        var elsewhere = Path.Combine(app.Path, "x", "y");
        await File.WriteAllTextAsync(PortableMode.MarkerPath(app.Path), JsonSerializer.Serialize(new PortableMarker { DataDirectory = elsewhere }, WolfMarkJsonContext.Default.PortableMarker), Ct);
        Assert.Equal(elsewhere, PortableMode.Detect(app.Path));

        await File.WriteAllTextAsync(PortableMode.MarkerPath(app.Path), "{}", Ct);
        Assert.Equal(PortableMode.DefaultDataDirectory(app.Path), PortableMode.Detect(app.Path));
    }

    [Fact]
    public async Task Damaged_marker_fails_instead_of_falling_back()
    {
        using var app = new TempDirectory();
        await File.WriteAllTextAsync(PortableMode.MarkerPath(app.Path), "{ not json", Ct);
        Assert.Throws<InvalidDataException>(() => PortableMode.Detect(app.Path));
    }

    [Fact]
    public async Task Marker_stores_a_relative_path_under_the_app_folder()
    {
        using var app = new TempDirectory();
        await PortableMode.WriteMarkerAsync(app.Path, PortableMode.DefaultDataDirectory(app.Path), Ct);
        var marker = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(PortableMode.MarkerPath(app.Path), Ct), WolfMarkJsonContext.Default.PortableMarker);
        Assert.Equal(PortableMode.DefaultDataDirectoryName, marker!.DataDirectory);
    }

    [Fact]
    public void Writable_folder_is_available()
    {
        using var app = new TempDirectory();
        Assert.Equal(PortableUnavailableReason.None, PortableMode.CheckAvailability(app.Path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(app.Path));
    }

    [Fact]
    public async Task Portable_root_is_relative_to_the_data_folder()
    {
        using var data = new TempDirectory();
        var store = new LocalSettingsStore(LocalPaths.ForPortable(data.Path), NullLogger<LocalSettingsStore>.Instance);
        await store.LoadAsync(Ct);
        Assert.Equal(Path.Combine(data.Path, "notes"), store.ResolveRoot());

        store.Current.RootDirectory = "notes";
        Assert.Equal(Path.Combine(data.Path, "notes"), store.ResolveRoot());

        var other = Path.Combine(data.Path, "elsewhere");
        Assert.Equal("elsewhere", store.ToStoredRoot(other));
        var outside = Path.GetFullPath(Path.Combine(data.Path, "..", "outside"));
        Assert.Equal(outside, store.ToStoredRoot(outside));
    }

    [Fact]
    public void Standard_store_keeps_absolute_root()
    {
        using var dir = new TempDirectory();
        var store = new LocalSettingsStore(new LocalPaths(dir.Path), NullLogger<LocalSettingsStore>.Instance);
        var root = Path.Combine(dir.Path, "n");
        store.Current.RootDirectory = root;
        Assert.Equal(root, store.ResolveRoot());
        Assert.Equal(root, store.ToStoredRoot(root));
    }
}

public sealed class PortableMigratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Setup : IDisposable
    {
        private readonly TempDirectory _dir = new();

        public Setup()
        {
            App = Path.Combine(_dir.Path, "app");
            Notes = Path.Combine(_dir.Path, "notes");
            Local = new LocalPaths(Path.Combine(_dir.Path, "local"));
            Directory.CreateDirectory(App);
            Directory.CreateDirectory(Path.Combine(Notes, "2026", "10", "15"));
            Directory.CreateDirectory(Path.Combine(Notes, ".wolfmark"));
            File.WriteAllText(Path.Combine(Notes, "2026", "10", "15", "praca_1403.md"), "# A");
            File.WriteAllText(Path.Combine(Notes, ".wolfmark", "tags.json"), "{}");
            Trash = new FolderRecycleBin(_dir.Path, Path.Combine(_dir.Path, "trash"));
        }

        public string App { get; }

        public string Notes { get; }

        public LocalPaths Local { get; }

        public FolderRecycleBin Trash { get; }

        public string Data => PortableMode.DefaultDataDirectory(App);

        public List<string> Backups { get; } = [];

        public PortableMigrator.Source Source(string notes, LocalPaths paths, string? root = null) => new(
            paths,
            notes,
            JsonSerializer.Serialize(new LocalSettings { RootDirectory = root ?? notes, Theme = AppTheme.Dark }, WolfMarkJsonContext.Default.LocalSettings),
            (path, ct) =>
            {
                Backups.Add(path);
                return File.WriteAllTextAsync(path, "db", ct);
            });

        public void Dispose() => _dir.Dispose();
    }

    private static LocalSettings ReadSettings(string path) =>
        JsonSerializer.Deserialize(File.ReadAllBytes(path), WolfMarkJsonContext.Default.LocalSettings)!;

    [Fact]
    public async Task Enable_copies_everything_and_writes_the_marker_last()
    {
        using var s = new Setup();
        var result = await PortableMigrator.EnableAsync(s.App, s.Data, s.Source(s.Notes, s.Local), null, null, Ct);

        Assert.Equal(2, result.FilesCopied);
        Assert.Null(result.CleanupError);
        var target = LocalPaths.ForPortable(s.Data);
        Assert.Equal("# A", await File.ReadAllTextAsync(Path.Combine(s.Data, "notes", "2026", "10", "15", "praca_1403.md"), Ct));
        Assert.True(File.Exists(Path.Combine(s.Data, "notes", ".wolfmark", "tags.json")));
        Assert.Equal([target.IndexDatabase], s.Backups);
        var settings = ReadSettings(target.SettingsFile);
        Assert.Equal("notes", settings.RootDirectory);
        Assert.Equal(AppTheme.Dark, settings.Theme);
        Assert.Equal(s.Data, PortableMode.Detect(s.App));
        Assert.True(File.Exists(Path.Combine(s.Notes, "2026", "10", "15", "praca_1403.md"))); // copy: originals stay
    }

    [Fact]
    public async Task Enable_with_move_recycles_the_original_notes()
    {
        using var s = new Setup();
        await PortableMigrator.EnableAsync(s.App, s.Data, s.Source(s.Notes, s.Local), s.Trash, null, Ct);

        Assert.False(Directory.Exists(s.Notes));
        Assert.True(File.Exists(Path.Combine(s.Trash.TrashDirectory, "notes", "2026", "10", "15", "praca_1403.md")));
        Assert.True(File.Exists(Path.Combine(s.Data, "notes", "2026", "10", "15", "praca_1403.md")));
    }

    [Fact]
    public async Task Enable_refuses_a_folder_that_is_not_empty()
    {
        using var s = new Setup();
        Directory.CreateDirectory(s.Data);
        await File.WriteAllTextAsync(Path.Combine(s.Data, "old.md"), "x", Ct);

        await Assert.ThrowsAsync<IOException>(() => PortableMigrator.EnableAsync(s.App, s.Data, s.Source(s.Notes, s.Local), null, null, Ct));
        Assert.Null(PortableMode.Detect(s.App));
        Assert.Equal("x", await File.ReadAllTextAsync(Path.Combine(s.Data, "old.md"), Ct));
    }

    [Fact]
    public async Task Failed_enable_switches_nothing()
    {
        using var s = new Setup();
        var source = s.Source(s.Notes, s.Local) with { BackupIndex = (_, _) => throw new IOException("disk full") };

        await Assert.ThrowsAsync<IOException>(() => PortableMigrator.EnableAsync(s.App, s.Data, source, s.Trash, null, Ct));
        Assert.Null(PortableMode.Detect(s.App));
        Assert.False(Directory.Exists(s.Data));
        Assert.True(File.Exists(Path.Combine(s.Notes, "2026", "10", "15", "praca_1403.md")));
    }

    [Fact]
    public async Task Enable_refuses_notes_that_contain_the_app()
    {
        using var s = new Setup();
        var app = Path.Combine(s.Notes, "tools", "wolfmark");
        Directory.CreateDirectory(app);

        await Assert.ThrowsAsync<IOException>(() =>
            PortableMigrator.EnableAsync(app, PortableMode.DefaultDataDirectory(app), s.Source(s.Notes, s.Local), null, null, Ct));
        Assert.Null(PortableMode.Detect(app));
        Assert.False(Directory.Exists(PortableMode.DefaultDataDirectory(app)));
    }

    [Fact]
    public async Task Disable_copies_back_replaces_changed_notes_and_keeps_the_portable_folder()
    {
        using var s = new Setup();
        await PortableMigrator.EnableAsync(s.App, s.Data, s.Source(s.Notes, s.Local), null, null, Ct);
        var portable = LocalPaths.ForPortable(s.Data);
        var portableNotes = Path.Combine(s.Data, "notes");
        await File.WriteAllTextAsync(Path.Combine(portableNotes, "2026", "10", "15", "praca_1403.md"), "# A edited on the USB drive", Ct);
        await File.WriteAllTextAsync(Path.Combine(portableNotes, "2026", "10", "15", "praca_1500.md"), "# B", Ct);

        var result = await PortableMigrator.DisableAsync(s.App, s.Source(portableNotes, portable, "notes"), s.Local, s.Notes, s.Trash, null, Ct);

        Assert.Equal(2, result.FilesCopied);
        Assert.Equal("# A edited on the USB drive", await File.ReadAllTextAsync(Path.Combine(s.Notes, "2026", "10", "15", "praca_1403.md"), Ct));
        Assert.Equal("# B", await File.ReadAllTextAsync(Path.Combine(s.Notes, "2026", "10", "15", "praca_1500.md"), Ct));
        Assert.Equal("# A", await File.ReadAllTextAsync(Path.Combine(s.Trash.TrashDirectory, "notes", "2026", "10", "15", "praca_1403.md"), Ct));
        Assert.Equal(s.Notes, ReadSettings(s.Local.SettingsFile).RootDirectory);
        Assert.Contains(s.Local.IndexDatabase, s.Backups);
        Assert.Null(PortableMode.Detect(s.App));
        Assert.True(File.Exists(Path.Combine(portableNotes, "2026", "10", "15", "praca_1500.md")));
    }

    [Fact]
    public async Task Standard_notes_root_comes_from_the_profile_settings()
    {
        using var s = new Setup();
        Directory.CreateDirectory(s.Local.BaseDirectory);
        await File.WriteAllTextAsync(s.Local.SettingsFile, JsonSerializer.Serialize(new LocalSettings { RootDirectory = s.Notes }, WolfMarkJsonContext.Default.LocalSettings), Ct);
        Assert.Equal(s.Notes, PortableMigrator.StandardNotesRoot(s.Local));
    }
}

public sealed class NotesCopyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Copy_keeps_the_source_and_skips_identical_files()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "a");
        var target = Path.Combine(dir.Path, "b");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(source, "x.md"), "same", Ct);
        await File.WriteAllTextAsync(Path.Combine(target, "x.md"), "same", Ct);
        await File.WriteAllTextAsync(Path.Combine(source, "y.md"), "new", Ct);

        var result = await NotesMover.CopyAsync(source, target, null, null, Ct);

        Assert.Equal((1, 1), (result.FilesCopied, result.FilesSkipped));
        Assert.True(File.Exists(Path.Combine(source, "y.md")));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(target, "y.md"), Ct));
    }

    [Fact]
    public async Task Same_size_but_other_content_is_a_conflict()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "a");
        var target = Path.Combine(dir.Path, "b");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(source, "x.md"), "abcd", Ct);
        await File.WriteAllTextAsync(Path.Combine(target, "x.md"), "wxyz", Ct);

        await Assert.ThrowsAsync<IOException>(() => NotesMover.CopyAsync(source, target, null, null, Ct));
        Assert.Equal("wxyz", await File.ReadAllTextAsync(Path.Combine(target, "x.md"), Ct));
    }

    [Fact]
    public async Task Refuses_source_inside_target()
    {
        using var dir = new TempDirectory();
        var source = Path.Combine(dir.Path, "a", "b");
        Directory.CreateDirectory(source);
        await Assert.ThrowsAsync<IOException>(() => NotesMover.CopyAsync(source, Path.Combine(dir.Path, "a"), null, null, Ct));
    }
}
