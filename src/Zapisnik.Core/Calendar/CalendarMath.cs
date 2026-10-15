using System.Globalization;

namespace Zapisnik.Core.Calendar;

/// <summary>ISO 8601 week math; weeks start on Monday (SPEC §4.3).</summary>
public static class CalendarMath
{
    public static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return date.AddDays(-offset);
    }

    public static int IsoWeek(DateOnly date) => ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));

    public static int IsoWeekYear(DateOnly date) => ISOWeek.GetYear(date.ToDateTime(TimeOnly.MinValue));

    public static CalendarWeek WeekOf(DateOnly date)
    {
        var monday = StartOfWeek(date);
        return new CalendarWeek(IsoWeekYear(monday), IsoWeek(monday), monday);
    }

    /// <summary>All ISO weeks that contain at least one day of the month.</summary>
    public static IReadOnlyList<CalendarWeek> WeeksOfMonth(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var weeks = new List<CalendarWeek>(6);
        for (var monday = StartOfWeek(first); monday <= last; monday = monday.AddDays(7))
        {
            weeks.Add(new CalendarWeek(IsoWeekYear(monday), IsoWeek(monday), monday));
        }

        return weeks;
    }

    public static (DateOnly From, DateOnly To) MonthRange(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    /// <summary>Range covering the full weeks of the month grid (may include adjacent months).</summary>
    public static (DateOnly From, DateOnly To) MonthGridRange(int year, int month)
    {
        var weeks = WeeksOfMonth(year, month);
        return (weeks[0].Monday, weeks[^1].Sunday);
    }

    /// <summary>Day-of-week names starting Monday for the given culture.</summary>
    public static IReadOnlyList<string> WeekdayNames(CultureInfo culture, bool abbreviated)
    {
        var names = abbreviated ? culture.DateTimeFormat.AbbreviatedDayNames : culture.DateTimeFormat.DayNames;
        return [.. Enumerable.Range(1, 7).Select(i => names[i % 7])];
    }
}
