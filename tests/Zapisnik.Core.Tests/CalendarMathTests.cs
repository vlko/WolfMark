using Zapisnik.Core.Calendar;

namespace Zapisnik.Core.Tests;

public sealed class CalendarMathTests
{
    [Fact]
    public void Week_starts_on_monday()
    {
        Assert.Equal(new DateOnly(2026, 10, 12), CalendarMath.StartOfWeek(new DateOnly(2026, 10, 15)));
        Assert.Equal(new DateOnly(2026, 10, 12), CalendarMath.StartOfWeek(new DateOnly(2026, 10, 18))); // Sunday
        Assert.Equal(new DateOnly(2026, 10, 12), CalendarMath.StartOfWeek(new DateOnly(2026, 10, 12)));
    }

    [Fact]
    public void Week_53_at_year_boundary()
    {
        // 2026 has 53 ISO weeks (1 Jan 2026 is a Thursday).
        Assert.Equal(53, CalendarMath.IsoWeek(new DateOnly(2026, 12, 31)));
        Assert.Equal(53, CalendarMath.IsoWeek(new DateOnly(2027, 1, 3)));
        Assert.Equal(2026, CalendarMath.IsoWeekYear(new DateOnly(2027, 1, 3)));
        Assert.Equal(1, CalendarMath.IsoWeek(new DateOnly(2027, 1, 4)));
    }

    [Fact]
    public void First_january_on_sunday_belongs_to_previous_year()
    {
        // 1 Jan 2023 was a Sunday → ISO week 52 of 2022.
        Assert.Equal(52, CalendarMath.IsoWeek(new DateOnly(2023, 1, 1)));
        Assert.Equal(2022, CalendarMath.IsoWeekYear(new DateOnly(2023, 1, 1)));
        var weeks = CalendarMath.WeeksOfMonth(2023, 1);
        Assert.Equal(new DateOnly(2022, 12, 26), weeks[0].Monday);
        Assert.Equal(52, weeks[0].WeekNumber);
        Assert.Equal(6, weeks.Count);
    }

    [Fact]
    public void October_2026_grid()
    {
        var weeks = CalendarMath.WeeksOfMonth(2026, 10);
        Assert.Equal(5, weeks.Count);
        Assert.Equal(40, weeks[0].WeekNumber);
        Assert.Equal(new DateOnly(2026, 9, 28), weeks[0].Monday);
        Assert.Equal(44, weeks[^1].WeekNumber);
    }

    [Fact]
    public void Weekday_names_start_monday()
    {
        var names = CalendarMath.WeekdayNames(System.Globalization.CultureInfo.GetCultureInfo("en-US"), abbreviated: true);
        Assert.Equal("Mon", names[0]);
        Assert.Equal("Sun", names[6]);
    }
}
