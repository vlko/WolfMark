using WolfMark.Core.Calendar;

namespace WolfMark.Core.Tests;

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

    /// <summary>M9-01: the task count in the corner of a day cell is drawn over the band that is there.</summary>
    [Fact]
    public void The_band_at_a_share_of_the_cell_follows_the_note_counts()
    {
        var date = new DateOnly(2026, 11, 17);
        var day = new CalendarDay(date, 10, [new TagCount("praca", 6), new TagCount(string.Empty, 3), new TagCount("rodina", 1)], 2, 0);

        Assert.Equal("praca", day.TagAtShare(0.5));
        Assert.Equal(string.Empty, day.TagAtShare(0.85));
        Assert.Equal("rodina", day.TagAtShare(0.95));
        Assert.Equal("rodina", day.TagAtShare(1));
        Assert.Null(CalendarDay.Empty(date).TagAtShare(0.9));

        // A fifth tag has no band of its own: the cell ends in the fourth one's colour.
        var many = new CalendarDay(date, 5, [.. "abcde".Select(c => new TagCount(c.ToString(), 1))], 0, 0);
        Assert.Equal("d", many.TagAtShare(0.9));
    }
}
