namespace WolfMark.Core.Markdown;

/// <summary>What of a line the spell checker sees: the prose, with code, links, tags and markers blanked out.</summary>
public static class MarkdownSpelling
{
    /// <summary>
    /// The line with every non-prose character replaced by a space, so columns stay source columns and the checker
    /// never reports `code`, URLs, #tags or "**". Equal lines give equal strings – a cache key for the results.
    /// </summary>
    public static string ProseOnly(string line, IReadOnlyList<MarkdownToken> tokens)
    {
        var any = false;
        foreach (var t in tokens)
        {
            any |= !IsProse(t.Kind) && t.Length > 0;
        }

        if (!any)
        {
            return line;
        }

        return string.Create(line.Length, (line, tokens), static (span, state) =>
        {
            state.line.CopyTo(span);
            foreach (var t in state.tokens)
            {
                if (!IsProse(t.Kind))
                {
                    var start = Math.Clamp(t.Start, 0, span.Length);
                    span[start..Math.Clamp(t.End, start, span.Length)].Fill(' ');
                }
            }
        });
    }

    private static bool IsProse(MarkdownTokenKind kind) => kind is not (
        MarkdownTokenKind.Syntax or MarkdownTokenKind.InlineCode or MarkdownTokenKind.CodeBlock or MarkdownTokenKind.CodeFence
        or MarkdownTokenKind.FrontMatter or MarkdownTokenKind.LinkUrl or MarkdownTokenKind.AutoLink or MarkdownTokenKind.WikiLink
        or MarkdownTokenKind.Image or MarkdownTokenKind.Hashtag or MarkdownTokenKind.ListMarker or MarkdownTokenKind.TaskOpen
        or MarkdownTokenKind.TaskDone or MarkdownTokenKind.HorizontalRule);
}
