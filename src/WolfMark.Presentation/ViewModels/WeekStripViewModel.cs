using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WolfMark.App.Infrastructure;
using WolfMark.Core.Calendar;
using WolfMark.Core.Tags;
using WolfMark.Index.Queries;

namespace WolfMark.App.ViewModels;

/// <summary>Week band of the calendar (M6-01): seven days plus one label line under it.</summary>
internal sealed partial class WeekStripViewModel : ObservableObject
{
    private readonly AppHost _host;
    private int _generation;
    private bool _loaded;

    public WeekStripViewModel(AppHost host)
    {
        _host = host;
        for (var i = 0; i < 7; i++)
        {
            Days.Add(new WeekDayViewModel());
        }
    }

    public ObservableCollection<WeekDayViewModel> Days { get; } = [];

    [ObservableProperty]
    public partial DateOnly Monday { get; private set; }

    /// <summary>"T42 · OKTÓBER 2026".</summary>
    [ObservableProperty]
    public partial string WeekLabel { get; private set; } = string.Empty;

    /// <summary>Line under the band ("■ PRÁCA 3  ■ WOLFMARK 5"): note counts per tag, Claude Code per project.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<WeekTopic> Topics { get; private set; } = [];

    public async Task LoadAsync(DateOnly anyDay, DateOnly selected)
    {
        var generation = ++_generation;
        var monday = CalendarMath.StartOfWeek(anyDay);
        var sameWeek = _loaded && monday == Monday;
        Monday = monday;
        var culture = CultureInfo.CurrentCulture;
        var mid = monday.AddDays(3);
        WeekLabel = Strings.Format(R.Week_Label, CalendarMath.IsoWeek(monday)) + " · " + culture.DateTimeFormat.GetMonthName(mid.Month) + " " + mid.Year;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var names = CalendarMath.WeekdayNames(culture, abbreviated: true);
        // A refresh of the shown week (after every save) keeps the old values until the new ones arrive –
        // blanking the days first redrew the strip twice.
        if (!sameWeek)
        {
            for (var i = 0; i < 7; i++)
            {
                var date = monday.AddDays(i);
                Days[i].Set(date, names[i], today, date == selected, CalendarDay.Empty(date));
            }
        }

        await _host.DatabaseReady;
        var days = await _host.Calendar.GetWeekAsync(monday, _host.ShutdownToken);
        var topics = await _host.Calendar.GetWeekTopicsAsync(monday, _host.ShutdownToken);
        if (generation != _generation)
        {
            return;
        }

        WolfMark.Core.Diagnostics.EditorTrace.Current?.Mark("week strip");
        for (var i = 0; i < 7; i++)
        {
            var date = monday.AddDays(i);
            Days[i].Set(date, names[i], today, date == selected, days.GetValueOrDefault(date) ?? CalendarDay.Empty(date));
        }

        Topics = topics;
        _loaded = true;
    }

    public void Select(DateOnly selected)
    {
        foreach (var d in Days)
        {
            d.IsSelected = d.Date == selected;
        }
    }
}

internal sealed partial class WeekDayViewModel : ObservableObject
{
    [ObservableProperty]
    public partial DateOnly Date { get; private set; }

    [ObservableProperty]
    public partial string DayName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsToday { get; private set; }

    [ObservableProperty]
    public partial bool IsPast { get; private set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial CalendarDay Day { get; private set; } = CalendarDay.Empty(default);

    public string AutomationName => DayFormatting.Describe(Date, Day);

    public void Set(DateOnly date, string dayName, DateOnly today, bool isSelected, CalendarDay day)
    {
        Date = date;
        DayName = dayName;
        IsToday = date == today;
        IsPast = date < today;
        IsSelected = isSelected;
        Day = day;
        OnPropertyChanged(nameof(AutomationName));
    }
}

internal static class DayFormatting
{
    /// <summary>Narrator text: "15. októbra 2026, 3 poznámky, praca, ClaudeCode" (M6-05).</summary>
    public static string Describe(DateOnly date, CalendarDay day)
    {
        var text = date.ToString("D", CultureInfo.CurrentCulture) + ", " + Strings.Notes(day.NoteCount);
        foreach (var t in day.Tags)
        {
            text += ", " + (t.Tag.Length == 0 ? Strings.Get(R.Untagged) : t.Tag);
        }

        return text;
    }
}
