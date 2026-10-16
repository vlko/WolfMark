using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WolfMark.App.Infrastructure;
using WolfMark.Core.Calendar;

namespace WolfMark.App.ViewModels;

/// <summary>Compact week bar under the title bar (M6-01).</summary>
internal sealed partial class WeekStripViewModel : ObservableObject
{
    private readonly AppHost _host;
    private int _generation;

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

    [ObservableProperty]
    public partial string WeekLabel { get; private set; } = string.Empty;

    public async Task LoadAsync(DateOnly anyDay, DateOnly selected)
    {
        var generation = ++_generation;
        var monday = CalendarMath.StartOfWeek(anyDay);
        Monday = monday;
        var culture = CultureInfo.CurrentCulture;
        WeekLabel = Strings.Format(R.Week_Label, CalendarMath.IsoWeek(monday)) + " · "
                    + culture.TextInfo.ToTitleCase(culture.DateTimeFormat.GetMonthName(monday.AddDays(3).Month)) + " " + monday.AddDays(3).Year;

        var today = DateOnly.FromDateTime(DateTime.Now);
        var names = CalendarMath.WeekdayNames(culture, abbreviated: true);
        for (var i = 0; i < 7; i++)
        {
            var date = monday.AddDays(i);
            Days[i].Set(date, names[i], date == today, date == selected, CalendarDay.Empty(date));
        }

        await _host.DatabaseReady;
        var days = await _host.Calendar.GetWeekAsync(monday, _host.ShutdownToken);
        if (generation != _generation)
        {
            return;
        }

        for (var i = 0; i < 7; i++)
        {
            var date = monday.AddDays(i);
            Days[i].Set(date, names[i], date == today, date == selected, days.GetValueOrDefault(date) ?? CalendarDay.Empty(date));
        }
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
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial CalendarDay Day { get; private set; } = CalendarDay.Empty(default);

    public string AutomationName => DayFormatting.Describe(Date, Day);

    public void Set(DateOnly date, string dayName, bool isToday, bool isSelected, CalendarDay day)
    {
        Date = date;
        DayName = dayName;
        IsToday = isToday;
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
