namespace WolfMark.Core.Markdown;

/// <summary>Block type of a line, changed from the toolbar or the editor's context menu.</summary>
public enum BlockKind
{
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Quote,
    Bullet,
    Numbered,
    Task,
}

/// <summary>
/// Pure-text implementations of the editor shortcuts (SPEC §4.4, M4-06). Offsets assume '\n' line
/// breaks (one character), matching RichEdit positions. Each method returns null when the default
/// key behaviour should run instead.
/// </summary>
public static class MarkdownEditing
{
    public const string IndentUnit = "  ";

    /// <summary>Ctrl+B / Ctrl+I / code: wrap the selection in <paramref name="marker"/>, or unwrap it.</summary>
    public static TextEdit ToggleWrap(string text, int selStart, int selLength, string marker)
    {
        var m = marker.Length;
        var selected = text.AsSpan(selStart, selLength);

        // Selection includes the markers: **x** → x
        if (selLength >= 2 * m && selected.StartsWith(marker) && selected.EndsWith(marker))
        {
            var inner = selected[m..^m].ToString();
            return new TextEdit(selStart, selLength, inner, selStart, inner.Length);
        }

        // Markers right outside the selection: **[x]** → x
        if (selStart >= m && selStart + selLength + m <= text.Length
            && text.AsSpan(selStart - m, m).SequenceEqual(marker)
            && text.AsSpan(selStart + selLength, m).SequenceEqual(marker)
            && !(marker == "*" && IsDoubled(text, selStart - m, selStart + selLength, '*')))
        {
            return new TextEdit(selStart - m, selLength + (2 * m), selected.ToString(), selStart - m, selLength);
        }

        if (selLength == 0)
        {
            // Empty selection: wrap the word under the caret, or insert a marker pair.
            var (ws, we) = WordAt(text, selStart);
            if (we > ws)
            {
                var word = text[ws..we];
                return new TextEdit(ws, we - ws, marker + word + marker, selStart + m, 0);
            }

            return new TextEdit(selStart, 0, marker + marker, selStart + m, 0);
        }

        return new TextEdit(selStart, selLength, marker + selected.ToString() + marker, selStart + m, selLength);
    }

    /// <summary>Ctrl+K: <c>[selection](url)</c> with the url part selected.</summary>
    public static TextEdit InsertLink(string text, int selStart, int selLength)
    {
        var label = text.Substring(selStart, selLength);
        const string Url = "https://";
        if (label.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || label.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var insertUrl = "[](" + label + ")";
            return new TextEdit(selStart, selLength, insertUrl, selStart + 1, 0);
        }

        var insert = "[" + label + "](" + Url + ")";
        var urlStart = selStart + label.Length + 3;
        return new TextEdit(selStart, selLength, insert, urlStart, Url.Length);
    }

    /// <summary>Ctrl+Shift+L: none → "- [ ] ", list item → task, open task → done → open.</summary>
    public static TextEdit ToggleTask(string text, int caret)
    {
        var (lineStart, lineEnd) = LineAt(text, caret);
        var line = text.AsSpan(lineStart, lineEnd - lineStart);
        var indent = CountIndent(line);
        var markerLength = MarkdownLineLexer.ListMarkerLength(line, indent);

        if (markerLength > 0)
        {
            var afterMarker = lineStart + indent + markerLength;
            var rest = text.AsSpan(afterMarker, lineEnd - afterMarker);
            if (rest.StartsWith("[ ]"))
            {
                return new TextEdit(afterMarker + 1, 1, "x", caret, 0);
            }

            if (rest.StartsWith("[x]") || rest.StartsWith("[X]"))
            {
                return new TextEdit(afterMarker + 1, 1, " ", caret, 0);
            }

            return new TextEdit(afterMarker, 0, "[ ] ", caret + 4, 0);
        }

        return new TextEdit(lineStart + indent, 0, "- [ ] ", caret + 6, 0);
    }

