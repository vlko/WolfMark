using System.Net;
using System.Text;

namespace WolfMark.Core.Markdown;

/// <summary>
/// Converts markdown (whole notes or a selection) to HTML or plain text for "copy as …" (SPEC §4.4).
/// Built on <see cref="MarkdownLineLexer"/> – same rules as the highlighting, no extra dependency, AOT-safe.
/// Covers what the editor styles: headings, paragraphs, lists (flat), tasks, quotes, fenced code, rules,
/// bold/italic/strike/code, links, wiki links, autolinks, images, tables (HTML table; tab-separated as text).
/// Front matter is dropped.
/// </summary>
public static class MarkdownConvert
{
    public static string ToHtml(string markdown) => Convert(markdown, html: true);

    public static string ToPlainText(string markdown) => Convert(markdown, html: false);

    private enum Block
    {
        None,
        Paragraph,
        Quote,
        BulletList,
        NumberedList,
        Code,
    }

    private static string Convert(string markdown, bool html)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder(markdown.Length + 64);
        var tokens = new List<MarkdownToken>();
        var state = LineState.DocumentStart;
        var block = Block.None;

        void Close()
        {
            if (html)
            {
                sb.Append(block switch
                {
                    Block.Paragraph => "</p>\n",
                    Block.Quote => "</p></blockquote>\n",
                    Block.BulletList => "</ul>\n",
                    Block.NumberedList => "</ol>\n",
                    Block.Code => "</code></pre>\n",
                    _ => string.Empty,
                });
            }

            block = Block.None;
        }

        void Open(Block next, string tag)
        {
            if (block == next)
            {
                return;
            }

            Close();
            block = next;
            if (html)
            {
                sb.Append(tag);
            }
        }

        var tables = FindTables(lines);
        var nextTable = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (nextTable < tables.Count && tables[nextTable].FirstLine == index)
            {
                Close();
                var table = tables[nextTable++];
                AppendTable(sb, lines, table, html);
                index = table.LastLine;
                continue;
            }

            tokens.Clear();
            var stateIn = state;
            state = MarkdownLineLexer.Tokenize(line, stateIn, tokens);
            var first = tokens.Count > 0 ? tokens[0].Kind : (MarkdownTokenKind?)null;

            if (first == MarkdownTokenKind.FrontMatter)
            {
                continue;
            }

            if (first == MarkdownTokenKind.CodeFence)
            {
                if (stateIn.Kind == LineStateKind.FencedCode)
                {
                    Close();
                }
                else
                {
                    Open(Block.Code, "<pre><code>");
                }

                continue;
            }

            if (first == MarkdownTokenKind.CodeBlock)
            {
                Open(Block.Code, "<pre><code>");
                sb.Append(html ? Escape(line) : line).Append('\n');
                continue;
            }

            if (line.Trim().Length == 0)
            {
                Close();
                if (!html)
                {
                    sb.Append('\n');
                }

                continue;
            }

            if (first is >= MarkdownTokenKind.Heading1 and <= MarkdownTokenKind.Heading6)
            {
                Close();
                var level = first.Value - MarkdownTokenKind.Heading1 + 1;
                if (html)
                {
                    sb.Append("<h").Append(level).Append('>');
                    AppendInline(sb, line, tokens, html);
                    sb.Append("</h").Append(level).Append(">\n");
                }
                else
                {
                    AppendInline(sb, line, tokens, html);
                    sb.Append('\n');
                }

                continue;
            }

            if (first == MarkdownTokenKind.HorizontalRule)
            {
                Close();
                sb.Append(html ? "<hr>\n" : "———\n");
                continue;
            }

            var marker = tokens.FirstOrDefault(t => t.Kind == MarkdownTokenKind.ListMarker);
            if (marker.Length > 0)
            {
                var numbered = char.IsAsciiDigit(line[marker.Start]);
                Open(numbered ? Block.NumberedList : Block.BulletList, numbered ? "<ol>\n" : "<ul>\n");
                var task = tokens.FirstOrDefault(t => t.Kind is MarkdownTokenKind.TaskOpen or MarkdownTokenKind.TaskDone);
                if (html)
                {
                    sb.Append("<li>");
                    if (task.Length > 0)
                    {
                        sb.Append(task.Kind == MarkdownTokenKind.TaskDone ? "<input type=\"checkbox\" checked disabled> " : "<input type=\"checkbox\" disabled> ");
                    }

                    AppendInline(sb, line, tokens, html);
                    sb.Append("</li>\n");
                }
                else
                {
                    sb.Append(line, 0, marker.Start);
                    if (task.Length > 0)
                    {
                        sb.Append(task.Kind == MarkdownTokenKind.TaskDone ? "☑ " : "☐ ");
                    }
                    else
                    {
                        sb.Append(line, marker.Start, marker.Length);
                    }

                    AppendInline(sb, line, tokens, html);
                    sb.Append('\n');
                }

                continue;
            }

