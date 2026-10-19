using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WolfMark.Core.Calendar;
using WolfMark.Core.Tags;
using WolfMark.Index.Queries;

namespace WolfMark.App.ViewModels;

/// <summary>
/// Expanded month grid of the calendar panel (M6-02, M6-04, M6-05): ISO weeks, tag fills, labels between
/// weeks, dimming by the active tag. The panel is part of the main window – no overlay (ADR 0004).
/// </summary>
internal sealed partial class CalendarViewModel : ObservableObject
{
    private readonly AppHost _host;
    private readonly Dictionary<DateOnly, DayCellViewModel> _cells = [];
    private int _generation;

    public CalendarViewModel(AppHost host)
    {
        _host = host;
        var today = DateOnly.FromDateTime(DateTime.Now);
        Year = today.Year;
        Month = today.Month;
    }

    /// <summary>Raised after <see cref="Weeks"/> was rebuilt.</summary>
    public event EventHandler? Rebuilt;

    public List<WeekRowViewModel> Weeks { get; } = [];

    [ObservableProperty]
    public partial int Year { get; private set; }

    [ObservableProperty]
    public partial int Month { get; private set; }

    /// <summary>"OKTÓBER 2026".</summary>
    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? FilterTag { get; private set; }

    public int TodayWeekday { get; private set; } = -1;

    public Task MoveAsync(int months)
    {
        var d = new DateOnly(Year, Month, 1).AddMonths(months);
        Year = d.Year;
        Month = d.Month;
        return LoadAsync();
    }

    public Task GoToAsync(DateOnly day)
    {
        Year = day.Year;
        Month = day.Month;
        return LoadAsync();
    }

    public void SetFilter(string? tag)
    {
        FilterTag = tag;
        foreach (var cell in _cells.Values)
        {
            cell.UpdateDimmed(tag);
        }
    }

    public async Task LoadAsync()
    {
        var generation = ++_generation;
        var culture = CultureInfo.CurrentCulture;
        Title = (culture.DateTimeFormat.GetMonthName(Month) + " " + Year).ToUpper(culture);
        var today = DateOnly.FromDateTime(DateTime.Now);
        TodayWeekday = today.Year == Year && today.Month == Month ? ((int)today.DayOfWeek + 6) % 7 : -1;

        var (from, to) = CalendarMath.MonthGridRange(Year, Month);
        await _host.DatabaseReady;
        var days = await _host.Calendar.GetDaysAsync(from, to, _host.ShutdownToken);
        var labels = await _host.Calendar.GetLabelsAsync(from, to, _host.Tags.LabelTags, _host.ShutdownToken);
        if (generation != _generation)
        {
            return;
        }

        _cells.Clear();
        Weeks.Clear();
        foreach (var week in CalendarMath.WeeksOfMonth(Year, Month))
        {
            var row = new WeekRowViewModel(week, today);
            foreach (var date in week.Days)
            {
                var cell = new DayCellViewModel(date, date.Month == Month, date == today, date < today, days.GetValueOrDefault(date) ?? CalendarDay.Empty(date));
                cell.UpdateDimmed(FilterTag);
                row.Days.Add(cell);
                if (cell.InMonth)
                {
                    _cells[date] = cell;
                }
            }

            row.Labels = [.. labels.Where(l => l.Day >= week.Monday && l.Day <= week.Sunday && l.Day.Month == Month)];
            Weeks.Add(row);
        }

        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Updates cells for changed days in place.</summary>
    public async Task RefreshDaysAsync(IReadOnlySet<DateOnly> changed)
    {
        var visible = changed.Where(_cells.ContainsKey).ToList();
        if (visible.Count == 0)
        {
            return;
        }

        var days = await _host.Calendar.GetDaysAsync(visible.Min(), visible.Max(), _host.ShutdownToken);
        foreach (var date in visible)
        {
            if (_cells.TryGetValue(date, out var cell))
            {
                cell.Day = days.GetValueOrDefault(date) ?? CalendarDay.Empty(date);
                cell.UpdateDimmed(FilterTag);
            }
        }
    }
}

internal sealed partial class WeekRowViewModel(CalendarWeek week, DateOnly today) : ObservableObject
{
    public CalendarWeek Week { get; } = week;

    public bool IsCurrentWeek { get; } = today >= week.Monday && today <= week.Sunday;

    public List<DayCellViewModel> Days { get; } = [];

    public List<CalendarLabel> Labels { get; set; } = [];
}

internal sealed partial class DayCellViewModel(DateOnly date, bool inMonth, bool isToday, bool isPast, CalendarDay day) : ObservableObject
{
    public DateOnly Date { get; } = date;

    public bool InMonth { get; } = inMonth;

    public bool IsToday { get; } = isToday;

    public bool IsPast { get; } = isPast;

    public bool IsWeekend { get; } = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    [ObservableProperty]
    public partial CalendarDay Day { get; set; } = day;

    [ObservableProperty]
    public partial bool IsDimmed { get; private set; }

    public string AutomationName => DayFormatting.Describe(Date, Day);

    public void UpdateDimmed(string? filterTag) =>
        IsDimmed = filterTag is not null && !Day.Tags.Any(t => TagName.Comparer.Equals(t.Tag, filterTag));

    partial void OnDayChanged(CalendarDay value) => OnPropertyChanged(nameof(AutomationName));
}
