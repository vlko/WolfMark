using System.Globalization;
using WolfMark.Core.Tags;

namespace WolfMark.Index.Queries;

/// <summary>
/// Parsed command-palette query (M5-03): free text plus filters <c>#tag</c> and
/// <c>@2026</c>, <c>@2026-10</c>, <c>@2026-10-15</c>.
/// </summary>
public sealed record SearchQuery(string Text, IReadOnlyList<string> Terms, IReadOnlyList<string> Tags, DateOnly? From, DateOnly? To)
{
    public bool HasText => Terms.Count > 0;

    public bool IsEmpty => Terms.Count == 0 && Tags.Count == 0 && From is null;

    public static SearchQuery Parse(string? input)
    {
        var terms = new List<string>();
        var tags = new List<string>();
        DateOnly? from = null, to = null;

        foreach (var raw in (input ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length > 1 && raw[0] == '#' && TagName.TryNormalize(raw, out var tag))
            {
                tags.Add(tag);
            }
            else if (raw.Length > 1 && raw[0] == '@' && TryParseDateFilter(raw[1..], out var f, out var t))
            {
                from = f;
                to = t;
            }
            else
            {
                terms.Add(raw);
            }
        }

        return new SearchQuery(string.Join(' ', terms), terms, tags, from, to);
    }

    public static bool TryParseDateFilter(string value, out DateOnly from, out DateOnly to)
    {
        from = to = default;
        var c = CultureInfo.InvariantCulture;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", c, DateTimeStyles.None, out var day))
        {
            from = to = day;
            return true;
        }

        if (DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", c, DateTimeStyles.None, out var month))
        {
            from = month;
            to = month.AddMonths(1).AddDays(-1);
            return true;
        }

        if (value.Length == 4 && int.TryParse(value, NumberStyles.None, c, out var year) && year is > 1900 and < 9999)
        {
            from = new DateOnly(year, 1, 1);
            to = new DateOnly(year, 12, 31);
            return true;
        }

        return false;
    }

    /// <summary>FTS5 MATCH expression: every term quoted (no operator injection) and prefix-matched.</summary>
    public string ToFtsExpression() =>
        string.Join(' ', Terms.Select(t => "\"" + t.Replace("\"", "\"\"") + "\"*"));
}
