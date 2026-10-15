namespace Zapisnik.Core.Markdown;

/// <summary>
/// Line-oriented markdown lexer for syntax highlighting (SPEC §4.4). It is deliberately not a full
/// CommonMark parser: it recognises what the editor styles, is allocation-free per line and
/// carries only <see cref="LineState"/> between lines so edits retokenize a few lines at most.
/// </summary>
public static class MarkdownLineLexer
{
    private const int MaxInlineDepth = 2;

    public static LineState Tokenize(ReadOnlySpan<char> line, LineState state, List<MarkdownToken> tokens)
    {
        switch (state.Kind)
        {
            case LineStateKind.FrontMatter:
                tokens.Add(new(0, line.Length, MarkdownTokenKind.FrontMatter));
                var t = line.TrimEnd();
                return t is "---" or "..." ? LineState.Normal : state;

            case LineStateKind.FencedCode:
                if (IsClosingFence(line, state))
                {
                    tokens.Add(new(0, line.Length, MarkdownTokenKind.CodeFence));
                    return LineState.Normal;
                }

                tokens.Add(new(0, line.Length, MarkdownTokenKind.CodeBlock));
                return state;

            case LineStateKind.DocumentStart when line.TrimEnd() is "---":
                tokens.Add(new(0, line.Length, MarkdownTokenKind.FrontMatter));
                return new LineState(LineStateKind.FrontMatter);
        }

        return TokenizeNormal(line, tokens);
    }

    private static LineState TokenizeNormal(ReadOnlySpan<char> line, List<MarkdownToken> tokens)
    {
        var indent = 0;
        while (indent < line.Length && line[indent] == ' ')
        {
            indent++;
        }

        if (indent >= line.Length)
        {
            return LineState.Normal;
        }

        var c = line[indent];

        // Fenced code
        if (indent <= 3 && c is '`' or '~')
        {
            var run = CountRun(line, indent, c);
            if (run >= 3 && (c == '~' || line[(indent + run)..].IndexOf('`') < 0))
            {
                tokens.Add(new(0, line.Length, MarkdownTokenKind.CodeFence));
                return new LineState(LineStateKind.FencedCode, c, run);
            }
        }

        // ATX heading
        if (indent <= 3 && c == '#')
        {
            var level = CountRun(line, indent, '#');
            var after = indent + level;
            if (level <= 6 && (after == line.Length || line[after] == ' ' || line[after] == '\t'))
            {
                var markerEnd = after;
                while (markerEnd < line.Length && line[markerEnd] is ' ' or '\t')
                {
                    markerEnd++;
                }

                tokens.Add(new(0, line.Length, (MarkdownTokenKind)((int)MarkdownTokenKind.Heading1 + level - 1)));
                tokens.Add(new(0, markerEnd, MarkdownTokenKind.Syntax));
                TokenizeInline(line, markerEnd, line.Length, tokens, 0);
                return LineState.Normal;
            }
        }

        // Horizontal rule
        if (indent <= 3 && c is '-' or '*' or '_' && IsHorizontalRule(line[indent..], c))
        {
            tokens.Add(new(0, line.Length, MarkdownTokenKind.HorizontalRule));
            return LineState.Normal;
        }

        var pos = indent;

        // Block quote(s)
        if (c == '>')
        {
            tokens.Add(new(0, line.Length, MarkdownTokenKind.Quote));
            while (pos < line.Length && line[pos] == '>')
            {
                var start = pos;
                pos++;
                if (pos < line.Length && line[pos] == ' ')
                {
                    pos++;
                }

                tokens.Add(new(start, pos - start, MarkdownTokenKind.Syntax));
                while (pos < line.Length && line[pos] == ' ')
                {
                    pos++;
                }
            }
        }

        // List item and task
        if (pos < line.Length)
        {
            var markerLength = ListMarkerLength(line, pos);
            if (markerLength > 0)
            {
                tokens.Add(new(pos, markerLength, MarkdownTokenKind.ListMarker));
                pos += markerLength;

                if (pos + 3 <= line.Length && line[pos] == '[' && line[pos + 2] == ']'
                    && (pos + 3 == line.Length || line[pos + 3] == ' '))
                {
                    var mark = line[pos + 1];
                    if (mark == ' ')
                    {
                        tokens.Add(new(pos, 3, MarkdownTokenKind.TaskOpen));
                        pos += 3;
                    }
                    else if (mark is 'x' or 'X')
                    {
                        tokens.Add(new(pos, 3, MarkdownTokenKind.TaskDone));
                        pos += 3;
                        if (pos < line.Length)
                        {
                            tokens.Add(new(pos, line.Length - pos, MarkdownTokenKind.TaskDoneText));
                        }
                    }
                }
            }
        }

        TokenizeInline(line, pos, line.Length, tokens, 0);
        return LineState.Normal;
    }

