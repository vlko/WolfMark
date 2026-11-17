using WolfMark.App;
using WolfMark.App.ViewModels;

namespace WolfMark.Presentation.Tests;

/// <summary>Settings → Trash, shared by both apps (ADR 0016).</summary>
public sealed class TrashModelTests : IAsyncLifetime
{
    private static readonly DateTime At = DateTime.Today.AddHours(14).AddMinutes(3);
    private readonly string _demo = Directory.CreateTempSubdirectory("wolfmark-trash-").FullName;
    private AppHost _host = null!;
    private MainViewModel _vm = null!;
    private TrashModel _model = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await AppHost.CreateAsync(_demo);
        _vm = new MainViewModel(_host);
        _model = new TrashModel(_vm);
    }

    [Fact]
    public async Task An_empty_trash_says_so()
    {
        await _model.LoadAsync();

        Assert.Empty(_model.Items);
        Assert.False(string.IsNullOrWhiteSpace(_model.Summary));
        Assert.True(_model.RemovesForGood); // a demo never uses the system's bin
    }

    [Fact]
    public async Task A_deleted_note_is_listed_and_restoring_opens_it_again()
    {
        var path = await _host.Engine.CreateAsync(At, "praca", "# Omylom zmazaná", Ct);
        await _vm.DeleteNoteAsync(path.RelativePath);
        await _model.LoadAsync();
        var row = Assert.Single(_model.Items);
        Assert.Equal("Omylom zmazaná", row.Title);
        Assert.Contains("praca", row.Details);

        var message = await _model.RestoreAsync(row);

        Assert.Contains(path.RelativePath, message);
        Assert.Empty(_model.Items);
        Assert.Equal("# Omylom zmazaná", await File.ReadAllTextAsync(_host.Engine.GetFullPath(path), Ct));
        Assert.Equal(path.RelativePath, _vm.Editor.Path?.RelativePath);
    }

    [Fact]
    public async Task Removing_one_and_emptying_take_the_notes_out_of_the_trash()
    {
        for (var i = 0; i < 3; i++)
        {
            var path = await _host.Engine.CreateAsync(At.AddMinutes(i), "praca", $"# Poznámka {i}", Ct);
            await _vm.DeleteNoteAsync(path.RelativePath);
        }

        await _model.LoadAsync();
        Assert.Equal(3, _model.Items.Count);

        Assert.Null(await _model.RemoveAsync(_model.Items[0]));
        Assert.Equal(2, _model.Items.Count);

        Assert.Null(await _model.EmptyAsync());
        Assert.Empty(_model.Items);
        Assert.False(Directory.EnumerateFiles(_host.Trash.TrashDirectory, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Retention_is_off_until_chosen_and_is_saved()
    {
        Assert.Equal(0, _model.RetentionIndex);

        await _model.SetRetentionAsync(2);

        Assert.Equal(90, _host.Settings.Current.TrashRetentionDays);
        Assert.Equal(2, _model.RetentionIndex);
        Assert.Contains("90", TrashModel.RetentionText(90));
        Assert.DoesNotContain("0", TrashModel.RetentionText(0));
    }

    [Theory]
    [InlineData(1, "1 kB")]
    [InlineData(2048, "2 kB")]
    [InlineData(3 * 1048576, "3.0 MB")]
    public void Sizes_are_whole_kilobytes_or_megabytes(long bytes, string expected)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, TrashModel.FormatSize(bytes));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        Directory.Delete(_demo, recursive: true);
    }
}
