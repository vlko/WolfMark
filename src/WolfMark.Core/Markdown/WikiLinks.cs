using System.Globalization;
using WolfMark.Core.Notes;

namespace WolfMark.Core.Markdown;

public enum WikiTargetKind
{
    /// <summary>A note by its place: <c>[[2026/11/16/praca_1403]]</c> (what "Copy link" writes).</summary>
    Note,

    /// <summary>A day: <c>[[2026-11-16]]</c>.</summary>
    Day,

    /// <summary>A note by its file name alone: <c>[[praca_1403]]</c> – the newest note of that name.</summary>
    Name,
}

/// <summary>Where a wiki link points.</summary>
/// <param name="Value">The note's path with ".md", the day as yyyy-MM-dd, or the file name without ".md".</param>
public readonly record struct WikiTarget(WikiTargetKind Kind, string Value, DateOnly Day = default);

/// <summary>
/// Wiki links <c>[[…]]</c> between notes (M9-04): what the editor follows and what the index stores so a note can
/// show which notes link to it. <c>[[target|label]]</c> shows a label of its own; only the target counts.
/// </summary>
public static class WikiLinks
{
    /// <summary>Reads the text between the brackets; null when it names nothing.</summary>
    public static WikiTarget? Parse(string inner)
    {
        var bar = inner.IndexOf('|', StringComparison.Ordinal);
        var target = (bar >= 0 ? inner[..bar] : inner).Trim().Replace('\\', '/').Trim('/');
        if (target.EndsWith(NoteName.Extension, StringComparison.OrdinalIgnoreCase))
        {
            target = target[..^NoteName.Extension.Length];
        }

        if (target.Length == 0)
        {
            return null;
        }

        if (DateOnly.TryParseExact(target, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return new WikiTarget(WikiTargetKind.Day, target, day);
        }

        if (target.Contains('/', StringComparison.Ordinal))
        {
            // A place in the notes – never a way out of them (no "..", hidden or internal folders: NotePath says no).
            return NotePath.TryParse(target + NoteName.Extension, out var path)
                ? new WikiTarget(WikiTargetKind.Note, path.RelativePath)
                : null;
        }

        return NotePath.IsCandidateFileName(target + NoteName.Extension) ? new WikiTarget(WikiTargetKind.Name, target) : null;
    }

    /// <summary>The text of the wiki link at <paramref name="column"/> – on its text or on the brackets around it.</summary>
    public static string? TargetAt(string line, IReadOnlyList<MarkdownToken> tokens, int column)
    {
        foreach (var token in tokens)
        {
            // "[[" and "]]" are two characters on each side of the text.
            if (token.Kind == MarkdownTokenKind.WikiLink && column >= token.Start - 2 && column <= token.End + 2)
            {
                return line.Substring(token.Start, token.Length);
            }
        }

        return null;
    }

    /// <summary>
    /// How a link is stored in the index and looked up: lower case, a note by its path without ".md", a name as it
    /// is, a day with "day:" in front.
    /// </summary>
    public static string Key(WikiTarget target) => target.Kind switch
    {
        WikiTargetKind.Day => "day:" + target.Value,
        WikiTargetKind.Note => target.Value[..^NoteName.Extension.Length].ToLowerInvariant(),
        _ => target.Value.ToLowerInvariant(),
    };

    /// <summary>The keys under which other notes may link to this one: its path and its bare file name.</summary>
    public static IReadOnlyList<string> KeysOf(NotePath note)
    {
        var path = note.RelativePath[..^NoteName.Extension.Length].ToLowerInvariant();
        var name = path[(path.LastIndexOf('/') + 1)..];
        return [path, name];
    }
}