            if (first == MarkdownTokenKind.Quote)
            {
                if (block == Block.Quote && html)
                {
                    sb.Append("<br>\n");
                }

                Open(Block.Quote, "<blockquote><p>");
                AppendInline(sb, line, tokens, html);
                if (!html)
                {
                    sb.Append('\n');
                }

                continue;
            }

            if (block == Block.Paragraph)
            {
                sb.Append(html ? "<br>\n" : string.Empty);
            }
            else
            {
                Open(Block.Paragraph, "<p>");
            }

            AppendInline(sb, line, tokens, html);
            if (!html)
            {
                sb.Append('\n');
            }
        }

        Close();
        return html ? sb.ToString().TrimEnd('\n') + "\n" : sb.ToString().TrimEnd('\n');
    }

    /// <summary>The document's tables; lines in code blocks and front matter hold none.</summary>
    private static List<MarkdownTable> FindTables(string[] lines)
    {
        var code = new bool[lines.Length];
        var tokens = new List<MarkdownToken>();
        var state = LineState.DocumentStart;
        for (var i = 0; i < lines.Length; i++)
        {
            tokens.Clear();
            var stateIn = state;
            state = MarkdownLineLexer.Tokenize(lines[i], stateIn, tokens);
            code[i] = stateIn.Kind is LineStateKind.FencedCode or LineStateKind.FrontMatter
                || tokens.Any(t => t.Kind is MarkdownTokenKind.CodeFence or MarkdownTokenKind.CodeBlock or MarkdownTokenKind.FrontMatter);
        }

        return MarkdownTable.Find(lines.Length, i => lines[i], i => code[i]);
    }

    /// <summary>A table as an HTML table (header in thead, alignment as text-align), or as tab-separated rows.</summary>
    private static void AppendTable(StringBuilder sb, string[] lines, MarkdownTable table, bool html)
    {
        var tokens = new List<MarkdownToken>();
        if (html)
        {
            sb.Append("<table>\n");
        }

        for (var r = 0; r < table.RowCount; r++)
        {
            var line = lines[table.LineOfRow(r)];
            tokens.Clear();
            MarkdownLineLexer.Tokenize(line, LineState.Normal, tokens);
            var cells = MarkdownTable.Cells(line);
            if (html)
            {
                sb.Append(r == 0 ? "<thead><tr>" : r == 1 ? "<tbody><tr>" : "<tr>");
            }

            for (var c = 0; c < table.ColumnCount; c++)
            {
                var (start, length) = c < cells.Length ? cells[c] : (line.Length, 0);
                List<MarkdownToken> cellTokens = [.. MarkdownTable.CellTokens(tokens, (start, length))];

                var cell = line.Substring(start, length).Replace("\\|", "|", StringComparison.Ordinal);
                if (html)
                {
                    var tag = r == 0 ? "th" : "td";
                    var align = table.Aligns[c] switch
                    {
                        TableAlign.Left => " style=\"text-align:left\"",
                        TableAlign.Center => " style=\"text-align:center\"",
                        TableAlign.Right => " style=\"text-align:right\"",
                        _ => string.Empty,
                    };
                    sb.Append('<').Append(tag).Append(align).Append('>');
                    AppendInline(sb, cell, cell.Length == length ? cellTokens : [], html);
                    sb.Append("</").Append(tag).Append('>');
                }
                else
                {
                    if (c > 0)
                    {
                        sb.Append('\t');
                    }

                    AppendInline(sb, cell, cell.Length == length ? cellTokens : [], html);
                }
            }

            if (html)
            {
                sb.Append(r == 0 ? "</tr></thead>\n" : "</tr>\n");
            }
            else
            {
                sb.Append('\n');
            }
        }

        if (html)
        {
            sb.Append(table.RowCount > 1 ? "</tbody></table>\n" : "</table>\n");
        }
    }

    /// <summary>Writes the line's inline content: delimiters dropped, spans as tags (HTML) or plain text.</summary>
    private static void AppendInline(StringBuilder sb, string line, List<MarkdownToken> tokens, bool html)
    {
        var skip = new bool[line.Length];
        var opens = new List<(int At, int Len, string Tag)>();
        var closes = new List<(int At, int Len, string Tag)>();

        foreach (var t in tokens)
        {
            switch (t.Kind)
            {
                case MarkdownTokenKind.Syntax or MarkdownTokenKind.LinkUrl or MarkdownTokenKind.ListMarker:
                    Array.Fill(skip, true, t.Start, Math.Min(t.Length, line.Length - t.Start));
                    break;
                case MarkdownTokenKind.TaskOpen or MarkdownTokenKind.TaskDone:
                    // "[ ] " – the checkbox replaces the marker and its space.
                    var taskLength = t.End < line.Length && line[t.End] == ' ' ? t.Length + 1 : t.Length;
                    Array.Fill(skip, true, t.Start, Math.Min(taskLength, line.Length - t.Start));
                    break;
            }
        }

        // Leading indentation before list markers / quotes is layout, not content.
        for (var i = 0; i < line.Length && line[i] == ' ' && tokens.Any(t => t.Kind is MarkdownTokenKind.ListMarker or MarkdownTokenKind.Quote); i++)
        {
            skip[i] = true;
        }

        foreach (var t in tokens)
        {
            if (!html || t.Length == 0)
            {
                continue;
            }

            switch (t.Kind)
            {
                case MarkdownTokenKind.Strong:
                    Add("<strong>", "</strong>");
                    break;
                case MarkdownTokenKind.Emphasis:
                    Add("<em>", "</em>");
                    break;
                case MarkdownTokenKind.Strikethrough:
                    Add("<del>", "</del>");
                    break;
                case MarkdownTokenKind.InlineCode:
                    Add("<code>", "</code>");
                    break;
                case MarkdownTokenKind.AutoLink:
                    Add($"<a href=\"{EscapeAttribute(line.Substring(t.Start, t.Length))}\">", "</a>");
                    break;
                case MarkdownTokenKind.WikiLink:
                    Add($"<a href=\"{EscapeAttribute(line.Substring(t.Start, t.Length))}\">", "</a>");
                    break;
                case MarkdownTokenKind.LinkText:
                {
                    var url = tokens.FirstOrDefault(u => u.Kind == MarkdownTokenKind.LinkUrl && u.Start > t.End);
                    var href = url.Length > 0 ? line.Substring(url.Start, url.Length) : string.Empty;
                    Add($"<a href=\"{EscapeAttribute(href)}\">", "</a>");
                    break;
                }

                case MarkdownTokenKind.Image:
                {
                    // ![alt](url): the whole token becomes one <img>.
                    if (MarkdownImageLink.TryParse(line.AsSpan(t.Start, t.Length), out var alt, out var src))
                    {
                        opens.Add((t.Start, 0, $"<img src=\"{EscapeAttribute(src)}\" alt=\"{EscapeAttribute(alt)}\">"));
                        Array.Fill(skip, true, t.Start, t.Length);
                    }

                    break;
                }
            }

            void Add(string open, string close)
            {
                opens.Add((t.Start, t.Length, open));
                closes.Add((t.End, t.Length, close));
            }
        }

        // Outer spans open first and close last.
        opens.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : b.Len.CompareTo(a.Len));
        closes.Sort((a, b) => a.At != b.At ? a.At.CompareTo(b.At) : a.Len.CompareTo(b.Len));

        int oi = 0, ci = 0;
        for (var i = 0; i <= line.Length; i++)
        {
            while (ci < closes.Count && closes[ci].At == i)
            {
                sb.Append(closes[ci++].Tag);
            }

            while (oi < opens.Count && opens[oi].At == i)
            {
                sb.Append(opens[oi++].Tag);
            }

            if (i < line.Length && !skip[i])
            {
                if (html)
                {
                    AppendEscaped(sb, line[i]);
                }
                else
                {
                    sb.Append(line[i]);
                }
            }
        }
    }

    private static string Escape(string s) => WebUtility.HtmlEncode(s);

    private static string EscapeAttribute(string s) => WebUtility.HtmlEncode(s);

    private static void AppendEscaped(StringBuilder sb, char c) => sb.Append(c switch
    {
        '<' => "&lt;",
        '>' => "&gt;",
        '&' => "&amp;",
        '"' => "&quot;",
        _ => c.ToString(),
    });
}
