using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Zapisnik.Core.Calendar;
using Zapisnik.Core.Tags;
using Zapisnik.Index.Queries;

namespace Zapisnik.App.ViewModels;

public enum CalendarMode
{
    Year,
    Month,
}

/// <summary>Full calendar (M6-02..M6-05): year or month grid, legend filter, week loupe data.</summary>
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

    public ObservableCollection<MonthViewModel> Months { get; } = [];

    public ObservableCollection<LegendItemViewModel> Legend { get; } = [];

    [ObservableProperty]
    public partial int Year { get; private set; }

    [ObservableProperty]
    public partial int Month { get; private set; }

    [ObservableProperty]
    public partial CalendarMode Mode { get; private set; } = CalendarMode.Year;

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? FilterTag { get; private set; }

    public async Task SetModeAsync(CalendarMode mode)
    {
        Mode = mode;
        await LoadAsync();
    }

    public Task MoveAsync(int delta)
    {
        if (Mode == CalendarMode.Year)
        {
            Year += delta;
        }
        else
        {
            var d = new DateOnly(Year, Month, 1).AddMonths(delta);
            Year = d.Year;
            Month = d.Month;
        }

        return LoadAsync();
    }

    public Task GoToAsync(DateOnly day)
    {
        Year = day.Year;
        Month = day.Month;
        return LoadAsync();
    }

    public void ToggleFilter(string tag)
    {
        FilterTag = FilterTag is not null && TagName.Comparer.Equals(FilterTag, tag) ? null : tag;
        foreach (var item in Legend)
        {
            item.IsActive = FilterTag is not null && TagName.Comparer.Equals(item.Tag, FilterTag);
        }

        foreach (var cell in _cells.Values)
        {
            cell.UpdateDimmed(FilterTag);
        }
    }

    public async Task LoadAsync()
    {
        var generation = ++_generation;
        var culture = CultureInfo.CurrentCulture;
        Title = Mode == CalendarMode.Year
            ? Year.ToString(culture)
            : culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetMonthName(Month)) + " " + Year;

        var months = Mode == CalendarMode.Year ? Enumerable.Range(1, 12).Select(m => (Year, m)).ToList() : [(Year, Month)];
        var (from, _) = CalendarMath.MonthGridRange(months[0].Item1, months[0].Item2);
        var (_, to) = CalendarMath.MonthGridRange(months[^1].Item1, months[^1].Item2);

        await _host.DatabaseReady;
        var days = await _host.Calendar.GetDaysAsync(from, to, _host.ShutdownToken);
        var labels = await _host.Calendar.GetLabelsAsync(from, to, _host.Tags.LabelTags, _host.ShutdownToken);
        var usage = await _host.Calendar.GetTagUsageAsync(from, to, _host.ShutdownToken);
        IReadOnlyList<CalendarLabel> titles = Mode == CalendarMode.Month
            ? await LoadMonthTitlesAsync(from, to)
            : [];
        if (generation != _generation)
        {
            return;
        }

        _cells.Clear();
        Months.Clear();
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var (y, m) in months)
        {
            var month = new MonthViewModel(y, m, today);
            foreach (var week in CalendarMath.WeeksOfMonth(y, m))
            {
                var row = new WeekRowViewModel(week, today);
                foreach (var date in week.Days)
                {
                    var cell = new DayCellViewModel(date, date.Month == m, date == today, days.GetValueOrDefault(date) ?? CalendarDay.Empty(date));
                    cell.Titles = [.. titles.Where(t => t.Day == date).Take(4)];
                    cell.UpdateDimmed(FilterTag);
                    row.Days.Add(cell);
                    if (cell.InMonth)
                    {
                        _cells[date] = cell;
                    }
                }

                row.Labels = [.. labels.Where(l => l.Day >= week.Monday && l.Day <= week.Sunday && l.Day.Month == m)];
                month.Weeks.Add(row);
            }

            Months.Add(month);
        }

        Legend.Clear();
        foreach (var u in usage)
        {
            Legend.Add(new LegendItemViewModel(u.Tag, u.Count)
            {
                IsActive = FilterTag is not null && TagName.Comparer.Equals(FilterTag, u.Tag),
            });
        }
    }

    private async Task<IReadOnlyList<CalendarLabel>> LoadMonthTitlesAsync(DateOnly from, DateOnly to)
    {
        var list = new List<CalendarLabel>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.Month != Month)
            {
                continue;
            }

            foreach (var n in await _host.History.GetDayAsync(d, _host.ShutdownToken))
            {
                list.Add(new CalendarLabel(n.Day, n.PrimaryTag ?? string.Empty, n.Title, n.RelativePath));
            }
        }

        return list;
    }

    /// <summary>Updates cells for changed days in place (no full reload).</summary>
    public async Task RefreshDaysAsync(IReadOnlySet<DateOnly> changed)
    {
        var visible = changed.Where(_cells.ContainsKey).ToList();
        if (visible.Count == 0)
        {
            return;
        }

        var from = visible.Min();
        var to = visible.Max();
        var days = await _host.Calendar.GetDaysAsync(from, to, _host.ShutdownToken);
        foreach (var date in visible)
        {
            if (_cells.TryGetValue(date, out var cell))
            {
                cell.Day = days.GetValueOrDefault(date) ?? CalendarDay.Empty(date);
                cell.UpdateDimmed(FilterTag);
            }
        }
    }

    public Task<WeekHighlight?> GetHighlightAsync(DateOnly monday) => _host.Calendar.GetWeekHighlightAsync(monday, _host.ShutdownToken);
}

internal sealed partial class MonthViewModel(int year, int month, DateOnly today) : ObservableObject
{
    public int Year { get; } = year;

    public int Month { get; } = month;

    public string Title { get; } =
        CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month).ToUpper(CultureInfo.CurrentCulture) + " " + year;

    public List<WeekRowViewModel> Weeks { get; } = [];

    /// <summary>Index (0 = Monday) of today's weekday when today is in this month, otherwise -1.</summary>
    public int TodayWeekday { get; } = today.Year == year && today.Month == month ? ((int)today.DayOfWeek + 6) % 7 : -1;
}

internal sealed partial class WeekRowViewModel(CalendarWeek week, DateOnly today) : ObservableObject
{
    public CalendarWeek Week { get; } = week;

    public bool IsCurrentWeek { get; } = today >= week.Monday && today <= week.Sunday;

    public List<DayCellViewModel> Days { get; } = [];

    public List<CalendarLabel> Labels { get; set; } = [];
}

internal sealed partial class DayCellViewModel(DateOnly date, bool inMonth, bool isToday, CalendarDay day) : ObservableObject
{
    public DateOnly Date { get; } = date;

    public bool InMonth { get; } = inMonth;

    public bool IsToday { get; } = isToday;

    public bool IsWeekend { get; } = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    [ObservableProperty]
    public partial CalendarDay Day { get; set; } = day;

    [ObservableProperty]
    public partial bool IsDimmed { get; private set; }

    public List<CalendarLabel> Titles { get; set; } = [];

    public string AutomationName => DayFormatting.Describe(Date, Day);

    public void UpdateDimmed(string? filterTag) =>
        IsDimmed = filterTag is not null && !Day.Tags.Any(t => TagName.Comparer.Equals(t.Tag, filterTag));

    partial void OnDayChanged(CalendarDay value) => OnPropertyChanged(nameof(AutomationName));
}

internal sealed partial class LegendItemViewModel(string tag, int count) : ObservableObject
{
    public string Tag { get; } = tag;

    public int Count { get; } = count;

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}
