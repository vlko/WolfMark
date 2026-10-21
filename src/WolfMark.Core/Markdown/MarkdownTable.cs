namespace WolfMark.Core.Markdown;

public enum TableAlign : byte
{
    Default,
    Left,
    Center,
    Right,
}

/// <summary>
/// A GFM pipe table: a header row, the delimiter row (<c>|---|:-:|</c>) and the body rows below it, up to the first
/// line without a pipe (GFM also ends it at a blank line; a line without any pipe is taken as text, not as a row).
/// Lines are document line numbers; the delimiter row is <see cref="FirstLine"/> + 1.
/// </summary>
public sealed record MarkdownTable(int FirstLine, int LineCount, IReadOnlyList<TableAlign> Aligns)
{
    public int LastLine => FirstLine + LineCount - 1;

    public int ColumnCount => Aligns.Count;

    /// <summary>Rows as shown: the header, then the body (the delimiter row left out).</summary>
    public int RowCount => LineCount - 1;

    public bool Contains(int line) => line >= FirstLine && line <= LastLine;

    /// <summary>The document line of a shown row.</summary>
    public int LineOfRow(int row) => row == 0 ? FirstLine : FirstLine + 1 + row;

    /// <summary>
    /// The tables of a document, in order. <paramref name="isCode"/> marks lines inside code blocks or front matter,
    /// which never hold a table.
    /// </summary>
    public static List<MarkdownTable> Find(int lineCount, Func<int, string> lineText, Func<int, bool> isCode)
    {
        var tables = new List<MarkdownTable>();
        for (var line = 1; line < lineCount; line++)
        {
            var header = lineText(line - 1);
            if (!header.Contains('|') || isCode(line - 1) || isCode(line) || !TryParseDelimiter(lineText(line), out var aligns)
                || CountCells(header) != aligns.Length)
            {
                continue;
            }

            var last = line;
            while (last + 1 < lineCount && !isCode(last + 1) && IsRow(lineText(last + 1)))
            {
                last++;
            }

            tables.Add(new MarkdownTable(line - 1, last - line + 2, aligns));
            line = last + 1; // the line after the table can start no table (its header would be the last row)
        }

        return tables;
    }

    /// <summary>A body row: some text with an unescaped pipe.</summary>
    public static bool IsRow(string line) => !string.IsNullOrWhiteSpace(line) && Pipes(line).Count > 0;

    /// <summary>
    /// The delimiter row: cells of dashes with optional colons (<c>:--</c> left, <c>:-:</c> center, <c>--:</c>
    /// right) between pipes; at least one pipe.
    /// </summary>
    public static bool TryParseDelimiter(string line, out TableAlign[] aligns)
    {
        aligns = [];
        if (!line.Contains('|'))
        {
            return false;
        }

        var cells = Cells(line);
        var result = new TableAlign[cells.Length];
        for (var c = 0; c < cells.Length; c++)
        {
            var cell = line.AsSpan(cells[c].Start, cells[c].Length);
            if (cell.Length == 0)
            {
                return false;
            }

            var left = cell[0] == ':';
            var right = cell[^1] == ':';
            var dashes = cell[(left ? 1 : 0)..(cell.Length - (right && cell.Length > 1 ? 1 : 0))];
            if (dashes.Length == 0 || dashes.ContainsAnyExcept('-'))
            {
                return false;
            }

            result[c] = (left, right) switch
            {
                (true, true) => TableAlign.Center,
                (true, false) => TableAlign.Left,
                (false, true) => TableAlign.Right,
                _ => TableAlign.Default,
            };
        }

        aligns = result;
        return true;
    }

    /// <summary>
    /// The cells of a row as source ranges, trimmed; the outer pipes are optional, <c>\|</c> is a pipe inside a cell.
    /// </summary>
    public static (int Start, int Length)[] Cells(string line)
    {
        var pipes = Pipes(line);
        var start = 0;
        var end = line.Length;
        var first = 0;
        var count = pipes.Count;
        var leading = line.AsSpan().TrimStart().Length;
        if (count > 0 && pipes[0] == line.Length - leading)
        {
            start = pipes[0] + 1; // leading pipe
            first = 1;
        }

        var trimmedEnd = line.AsSpan().TrimEnd().Length;
        if (count > first && pipes[count - 1] == trimmedEnd - 1)
        {
            end = pipes[count - 1]; // trailing pipe
            count--;
        }

        var cells = new (int Start, int Length)[count - first + 1];
        var from = start;
        for (var k = 0; k < cells.Length; k++)
        {
            var to = k < cells.Length - 1 ? pipes[first + k] : end;
            cells[k] = Trim(line, from, to);
            from = to + 1;
        }

        return cells;
    }

    /// <summary>The number of cells in a row (see <see cref="Cells"/>).</summary>
    public static int CountCells(string line) => Cells(line).Length;

    /// <summary>The source columns of a line's cell separators (unescaped pipes).</summary>
    public static List<int> Pipes(string line)
    {
        var pipes = new List<int>();
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\')
            {
                i++; // an escaped character, \| included
            }
            else if (line[i] == '|')
            {
                pipes.Add(i);
            }
        }

        return pipes;
    }

    /// <summary>A line's tokens inside one cell, in cell columns (clipped to the cell).</summary>
    public static MarkdownToken[] CellTokens(IReadOnlyList<MarkdownToken> tokens, (int Start, int Length) cell)
    {
        var end = cell.Start + cell.Length;
        var result = new List<MarkdownToken>();
        foreach (var t in tokens)
        {
            var s = Math.Max(t.Start, cell.Start);
            var e = Math.Min(t.End, end);
            if (e > s)
            {
                result.Add(new MarkdownToken(s - cell.Start, e - s, t.Kind));
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Column widths for a grid: each its natural width when they fit; otherwise the narrow columns keep theirs and
    /// the wide ones share the rest equally (water filling), never below <paramref name="minimum"/>.
    /// </summary>
    public static double[] FitColumns(IReadOnlyList<double> natural, double available, double minimum)
    {
        var widths = natural.Select(w => Math.Max(w, minimum)).ToArray();
        if (widths.Sum() <= available)
        {
            return widths;
        }

        var open = Enumerable.Range(0, widths.Length).ToList();
        var left = available;
        while (open.Count > 0)
        {
            var share = left / open.Count;
            var narrow = open.Where(c => widths[c] <= share).ToList();
            if (narrow.Count == 0)
            {
                foreach (var c in open)
                {
                    widths[c] = Math.Max(minimum, Math.Floor(share));
                }

                break;
            }

            foreach (var c in narrow)
            {
                left -= widths[c];
                open.Remove(c);
            }
        }

        return widths;
    }

    private static (int Start, int Length) Trim(string line, int from, int to)
    {
        while (from < to && char.IsWhiteSpace(line[from]))
        {
            from++;
        }

        while (to > from && char.IsWhiteSpace(line[to - 1]))
        {
            to--;
        }

        return (from, to - from);
    }
}