    /// <summary>Enter inside a list item continues the list; Enter on an empty item ends it.</summary>
    public static TextEdit? ContinueList(string text, int caret)
    {
        var (lineStart, lineEnd) = LineAt(text, caret);
        var line = text.AsSpan(lineStart, lineEnd - lineStart);
        var indent = CountIndent(line);
        var markerLength = MarkdownLineLexer.ListMarkerLength(line, indent);
        var isQuote = markerLength == 0 && indent < line.Length && line[indent] == '>';

        if (markerLength == 0 && !isQuote)
        {
            return null;
        }

        if (caret < lineStart + indent + Math.Max(markerLength, 1))
        {
            return null; // caret inside the marker – plain Enter
        }

        string marker;
        if (isQuote)
        {
            marker = "> ";
            markerLength = line[indent..].StartsWith("> ") ? 2 : 1;
        }
        else
        {
            var raw = line.Slice(indent, markerLength).ToString();
            marker = raw;
            if (char.IsAsciiDigit(raw[0]))
            {
                var digits = raw.TrimEnd(' ', '.', ')');
                var n = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) + 1;
                marker = n.ToString(System.Globalization.CultureInfo.InvariantCulture) + raw[digits.Length..];
            }
        }

        var contentStart = lineStart + indent + markerLength;
        var content = text.AsSpan(contentStart, lineEnd - contentStart);
        var isTask = content.StartsWith("[ ] ") || content.StartsWith("[x] ") || content.StartsWith("[X] ")
                     || content is "[ ]" or "[x]" or "[X]";
        var body = isTask ? content[Math.Min(4, content.Length)..] : content;

        if (body.IsWhiteSpace() && caret == lineEnd)
        {
            // Empty item: remove the marker and end the list.
            return new TextEdit(lineStart, lineEnd - lineStart, string.Empty, lineStart, 0);
        }

