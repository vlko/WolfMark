using System.Text.Json;
using WolfMark.Core.Calendar;
using WolfMark.Core.Settings;
using WolfMark.Core.Tags;
using WolfMark.Index.Database;

namespace WolfMark.Index.Queries;

/// <summary>A note title shown under a calendar week (SPEC §5.2).</summary>
public sealed record CalendarLabel(DateOnly Day, string Tag, string Title, string RelativePath);

/// <summary>What the week "loupe" shows: dominant tag of the week and its first title.</summary>
public sealed record WeekHighlight(string Tag, int Count, string? Title);

public sealed record TagUsage(string Tag, int Count);

/// <summary>Calendar reads come only from the index – never from files (SPEC §4.3, M3-02).</summary>
public sealed class CalendarService(IndexDatabase db)
{
    public Task<IReadOnlyDictionary<DateOnly, CalendarDay>> GetDaysAsync(DateOnly from, DateOnly to, CancellationToken ct) =>
        db.ReadAsync<IReadOnlyDictionary<DateOnly, CalendarDay>>(
            c =>
            {
                using var cmd = c.Command(
                    "SELECT day, note_count, tag_summary, open_tasks, done_tasks FROM calendar_days WHERE day BETWEEN $f AND $t;")
                    .With("$f", SqliteExtensions.FormatDay(from))
                    .With("$t", SqliteExtensions.FormatDay(to));
                using var r = cmd.ExecuteReader();
                var result = new Dictionary<DateOnly, CalendarDay>();
                while (r.Read())
                {
                    var day = SqliteExtensions.ParseDay(r.GetString(0));
                    var tags = JsonSerializer.Deserialize(r.GetString(2), WolfMarkJsonContext.Default.ListTagCount) ?? [];
                    result[day] = new CalendarDay(day, r.GetInt32(1), tags, r.GetInt32(3), r.GetInt32(4));
                }

                return result;
            },
            ct);

    public Task<IReadOnlyDictionary<DateOnly, CalendarDay>> GetWeekAsync(DateOnly anyDay, CancellationToken ct)
    {
        var monday = CalendarMath.StartOfWeek(anyDay);
        return GetDaysAsync(monday, monday.AddDays(6), ct);
    }

    public Task<IReadOnlyDictionary<DateOnly, CalendarDay>> GetMonthAsync(int year, int month, CancellationToken ct)
    {
        var (from, to) = CalendarMath.MonthGridRange(year, month);
        return GetDaysAsync(from, to, ct);
    }

    public Task<IReadOnlyDictionary<DateOnly, CalendarDay>> GetYearAsync(int year, CancellationToken ct)
    {
        var (from, _) = CalendarMath.MonthGridRange(year, 1);
        var (_, to) = CalendarMath.MonthGridRange(year, 12);
        return GetDaysAsync(from, to, ct);
    }

    /// <summary>Titles of notes whose primary tag is one of <paramref name="tags"/>.</summary>
    public Task<IReadOnlyList<CalendarLabel>> GetLabelsAsync(DateOnly from, DateOnly to, IReadOnlyCollection<string> tags, CancellationToken ct)
    {
        if (tags.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<CalendarLabel>>([]);
        }

        return db.ReadAsync<IReadOnlyList<CalendarLabel>>(
            c =>
            {
                var keys = tags.Select(TagName.ToKey).ToList();
                var placeholders = string.Join(", ", keys.Select((_, i) => "$k" + i));
                using var cmd = c.Command(
                    $"""
                    SELECT n.day, n.primary_tag, n.title, n.rel_path FROM notes n
                    WHERE n.day BETWEEN $f AND $t AND n.primary_tag_key IN ({placeholders})
                    ORDER BY n.day, n.created_at;
                    """)
                    .With("$f", SqliteExtensions.FormatDay(from))
                    .With("$t", SqliteExtensions.FormatDay(to));
                for (var i = 0; i < keys.Count; i++)
                {
                    cmd.With("$k" + i, keys[i]);
                }

                using var r = cmd.ExecuteReader();
                var list = new List<CalendarLabel>();
                while (r.Read())
                {
                    list.Add(new CalendarLabel(SqliteExtensions.ParseDay(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3)));
                }

                return list;
            },
            ct);
    }

    public Task<WeekHighlight?> GetWeekHighlightAsync(DateOnly anyDay, CancellationToken ct)
    {
        var monday = CalendarMath.StartOfWeek(anyDay);
        return db.ReadAsync(
            c =>
            {
                using var cmd = c.Command(
                    """
                    SELECT MIN(n.primary_tag), COUNT(*),
                           (SELECT n2.title FROM notes n2 WHERE n2.primary_tag_key = n.primary_tag_key
                              AND n2.day BETWEEN $f AND $t ORDER BY n2.created_at LIMIT 1)
                    FROM notes n
                    WHERE n.day BETWEEN $f AND $t AND n.primary_tag_key IS NOT NULL
                    GROUP BY n.primary_tag_key ORDER BY COUNT(*) DESC LIMIT 1;
                    """)
                    .With("$f", SqliteExtensions.FormatDay(monday))
                    .With("$t", SqliteExtensions.FormatDay(monday.AddDays(6)));
                using var r = cmd.ExecuteReader();
                return r.Read() ? new WeekHighlight(r.GetString(0), r.GetInt32(1), r.GetStringOrNull(2)) : null;
            },
            ct);
    }

    /// <summary>Primary-tag usage for the legend, most used first.</summary>
    public Task<IReadOnlyList<TagUsage>> GetTagUsageAsync(DateOnly? from, DateOnly? to, CancellationToken ct) =>
        db.ReadAsync<IReadOnlyList<TagUsage>>(
            c =>
            {
                using var cmd = c.Command(
                    """
                    SELECT MIN(primary_tag), COUNT(*) FROM notes
                    WHERE primary_tag_key IS NOT NULL AND ($f IS NULL OR day >= $f) AND ($t IS NULL OR day <= $t)
                    GROUP BY primary_tag_key ORDER BY COUNT(*) DESC, MIN(primary_tag);
                    """)
                    .With("$f", from is { } f ? SqliteExtensions.FormatDay(f) : null)
                    .With("$t", to is { } t ? SqliteExtensions.FormatDay(t) : null);
                using var r = cmd.ExecuteReader();
                var list = new List<TagUsage>();
                while (r.Read())
                {
                    list.Add(new TagUsage(r.GetString(0), r.GetInt32(1)));
                }

                return list;
            },
            ct);
}
