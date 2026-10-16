namespace WolfMark.Core.Calendar;

/// <summary>Number of notes with a given primary tag on a day. Empty tag = untagged.</summary>
public sealed record TagCount(string Tag, int Count);

/// <summary>Aggregated day for the calendar (SPEC §4.3).</summary>
public sealed record CalendarDay(DateOnly Date, int NoteCount, IReadOnlyList<TagCount> Tags, int OpenTasks, int DoneTasks)
{
    public static CalendarDay Empty(DateOnly date) => new(date, 0, [], 0, 0);

    public bool HasNotes => NoteCount > 0;

    /// <summary>Tag with the most notes, ignoring untagged notes; null if none.</summary>
    public string? DominantTag => Tags.Where(t => t.Tag.Length > 0).MaxBy(t => t.Count)?.Tag;

    public double TaskProgress => OpenTasks + DoneTasks == 0 ? 0 : DoneTasks / (double)(OpenTasks + DoneTasks);
}

/// <summary>One ISO week row in a month grid.</summary>
public sealed record CalendarWeek(int IsoYear, int WeekNumber, DateOnly Monday)
{
    public IEnumerable<DateOnly> Days => Enumerable.Range(0, 7).Select(Monday.AddDays);

    public DateOnly Sunday => Monday.AddDays(6);
}
