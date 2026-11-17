using System.Text;

namespace WolfMark.Core.Markdown;

/// <summary>A found occurrence: <see cref="Length"/> characters at <see cref="Start"/>.</summary>
public readonly record struct TextMatch(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// Find and replace within one note (Ctrl+F / Ctrl+H in both apps): plain text, the letter case ignored unless
/// asked for. Notes are small – every operation simply searches the whole text again.
/// </summary>
public static class TextSearch
{
    /// <summary>All occurrences of <paramref name="query"/>, in order, not overlapping.</summary>
    public static List<TextMatch> FindAll(string text, string query, bool matchCase)
    {
        var matches = new List<TextMatch>();
        if (query.Length == 0)
        {
            return matches;
        }

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var at = text.IndexOf(query, comparison); at >= 0; at = text.IndexOf(query, at + query.Length, comparison))
        {
            matches.Add(new TextMatch(at, query.Length));
        }

        return matches;
    }

    /// <summary>
    /// The match to go to from <paramref name="from"/>: forwards the first one starting there or later, backwards
    /// the last one starting before it; past the end (or the start) it wraps around.
    /// </summary>
    /// <returns>Its index in <paramref name="matches"/>, or -1 when there is none.</returns>
    public static int IndexFrom(IReadOnlyList<TextMatch> matches, int from, bool backwards)
    {
        if (matches.Count == 0)
        {
            return -1;
        }

        if (backwards)
        {
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                if (matches[i].Start < from)
                {
                    return i;
                }
            }

            return matches.Count - 1;
        }

        for (var i = 0; i < matches.Count; i++)
        {
            if (matches[i].Start >= from)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Replaces one match; the caret lands after the new text.</summary>
    public static TextEdit Replace(TextMatch match, string replacement) =>
        new(match.Start, match.Length, replacement, match.Start + replacement.Length, 0);

    /// <summary>
    /// Replaces every match as one edit – one step to undo. The edit covers the text from the first match to the
    /// last; null when there is nothing to replace.
    /// </summary>
    public static TextEdit? ReplaceAll(string text, IReadOnlyList<TextMatch> matches, string replacement)
    {
        if (matches.Count == 0)
        {
            return null;
        }

        var first = matches[0].Start;
        var last = matches[^1].End;
        var sb = new StringBuilder(last - first + (matches.Count * Math.Max(0, replacement.Length - matches[0].Length)));
        var at = first;
        foreach (var match in matches)
        {
            sb.Append(text, at, match.Start - at).Append(replacement);
            at = match.End;
        }

        return new TextEdit(first, last - first, sb.ToString(), first + sb.Length, 0);
    }
}
