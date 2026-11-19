using WolfMark.App;
using WolfMark.App.ViewModels;

namespace WolfMark.Presentation.Tests;

/// <summary>
/// M6-09: the week band redraws once per change of its days (<see cref="WeekStripViewModel.DaysChanged"/>), and a
/// refresh of the week it shows does not blank the days first.
/// </summary>
public sealed class WeekStripTests : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private readonly string _demo = Directory.CreateTempSubdirectory("wolfmark-week-").FullName;
    private AppHost _host = null!;
    private MainViewModel _vm = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await AppHost.CreateAsync(_demo);
        _vm = new MainViewModel(_host);
        _host.StartBackground();
        await _host.DatabaseReady.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        await _host.Engine.CreateAsync(DateTime.Today.AddHours(8), "praca", "# Dnešná", Ct);
        await _host.Indexer.FullScanAsync(null, Ct);
        await _vm.InitializeAsync();
        await _vm.Week.LoadAsync(Today, Today);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        try
        {
            Directory.Delete(_demo, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_refresh_of_the_shown_week_tells_once_and_never_shows_empty_days()
    {
        var todayCell = _vm.Week.Days.Single(d => d.Date == Today);
        Assert.True(todayCell.Day.HasNotes);
        var told = 0;
        var sawEmpty = false;
        _vm.Week.DaysChanged += (_, _) =>
        {
            told++;
            sawEmpty |= !todayCell.Day.HasNotes;
        };

        await _vm.Week.LoadAsync(Today, Today);

        Assert.Equal(1, told);
        Assert.False(sawEmpty);
    }

    [Fact]
    public async Task Another_week_tells_twice_the_numbers_at_once_and_the_notes_when_loaded()
    {
        var told = 0;
        _vm.Week.DaysChanged += (_, _) => told++;

        await _vm.Week.LoadAsync(Today.AddDays(-21), Today);

        Assert.Equal(2, told);
        Assert.DoesNotContain(_vm.Week.Days, d => d.Date == Today);
    }

    [Fact]
    public void Selecting_a_day_tells_once()
    {
        var told = 0;
        _vm.Week.DaysChanged += (_, _) => told++;

        _vm.Week.Select(_vm.Week.Monday);

        Assert.Equal(1, told);
        Assert.True(_vm.Week.Days[0].IsSelected);
    }
}
