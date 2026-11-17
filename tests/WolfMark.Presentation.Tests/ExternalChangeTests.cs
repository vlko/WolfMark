using WolfMark.App;
using WolfMark.App.ViewModels;
using WolfMark.Core.Events;
using WolfMark.Core.Notes;

namespace WolfMark.Presentation.Tests;

/// <summary>
/// M4-08: a note changed on disk by someone else (OneDrive, another editor) while it has unsaved edits. The autosave
/// must not overwrite that version – the user picks one, and leaving the note keeps both.
/// </summary>
public sealed class ExternalChangeTests : IAsyncLifetime
{
    private static readonly DateTime At = new(2026, 11, 16, 14, 3, 0);
    private readonly string _demo = Directory.CreateTempSubdirectory("wolfmark-external-").FullName;
    private AppHost _host = null!;
    private NoteEditorViewModel _editor = null!;
    private NotePath _path = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string FullPath => _host.Engine.GetFullPath(_path);

    public async ValueTask InitializeAsync()
    {
        _host = await AppHost.CreateAsync(_demo);
        _editor = new NoteEditorViewModel(_host);
        _path = await _host.Engine.CreateAsync(At, "praca", "# Pôvodná", Ct);
        await _editor.OpenAsync(_path);
    }

    private Task ChangeOnDiskAsync(string content) => File.WriteAllTextAsync(FullPath, content, Ct);

    private Task<string> OnDiskAsync() => File.ReadAllTextAsync(FullPath, Ct);

    [Fact]
    public async Task Saving_does_not_overwrite_a_change_the_watcher_has_not_reported_yet()
    {
        await ChangeOnDiskAsync("# Z iného počítača");

        _editor.OnTextEdited("# Moja");
        await _editor.FlushAsync();

        Assert.Equal("# Z iného počítača", await OnDiskAsync());
        Assert.True(_editor.ExternalChangePending);
        Assert.Equal(SaveState.Pending, _editor.SaveState);
    }

    [Fact]
    public async Task Saving_waits_while_a_reported_change_is_unanswered()
    {
        _editor.OnTextEdited("# Moja");
        await ChangeOnDiskAsync("# Z iného počítača");
        await _editor.OnNoteEventAsync(new NoteChanged(_path, ChangeOrigin.External));

        _editor.OnTextEdited("# Moja, ďalší znak");
        await _editor.FlushAsync();

        Assert.True(_editor.ExternalChangePending);
        Assert.Equal("# Z iného počítača", await OnDiskAsync());
    }

    [Fact]
    public async Task Keep_mine_overwrites_and_saving_works_again()
    {
        await ChangeOnDiskAsync("# Z iného počítača");
        _editor.OnTextEdited("# Moja");
        await _editor.FlushAsync();

        await _editor.KeepMineAsync();

        Assert.False(_editor.ExternalChangePending);
        Assert.Equal("# Moja", await OnDiskAsync());

        _editor.OnTextEdited("# Moja 2");
        await _editor.FlushAsync();
        Assert.Equal("# Moja 2", await OnDiskAsync());
    }

    [Fact]
    public async Task Reload_shows_the_version_on_disk_and_saving_works_again()
    {
        await ChangeOnDiskAsync("# Z iného počítača");
        _editor.OnTextEdited("# Moja");
        await _editor.FlushAsync();

        await _editor.ReloadFromDiskAsync();

        Assert.False(_editor.ExternalChangePending);
        Assert.Equal("# Z iného počítača", _editor.Text);

        _editor.OnTextEdited("# Z iného počítača, doplnené");
        await _editor.FlushAsync();
        Assert.Equal("# Z iného počítača, doplnené", await OnDiskAsync());
    }

    [Fact]
    public async Task Leaving_the_note_with_an_unanswered_change_keeps_both_versions()
    {
        var day = Path.GetDirectoryName(FullPath)!;
        var before = Directory.GetFiles(day, "*.md"); // the demo has notes of its own there
        await ChangeOnDiskAsync("# Z iného počítača");
        _editor.OnTextEdited("# Moja");
        await _editor.FlushAsync();

        await _editor.NewAsync(null);

        Assert.Equal("# Z iného počítača", await OnDiskAsync());
        var added = Directory.GetFiles(day, "*.md").Except(before);
        Assert.Equal("# Moja", await File.ReadAllTextAsync(Assert.Single(added), Ct));
    }

    [Fact]
    public async Task A_change_without_unsaved_edits_is_loaded_silently()
    {
        await ChangeOnDiskAsync("# Z iného počítača");

        await _editor.OnNoteEventAsync(new NoteChanged(_path, ChangeOrigin.External));

        Assert.False(_editor.ExternalChangePending);
        Assert.Equal("# Z iného počítača", _editor.Text);
    }

    [Fact]
    public async Task A_note_deleted_on_disk_is_written_again_by_the_next_save()
    {
        File.Delete(FullPath);

        _editor.OnTextEdited("# Stále tu");
        await _editor.FlushAsync();

        Assert.Equal("# Stále tu", await OnDiskAsync());
        Assert.False(_editor.ExternalChangePending);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        Directory.Delete(_demo, recursive: true);
    }
}
