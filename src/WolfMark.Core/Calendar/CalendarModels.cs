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

    /// <summary>Tags drawn as bands of a day cell – the rest shares the colour of the last one.</summary>
    public const int MaxBands = 4;

    /// <summary>
    /// The tag whose band covers <paramref name="share"/> (0–1) of the way across the cell, the bands being as wide
    /// as the tags' note counts (at most <see cref="MaxBands"/>). Empty for untagged notes, null for a day without notes.
    /// What is drawn over a band – the task count in the corner – takes its contrast from it.
    /// </summary>
    public string? TagAtShare(double share)
    {
        if (Tags.Count == 0)
        {
            return null;
        }

        var total = (double)Tags.Sum(t => t.Count);
        var end = 0.0;
        foreach (var tag in Tags.Take(MaxBands))
        {
            end += tag.Count / total;
            if (share < end)
            {
                return tag.Tag;
            }
        }

        return Tags[Math.Min(Tags.Count, MaxBands) - 1].Tag;
    }

    public double TaskProgress => OpenTasks + DoneTasks == 0 ? 0 : DoneTasks / (double)(OpenTasks + DoneTasks);
}

/// <summary>One ISO week row in a month grid.</summary>
public sealed record CalendarWeek(int IsoYear, int WeekNumber, DateOnly Monday)
{
    public IEnumerable<DateOnly> Days => Enumerable.Range(0, 7).Select(Monday.AddDays);

    public DateOnly Sunday => Monday.AddDays(6);
}
