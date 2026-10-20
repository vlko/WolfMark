namespace WolfMark.Core.Markdown;

/// <summary>Result of <see cref="MarkdownDocumentModel.Update"/>: which lines got new tokens.</summary>
/// <param name="FirstLine">First retokenized line (= first changed line).</param>
/// <param name="LineCount">Number of retokenized lines (0 = nothing changed).</param>
public readonly record struct MarkdownUpdate(int FirstLine, int LineCount)
{
    /// <summary>Lines removed at <see cref="FirstLine"/> by the edit.</summary>
    public int RemovedLines { get; init; }

    /// <summary>Lines inserted at <see cref="FirstLine"/> by the edit.</summary>
    public int InsertedLines { get; init; }

    /// <summary>Maps a line index from before the edit to after it; -1 if that line was replaced.</summary>
    public int MapLine(int oldLine) =>
        oldLine < FirstLine ? oldLine
        : oldLine >= FirstLine + RemovedLines ? oldLine + InsertedLines - RemovedLines
        : -1;
}

/// <summary>
/// Incremental, line-based tokenisation of a whole document. Keeps the lexer state at each line
/// boundary and on edit retokenizes from the first changed line until the state converges again.
/// Not thread-safe: own one instance per editor and call it from one thread at a time.
/// </summary>
public sealed class MarkdownDocumentModel
{
    private readonly List<Line> _lines = [];
    private readonly List<MarkdownToken> _scratch = [];
    private int[] _lineStarts = [];
    private bool _lineStartsDirty = true;

    public int LineCount => _lines.Count;

    /// <summary>Total number of line tokenisations performed (diagnostics/tests).</summary>
    public long TokenizedLines { get; private set; }

    public string GetLineText(int line) => _lines[line].Text;

    public IReadOnlyList<MarkdownToken> GetTokens(int line) => _lines[line].Tokens;

    public LineState GetStateAtLineStart(int line) => _lines[line].StateIn;

    /// <summary>Offset of the line start in the document ('\n' counts as one character).</summary>
    public int GetLineStart(int line)
    {
        EnsureLineStarts();
        return _lineStarts[line];
    }

    /// <summary>Line containing the given offset.</summary>
    public int GetLineAtOffset(int offset)
    {
        EnsureLineStarts();
        var idx = Array.BinarySearch(_lineStarts, 0, _lines.Count, offset);
        return idx >= 0 ? idx : Math.Max(0, ~idx - 1);
    }

    /// <remarks>
    /// The unchanged lines before and after the edit are compared in place (spans), so typing in a long note
    /// allocates only the changed lines – splitting the whole text on every keystroke churned the GC.
    /// </remarks>
    public MarkdownUpdate Update(string text)
    {
        var span = text.AsSpan();
        var oldCount = _lines.Count;
        var newCount = span.Count('\n') + 1;
        var max = Math.Min(oldCount, newCount);

        // Common leading lines; `head` ends up at the start of the first differing line.
        var prefix = 0;
        var head = 0;
        while (prefix < max)
        {
            var nl = span[head..].IndexOf('\n');
            var lineEnd = nl < 0 ? span.Length : head + nl;
            if (!SameLine(_lines[prefix].Text, span[head..lineEnd]))
            {
                break;
            }

            prefix++;
            head = lineEnd + 1;
        }

        // Common trailing lines, not overlapping the leading ones; `tail` ends up at the end of the last differing
        // line (exclusive, before its '\n').
        var suffix = 0;
        var tail = span.Length;
        while (suffix < max - prefix)
        {
            var nl = span[..tail].LastIndexOf('\n');
            if (!SameLine(_lines[oldCount - 1 - suffix].Text, span[(nl + 1)..tail]))
            {
                break;
            }

            suffix++;
            tail = nl; // -1 only after matching the first line, which ends the loop
        }

        var removed = oldCount - prefix - suffix;
        var inserted = newCount - prefix - suffix;

        if (removed == 0 && inserted == 0)
        {
            return new MarkdownUpdate(prefix, 0);
        }

        var diff = new MarkdownUpdate(prefix, 0) { RemovedLines = removed, InsertedLines = inserted };

        _lines.RemoveRange(prefix, removed);
        var insertedLines = new Line[inserted];
        var pos = head;
        for (var k = 0; k < inserted; k++)
        {
            var nl = k == inserted - 1 ? -1 : span[pos..].IndexOf('\n');
            var lineEnd = nl < 0 ? Math.Max(pos, tail) : pos + nl;
            insertedLines[k] = new Line(StripCr(span[pos..lineEnd]).ToString());
            pos = lineEnd + 1;
        }

        _lines.InsertRange(prefix, insertedLines);
        _lineStartsDirty = true;

        var state = prefix == 0 ? LineState.DocumentStart : _lines[prefix - 1].StateOut;
        var i = prefix;
        var changedEnd = prefix + inserted;
        while (i < _lines.Count)
        {
            var line = _lines[i];
            if (i >= changedEnd && line.Tokenized && line.StateIn == state)
            {
                break; // converged
            }

            _scratch.Clear();
            var outState = MarkdownLineLexer.Tokenize(line.Text, state, _scratch);
            line.StateIn = state;
            line.StateOut = outState;
            line.Tokens = _scratch.ToArray();
            line.Tokenized = true;
            TokenizedLines++;
            state = outState;
            i++;
        }

        return diff with { LineCount = i - prefix };
    }

    private static bool SameLine(string line, ReadOnlySpan<char> text) => StripCr(text).SequenceEqual(line);

    private static ReadOnlySpan<char> StripCr(ReadOnlySpan<char> line) => line.EndsWith('\r') ? line[..^1] : line;

    private void EnsureLineStarts()
    {
        if (!_lineStartsDirty)
        {
            return;
        }

        if (_lineStarts.Length < _lines.Count)
        {
            _lineStarts = new int[Math.Max(_lines.Count, _lineStarts.Length * 2)];
        }

        var offset = 0;
        for (var k = 0; k < _lines.Count; k++)
        {
            _lineStarts[k] = offset;
            offset += _lines[k].Text.Length + 1;
        }

        _lineStartsDirty = false;
    }

    private sealed class Line(string text)
    {
        public string Text { get; } = text;

        public LineState StateIn { get; set; }

        public LineState StateOut { get; set; }

        public MarkdownToken[] Tokens { get; set; } = [];

        public bool Tokenized { get; set; }
    }
}
