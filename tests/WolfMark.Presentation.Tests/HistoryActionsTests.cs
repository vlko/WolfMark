using WolfMark.App;
using WolfMark.App.ViewModels;
using WolfMark.Core.Notes;

namespace WolfMark.Presentation.Tests;

/// <summary>M5-04: the history context menu of both apps – link, change tag, delete (to the trash).</summary>
public sealed class HistoryActionsTests : IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 11, 16, 14, 3, 0);
    private readonly string _demo = Directory.CreateTempSubdirectory("wolfmark-history-").FullName;
    private AppHost _host = null!;
    private MainViewModel _vm = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await AppHost.CreateAsync(_demo);
        _vm = new MainViewModel(_host);
    }

    [Fact]
    public void Link_is_the_path_without_the_extension()
    {
        Assert.Equal("[[2026/11/16/praca_1403]]", MainViewModel.NoteLink("2026/11/16/praca_1403.md"));
    }

    [Fact]
    public async Task Changing_the_tag_of_a_note_in_the_history_renames_its_file()
    {
        var path = await _host.Engine.CreateAsync(At, "praca", "# Zo zoznamu", Ct);

        await _vm.ChangeNoteTagAsync(path.RelativePath, "rodina");

        Assert.False(File.Exists(_host.Engine.GetFullPath(path)));
        Assert.Equal("# Zo zoznamu", await File.ReadAllTextAsync(_host.Engine.GetFullPath("2026/11/16/rodina_1403.md"), Ct));
    }

    [Fact]
    public async Task Changing_the_tag_of_the_open_note_keeps_the_editor_on_it()
    {
        var path = await _host.Engine.CreateAsync(At, "praca", "# Otvorená", Ct);
        await _vm.OpenNoteAsync(path.RelativePath);

        await _vm.ChangeNoteTagAsync(path.RelativePath, null);

        Assert.Equal("2026/11/16/1403.md", _vm.Editor.Path?.RelativePath);
        _vm.Editor.OnTextEdited("# Otvorená a upravená");
        await _vm.Editor.FlushAsync();
        Assert.Equal("# Otvorená a upravená", await File.ReadAllTextAsync(_host.Engine.GetFullPath("2026/11/16/1403.md"), Ct));
    }

    /// <summary>A demo keeps its trash in the demo folder on every OS (the real app uses the Windows Recycle Bin there).</summary>
    [Fact]
    public async Task Deleting_moves_the_note_to_the_trash()
    {
        var path = await _host.Engine.CreateAsync(At, "praca", "# Preč", Ct);

        await _vm.DeleteNoteAsync(path.RelativePath);

        Assert.False(File.Exists(_host.Engine.GetFullPath(path)));
        var trashed = Directory.EnumerateFiles(Path.Combine(_host.Root, ".wolfmark", "trash"), "*.md", SearchOption.AllDirectories);
        Assert.Contains(trashed, f => File.ReadAllText(f) == "# Preč");
    }

    [Fact]
    public async Task Deleting_the_open_note_clears_the_editor_without_recreating_the_file()
    {
        var path = await _host.Engine.CreateAsync(At, "praca", "# Otvorená", Ct);
        await _vm.OpenNoteAsync(path.RelativePath);

        await _vm.DeleteNoteAsync(path.RelativePath);
        await _vm.Editor.FlushAsync();

        Assert.False(File.Exists(_host.Engine.GetFullPath(path)));
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        Directory.Delete(_demo, recursive: true);
    }
}
