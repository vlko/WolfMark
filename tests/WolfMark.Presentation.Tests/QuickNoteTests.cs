using WolfMark.App;
using WolfMark.App.ViewModels;

namespace WolfMark.Presentation.Tests;

/// <summary>M8-01: the quick note window's behaviour, shared by both apps.</summary>
public sealed class QuickNoteTests : IAsyncLifetime
{
    private readonly string _demo = Directory.CreateTempSubdirectory("wolfmark-quick-").FullName;
    private AppHost _host = null!;
    private MainViewModel _main = null!;
    private QuickNoteViewModel _quick = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await AppHost.CreateAsync(_demo);
        _main = new MainViewModel(_host);
        _quick = new QuickNoteViewModel(_main);
    }

    [Fact]
    public async Task The_shortcut_starts_a_new_note_and_nothing_is_created_until_text_is_typed()
    {
        var before = Directory.GetFiles(_host.Root, "*.md", SearchOption.AllDirectories).Length;

        Assert.True(await _quick.StartAsync(windowVisible: false));
        await _quick.SaveAsync();

        Assert.False(_quick.Note.IsPersisted);
        Assert.Equal(before, Directory.GetFiles(_host.Root, "*.md", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task Pressing_the_shortcut_again_keeps_the_text_being_written()
    {
        await _quick.StartAsync(windowVisible: false);
        _quick.Note.OnTextEdited("rozpísané");

        Assert.False(await _quick.StartAsync(windowVisible: true));

        Assert.Equal("rozpísané", _quick.Note.Text);
    }

    [Fact]
    public async Task A_hidden_window_starts_a_new_note_and_the_previous_one_is_saved()
    {
        await _quick.StartAsync(windowVisible: false);
        _quick.Note.OnTextEdited("prvá");
        var first = _quick.Note;

        Assert.True(await _quick.StartAsync(windowVisible: false));

        Assert.Equal(string.Empty, first.Text);
        Assert.Contains(Directory.GetFiles(_host.Root, "*.md", SearchOption.AllDirectories), f => File.ReadAllText(f) == "prvá");
    }

    [Fact]
    public async Task Continuing_in_the_main_window_opens_the_saved_note_there_in_full_view()
    {
        await _quick.StartAsync(windowVisible: false);
        _quick.Note.OnTextEdited("pokračovanie");

        await _quick.OpenInMainAsync();

        Assert.True(_main.IsFullView);
        Assert.Equal(_quick.Note.Path?.RelativePath, _main.Editor.Path?.RelativePath);
        Assert.Equal("pokračovanie", _main.Editor.Text);
        Assert.Equal("pokračovanie", await File.ReadAllTextAsync(_host.Engine.GetFullPath(_main.Editor.Path!), Ct));
    }

    [Theory]
    [InlineData(0, 0, 1920, 1040, 1.0, 640, 220, 640, 380)]
    [InlineData(1920, 0, 2560, 1400, 1.5, 2720, 276, 960, 570)] // second monitor at 150 %
    [InlineData(0, 0, 600, 300, 1.0, 0, 0, 600, 300)] // smaller than the window: the whole work area
    public void The_window_sits_centred_in_the_upper_third(int ax, int ay, int aw, int ah, double scale, int x, int y, int w, int h)
    {
        Assert.Equal((x, y, w, h), QuickNoteViewModel.Place(ax, ay, aw, ah, scale));
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        Directory.Delete(_demo, recursive: true);
    }
}
