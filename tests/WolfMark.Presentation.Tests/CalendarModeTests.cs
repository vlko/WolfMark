using WolfMark.App;
using WolfMark.App.ViewModels;
using WolfMark.Core.Calendar;

namespace WolfMark.Presentation.Tests;

/// <summary>
/// M6-08: the month is a view of its own – month and history, no note (as on Android). Picking a note brings the
/// week view back on that note's week; "Today" brings the app back to how it starts, except for week/month.
/// </summary>
public sealed class CalendarModeTests : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private static readonly DateTime LongAgo = DateTime.Today.AddDays(-40).AddHours(9);
    private readonly string _demo = Directory.CreateTempSubdirectory("wolfmark-month-").FullName;
    private AppHost _host = null!;
    private MainViewModel _vm = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _host = await AppHost.CreateAsync(_demo);
        // A settings file of an older version that was closed with the month open.
        _host.Settings.Current.CalendarExpanded = true;
        _vm = new MainViewModel(_host);
        _host.StartBackground();
        await _host.DatabaseReady.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        await _host.Indexer.FullScanAsync(null, Ct);
        await _vm.InitializeAsync();
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
    public void The_app_starts_on_the_week_whatever_was_remembered()
    {
        Assert.False(_vm.IsCalendarExpanded);
    }

    [Fact]
    public async Task A_note_picked_in_the_month_view_brings_back_the_week_view_on_its_week()
    {
        var old = await _host.Engine.CreateAsync(LongAgo, "praca", "# Dávna poznámka", Ct);
        await _host.Indexer.FullScanAsync(null, Ct);
        await _vm.ToggleCalendarAsync();
        Assert.True(_vm.IsCalendarExpanded);

        await _vm.OpenNoteAsync(old.RelativePath);

        var day = DateOnly.FromDateTime(LongAgo);
        Assert.False(_vm.IsCalendarExpanded);
        Assert.Equal(old.RelativePath, _vm.Editor.Path?.RelativePath);
        Assert.Equal(day, _vm.SelectedDay);
        Assert.Equal(CalendarMath.WeekOf(day).Monday, _vm.Week.Monday);
    }

    [Fact]
    public async Task A_note_of_the_day_already_selected_also_leaves_the_month_view()
    {
        var today = await _host.Engine.CreateAsync(DateTime.Today.AddHours(8), "praca", "# Dnešná", Ct);
        await _host.Indexer.FullScanAsync(null, Ct);
        await _vm.GoToTodayAsync();
        await _vm.MoveWeekAsync(-3); // the band was left on another week
        await _vm.ToggleCalendarAsync();

        await _vm.OpenNoteAsync(today.RelativePath);

        Assert.False(_vm.IsCalendarExpanded);
        Assert.Equal(CalendarMath.WeekOf(Today).Monday, _vm.Week.Monday);
    }

    [Fact]
    public async Task A_day_picked_in_the_month_view_narrows_the_history_and_opens_nothing()
    {
        await _host.Engine.CreateAsync(LongAgo, "praca", "# Dávna poznámka", Ct);
        await _host.Indexer.FullScanAsync(null, Ct);
        var open = _vm.Editor.Path?.RelativePath;
        var persisted = _vm.Editor.IsPersisted;
        await _vm.ToggleCalendarAsync();

        var day = DateOnly.FromDateTime(LongAgo);
        await _vm.SelectDayAsync(day);

        Assert.True(_vm.IsCalendarExpanded);
        Assert.Equal(day, _vm.SelectedDay);
        Assert.Equal(day, _vm.History.DayFilter);
        Assert.Equal((open, persisted), (_vm.Editor.Path?.RelativePath, _vm.Editor.IsPersisted));

        // Back to the week by the button: the band shows the week of the day picked in the month.
        await _vm.ToggleCalendarAsync();
        Assert.Equal(CalendarMath.WeekOf(day).Monday, _vm.Week.Monday);
    }

    [Fact]
    public async Task In_the_week_view_a_picked_day_still_opens_its_latest_note()
    {
        var old = await _host.Engine.CreateAsync(LongAgo, "praca", "# Dávna poznámka", Ct);
        await _host.Indexer.FullScanAsync(null, Ct);

        await _vm.SelectDayAsync(DateOnly.FromDateTime(LongAgo));

        Assert.Equal(old.RelativePath, _vm.Editor.Path?.RelativePath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Today_clears_every_filter_and_keeps_week_or_month(bool month)
    {
        await _host.Engine.CreateAsync(LongAgo, "praca", "# Dávna poznámka\n- [ ] úloha", Ct);
        await _host.Indexer.FullScanAsync(null, Ct);
        await _vm.SetActiveTagAsync("praca");
        await _vm.SelectDayAsync(DateOnly.FromDateTime(LongAgo));
        await _vm.History.ToggleOpenTasksAsync();
        if (month)
        {
            await _vm.ToggleCalendarAsync();
        }
        else
        {
            await _vm.MoveWeekAsync(5);
        }

        await _vm.GoToTodayAsync();

        Assert.Equal(month, _vm.IsCalendarExpanded);
        Assert.Null(_vm.ActiveTag);
        Assert.DoesNotContain(_vm.RailTags, t => t.IsActive);
        Assert.False(_vm.History.HasFilter);
        Assert.Null(_vm.History.DayFilter);
        Assert.False(_vm.History.OpenTasksOnly);
        Assert.Equal(Today, _vm.SelectedDay);
        Assert.Equal(CalendarMath.WeekOf(Today).Monday, _vm.Week.Monday);
        Assert.Equal((Today.Year, Today.Month), (_vm.Calendar.Year, _vm.Calendar.Month));
    }

    [Fact]
    public async Task A_new_note_leaves_the_month_view()
    {
        await _vm.ToggleCalendarAsync();

        await _vm.NewNoteAsync();

        Assert.False(_vm.IsCalendarExpanded);
        Assert.Equal(Today, _vm.SelectedDay);
    }
}