    /// <summary>Returns the length of a list marker including the following space, or 0.</summary>
    public static int ListMarkerLength(ReadOnlySpan<char> line, int pos)
    {
        if (pos >= line.Length)
        {
            return 0;
        }

        var c = line[pos];
        if (c is '-' or '*' or '+')
        {
            return pos + 1 < line.Length && line[pos + 1] == ' ' ? 2 : 0;
        }

        var i = pos;
        while (i < line.Length && i - pos < 9 && char.IsAsciiDigit(line[i]))
        {
            i++;
        }

        if (i > pos && i + 1 < line.Length && line[i] is '.' or ')' && line[i + 1] == ' ')
        {
            return i - pos + 2;
        }

        return 0;
    }

    private static void TokenizeInline(ReadOnlySpan<char> line, int start, int end, List<MarkdownToken> tokens, int depth)
    {
        var i = start;
        while (i < end)
        {
            var c = line[i];
            switch (c)
            {
                case '\\' when i + 1 < end && char.IsAsciiLetterOrDigit(line[i + 1]) is false:
                    tokens.Add(new(i, 1, MarkdownTokenKind.Syntax));
                    i += 2;
                    continue;

                case '`':
                {
                    var run = CountRun(line, i, '`', end);
                    var close = FindBacktickClose(line, i + run, end, run);
                    if (close >= 0)
                    {
                        tokens.Add(new(i, run, MarkdownTokenKind.Syntax));
                        tokens.Add(new(i + run, close - (i + run), MarkdownTokenKind.InlineCode));
                        tokens.Add(new(close, run, MarkdownTokenKind.Syntax));
                        i = close + run;
                    }
                    else
                    {
                        i += run;
                    }

                    continue;
                }

                case '[' when i + 1 < end && line[i + 1] == '[':
                {
                    var close = line[(i + 2)..end].IndexOf("]]");
                    if (close > 0)
                    {
                        var closeAbs = i + 2 + close;
                        tokens.Add(new(i, 2, MarkdownTokenKind.Syntax));
                        tokens.Add(new(i + 2, close, MarkdownTokenKind.WikiLink));
                        tokens.Add(new(closeAbs, 2, MarkdownTokenKind.Syntax));
                        i = closeAbs + 2;
                        continue;
                    }

                    break;
                }

                case '!' when i + 1 < end && line[i + 1] == '[':
                    if (TryLink(line, i + 1, end, out var imgTextEnd, out var imgEnd))
                    {
                        tokens.Add(new(i, imgEnd - i, MarkdownTokenKind.Image));
                        tokens.Add(new(i, 2, MarkdownTokenKind.Syntax));
                        tokens.Add(new(imgTextEnd, imgEnd - imgTextEnd, MarkdownTokenKind.Syntax));
                        i = imgEnd;
                        continue;
                    }

                    break;

                case '[':
                    if (TryLink(line, i, end, out var textEnd, out var linkEnd))
                    {
                        tokens.Add(new(i, 1, MarkdownTokenKind.Syntax));
                        tokens.Add(new(i + 1, textEnd - (i + 1), MarkdownTokenKind.LinkText));
                        tokens.Add(new(textEnd, 2, MarkdownTokenKind.Syntax)); // "]("
                        tokens.Add(new(textEnd + 2, linkEnd - 1 - (textEnd + 2), MarkdownTokenKind.LinkUrl));
                        tokens.Add(new(linkEnd - 1, 1, MarkdownTokenKind.Syntax)); // ")"
                        if (depth < MaxInlineDepth)
                        {
                            TokenizeInline(line, i + 1, textEnd, tokens, depth + 1);
                        }

                        i = linkEnd;
                        continue;
                    }

                    break;

                case '*' or '_':
                {
                    var run = CountRun(line, i, c, end);
                    if (TryEmphasis(line, i, end, c, run, out var used, out var closeAt))
                    {
                        var contentStart = i + used;
                        var kind = used switch
                        {
                            1 => MarkdownTokenKind.Emphasis,
                            _ => MarkdownTokenKind.Strong,
                        };
                        tokens.Add(new(contentStart, closeAt - contentStart, kind));
                        if (used == 3)
                        {
                            tokens.Add(new(contentStart, closeAt - contentStart, MarkdownTokenKind.Emphasis));
                        }

                        tokens.Add(new(i, used, MarkdownTokenKind.Syntax));
                        tokens.Add(new(closeAt, used, MarkdownTokenKind.Syntax));
                        if (depth < MaxInlineDepth)
                        {
                            TokenizeInline(line, contentStart, closeAt, tokens, depth + 1);
                        }

                        i = closeAt + used;
                    }
                    else
                    {
                        i += run;
                    }

                    continue;
                }

                case '~' when i + 1 < end && line[i + 1] == '~':
                {
                    var close = line[(i + 2)..end].IndexOf("~~");
                    if (close > 0)
                    {
                        var closeAbs = i + 2 + close;
                        tokens.Add(new(i + 2, close, MarkdownTokenKind.Strikethrough));
                        tokens.Add(new(i, 2, MarkdownTokenKind.Syntax));
                        tokens.Add(new(closeAbs, 2, MarkdownTokenKind.Syntax));
                        i = closeAbs + 2;
                        continue;
                    }

                    break;
                }

                case '#' when IsWordBoundaryBefore(line, i, start) && i + 1 < end && Tags.TagName.IsAllowedChar(line[i + 1]) && line[i + 1] != '-':
                {
                    var j = i + 1;
                    while (j < end && Tags.TagName.IsAllowedChar(line[j]))
                    {
                        j++;
                    }

                    tokens.Add(new(i, j - i, MarkdownTokenKind.Hashtag));
                    i = j;
                    continue;
                }

                case 'h' when IsWordBoundaryBefore(line, i, start) && StartsWithUrlScheme(line[i..end]):
                {
                    var j = i;
                    while (j < end && !char.IsWhiteSpace(line[j]) && line[j] != '<' && line[j] != '>')
                    {
                        j++;
                    }

                    while (j > i && line[j - 1] is '.' or ',' or ';' or ':' or '!' or '?' or ')' or '"' or '\'')
                    {
                        j--;
                    }

                    tokens.Add(new(i, j - i, MarkdownTokenKind.AutoLink));
                    i = j;
                    continue;
                }
            }

            i++;
        }
    }

