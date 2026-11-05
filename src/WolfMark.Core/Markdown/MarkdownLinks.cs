namespace WolfMark.Core.Markdown;

/// <summary>Web links in a line: what the editor's context menu offers to open in the browser.</summary>
public static class MarkdownLinks
{
    /// <summary>
    /// The address of the link at <paramref name="column"/> – <c>[text](url "title")</c> anywhere on the text or the
    /// address, or a bare <c>https://…</c> – when it is a web or mail address. Other targets (files, UNC paths,
    /// other schemes) are never opened from a note.
    /// </summary>
    public static string? WebUrlAt(string line, IReadOnlyList<MarkdownToken> tokens, int column)
    {
        for (var k = 0; k < tokens.Count; k++)
        {
            var token = tokens[k];
            if (column < token.Start || column > token.End)
            {
                continue;
            }

            string? target = token.Kind switch
            {
                MarkdownTokenKind.AutoLink => line.Substring(token.Start, token.Length),
                MarkdownTokenKind.LinkUrl => Target(line.Substring(token.Start, token.Length)),
                MarkdownTokenKind.LinkText => UrlAfter(line, tokens, k),
                _ => null,
            };
            if (target is not null && IsWeb(target))
            {
                return target;
            }
        }

        return null;
    }

    /// <summary>The address of a link whose text token is at <paramref name="index"/>: the next address token.</summary>
    private static string? UrlAfter(string line, IReadOnlyList<MarkdownToken> tokens, int index)
    {
        var text = tokens[index];
        for (var k = index + 1; k < tokens.Count; k++)
        {
            // "](" follows the text directly; the address starts right after it.
            if (tokens[k].Kind == MarkdownTokenKind.LinkUrl && tokens[k].Start == text.End + 2)
            {
                return Target(line.Substring(tokens[k].Start, tokens[k].Length));
            }
        }

        return null;
    }

    /// <summary><c>url "title"</c> or <c>&lt;url&gt;</c> → url.</summary>
    private static string Target(string url)
    {
        url = url.Trim();
        if (url.Length > 1 && url[0] == '<' && url[^1] == '>')
        {
            return url[1..^1];
        }

        return url.IndexOf(' ') is var space and > 0 ? url[..space] : url;
    }

    private static bool IsWeb(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeMailto);
}