        var insert = "\n" + new string(' ', indent) + marker + (isTask ? "[ ] " : string.Empty);
        return new TextEdit(caret, 0, insert, caret + insert.Length, 0);
    }

    /// <summary>Tab / Shift+Tab on list lines indents or outdents all selected lines.</summary>
    public static TextEdit? Indent(string text, int selStart, int selLength, bool outdent)
    {
        var (blockStart, _) = LineAt(text, selStart);
        var (_, blockEnd) = LineAt(text, selStart + selLength);
        var block = text[blockStart..blockEnd];
        var lines = block.Split('\n');

        var anyList = false;
        foreach (var l in lines)
        {
            var ind = CountIndent(l);
            if (MarkdownLineLexer.ListMarkerLength(l, ind) > 0)
            {
                anyList = true;
                break;
            }
        }

        if (!anyList)
        {
            return null;
        }

        var firstDelta = 0;
        var totalDelta = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var before = lines[i].Length;
            if (outdent)
            {
                var remove = Math.Min(IndentUnit.Length, CountIndent(lines[i]));
                lines[i] = lines[i][remove..];
            }
            else if (lines[i].Length > 0)
            {
                lines[i] = IndentUnit + lines[i];
            }

            var delta = lines[i].Length - before;
            if (i == 0)
            {
                firstDelta = delta;
            }

            totalDelta += delta;
        }

        var newBlock = string.Join('\n', lines);
        var newSelStart = Math.Max(blockStart, selStart + firstDelta);
        var newSelLength = Math.Max(0, selLength + totalDelta - firstDelta);
        return new TextEdit(blockStart, block.Length, newBlock, newSelStart, selLength == 0 ? 0 : newSelLength);
    }

    /// <summary>Current block type of the line containing <paramref name="caret"/>.</summary>
    public static BlockKind GetBlock(string text, int caret)
    {
        var (lineStart, lineEnd) = LineAt(text, caret);
        return ParsePrefix(text.AsSpan(lineStart, lineEnd - lineStart)).Kind;
    }

    /// <summary>Replaces the line's block prefix (heading, quote, list, task) with the one of <paramref name="kind"/>.</summary>
    public static TextEdit SetBlock(string text, int caret, BlockKind kind)
    {
        var (lineStart, lineEnd) = LineAt(text, caret);
        var line = text.AsSpan(lineStart, lineEnd - lineStart);
        var prefix = ParsePrefix(line);

        // Lists keep their indentation; headings, quotes and paragraphs start at the margin.
        var keepIndent = kind is BlockKind.Bullet or BlockKind.Numbered or BlockKind.Task;
        var indent = keepIndent ? new string(' ', prefix.Indent) : string.Empty;
        var newPrefix = indent + kind switch
        {
            BlockKind.Heading1 => "# ",
            BlockKind.Heading2 => "## ",
            BlockKind.Heading3 => "### ",
            BlockKind.Quote => "> ",
            BlockKind.Bullet => "- ",
            BlockKind.Numbered => "1. ",
            BlockKind.Task => "- [ ] ",
            _ => string.Empty,
        };

        var newCaret = Math.Max(lineStart + newPrefix.Length, caret - prefix.Length + newPrefix.Length);
        return new TextEdit(lineStart, prefix.Length, newPrefix, newCaret, 0);
    }

    /// <summary>H1–H3 toolbar buttons: set the heading level, or back to a paragraph if it is already that level.</summary>
    public static TextEdit ToggleHeading(string text, int caret, int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 3);
        var kind = BlockKind.Heading1 + (level - 1);
        return SetBlock(text, caret, GetBlock(text, caret) == kind ? BlockKind.Paragraph : kind);
    }

    /// <summary>
    /// Removes the delimiters around an inline span (toggling bold/italic/code off while the caret is
    /// inside it). The caret keeps its position relative to the content.
    /// </summary>
    public static TextEdit Unwrap(string text, int openStart, int openLength, int closeStart, int closeLength, int caret)
    {
        var content = text.Substring(openStart + openLength, closeStart - (openStart + openLength));
        var relative = Math.Clamp(caret - (openStart + openLength), 0, content.Length);
        return new TextEdit(openStart, closeStart + closeLength - openStart, content, openStart + relative, 0);
    }

    private readonly record struct Prefix(BlockKind Kind, int Indent, int Length);

    private static Prefix ParsePrefix(ReadOnlySpan<char> line)
    {
        var indent = CountIndent(line);
        var i = indent;

        var hashes = 0;
        while (i + hashes < line.Length && line[i + hashes] == '#')
        {
            hashes++;
        }

        if (hashes is >= 1 and <= 6 && (i + hashes == line.Length || line[i + hashes] == ' '))
        {
            var end = i + hashes;
            while (end < line.Length && line[end] == ' ')
            {
                end++;
            }

            var kind = hashes switch { 1 => BlockKind.Heading1, 2 => BlockKind.Heading2, 3 => BlockKind.Heading3, _ => BlockKind.Heading3 };
            return new Prefix(kind, indent, end);
        }

        if (i < line.Length && line[i] == '>')
        {
            while (i < line.Length && line[i] is '>' or ' ')
            {
                i++;
            }

            return new Prefix(BlockKind.Quote, indent, i);
        }

        var marker = MarkdownLineLexer.ListMarkerLength(line, i);
        if (marker > 0)
        {
            var numbered = char.IsAsciiDigit(line[i]);
            i += marker;
            if (i + 3 <= line.Length && line[i] == '[' && line[i + 2] == ']' && line[i + 1] is ' ' or 'x' or 'X')
            {
                i += 3;
                if (i < line.Length && line[i] == ' ')
                {
                    i++;
                }

                return new Prefix(BlockKind.Task, indent, i);
            }

            return new Prefix(numbered ? BlockKind.Numbered : BlockKind.Bullet, indent, i);
        }

        return new Prefix(BlockKind.Paragraph, indent, 0);
    }

    public static (int Start, int End) LineAt(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var start = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        var end = text.IndexOf('\n', offset);
        return (start, end < 0 ? text.Length : end);
    }

    private static (int Start, int End) WordAt(string text, int offset)
    {
        var s = offset;
        while (s > 0 && char.IsLetterOrDigit(text[s - 1]))
        {
            s--;
        }

        var e = offset;
        while (e < text.Length && char.IsLetterOrDigit(text[e]))
        {
            e++;
        }

        return (s, e);
    }

    private static bool IsDoubled(string text, int before, int after, char c) =>
        (before > 0 && text[before - 1] == c) || (after + 1 < text.Length && text[after + 1] == c);

    private static int CountIndent(ReadOnlySpan<char> line)
    {
        var i = 0;
        while (i < line.Length && line[i] == ' ')
        {
            i++;
        }

        return i;
    }
}