    private static bool StartsWithUrlScheme(ReadOnlySpan<char> s) =>
        s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    private static bool IsWordBoundaryBefore(ReadOnlySpan<char> line, int i, int start) =>
        i == start || i == 0 || char.IsWhiteSpace(line[i - 1]) || line[i - 1] is '(' or '[' or ',' or ';';

    private static bool TryLink(ReadOnlySpan<char> line, int open, int end, out int textEnd, out int linkEnd)
    {
        textEnd = linkEnd = -1;
        var depth = 0;
        for (var j = open + 1; j < end; j++)
        {
            var ch = line[j];
            if (ch == '\\')
            {
                j++;
                continue;
            }

            if (ch == '[')
            {
                depth++;
            }
            else if (ch == ']')
            {
                if (depth > 0)
                {
                    depth--;
                    continue;
                }

                if (j + 1 < end && line[j + 1] == '(')
                {
                    var close = line[(j + 2)..end].IndexOf(')');
                    if (close >= 0)
                    {
                        textEnd = j;
                        linkEnd = j + 2 + close + 1;
                        return true;
                    }
                }

                return false;
            }
        }

        return false;
    }

    private static bool TryEmphasis(ReadOnlySpan<char> line, int i, int end, char c, int run, out int used, out int closeAt)
    {
        used = Math.Min(run, 3);
        closeAt = -1;

        var contentStart = i + run;
        if (contentStart >= end || char.IsWhiteSpace(line[contentStart]))
        {
            return false;
        }

        // Intra-word underscores are not emphasis (snake_case).
        if (c == '_' && i > 0 && char.IsLetterOrDigit(line[i - 1]))
        {
            return false;
        }

        for (var size = used; size >= 1; size--)
        {
            var j = i + size;
            while (j < end)
            {
                if (line[j] == '`')
                {
                    // Do not close inside a code span.
                    var r = CountRun(line, j, '`', end);
                    var close = FindBacktickClose(line, j + r, end, r);
                    j = close >= 0 ? close + r : j + r;
                    continue;
                }

                if (line[j] == c)
                {
                    var r = CountRun(line, j, c, end);
                    if (r >= size && !char.IsWhiteSpace(line[j - 1]) && j > i + size)
                    {
                        var after = j + size;
                        if (c == '_' && after < end && char.IsLetterOrDigit(line[after]))
                        {
                            j += r;
                            continue;
                        }

                        used = size;
                        closeAt = j;
                        return true;
                    }

                    j += r;
                    continue;
                }

                j++;
            }
        }

        return false;
    }

    private static int FindBacktickClose(ReadOnlySpan<char> line, int from, int end, int run)
    {
        var j = from;
        while (j < end)
        {
            if (line[j] == '`')
            {
                var r = CountRun(line, j, '`', end);
                if (r == run)
                {
                    return j;
                }

                j += r;
            }
            else
            {
                j++;
            }
        }

        return -1;
    }

    private static int CountRun(ReadOnlySpan<char> line, int start, char c) => CountRun(line, start, c, line.Length);

    private static int CountRun(ReadOnlySpan<char> line, int start, char c, int end)
    {
        var i = start;
        while (i < end && line[i] == c)
        {
            i++;
        }

        return i - start;
    }

    private static bool IsClosingFence(ReadOnlySpan<char> line, LineState state)
    {
        var trimmed = line.TrimStart(' ');
        if (line.Length - trimmed.Length > 3)
        {
            return false;
        }

        var run = CountRun(trimmed, 0, state.FenceChar);
        return run >= state.FenceLength && trimmed[run..].IsWhiteSpace();
    }

    private static bool IsHorizontalRule(ReadOnlySpan<char> s, char c)
    {
        var count = 0;
        foreach (var ch in s)
        {
            if (ch == c)
            {
                count++;
            }
            else if (ch is not (' ' or '\t'))
            {
                return false;
            }
        }

        return count >= 3;
    }
}
