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

    public MarkdownUpdate Update(string text)
    {
        var newLines = text.Split('\n');
        for (var k = 0; k < newLines.Length; k++)
        {
            if (newLines[k].EndsWith('\r'))
            {
                newLines[k] = newLines[k][..^1];
            }
        }

        var oldCount = _lines.Count;
        var newCount = newLines.Length;

        var prefix = 0;
        var max = Math.Min(oldCount, newCount);
        while (prefix < max && string.Equals(_lines[prefix].Text, newLines[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < max - prefix
               && string.Equals(_lines[oldCount - 1 - suffix].Text, newLines[newCount - 1 - suffix], StringComparison.Ordinal))
        {
            suffix++;
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
        for (var k = 0; k < inserted; k++)
        {
            insertedLines[k] = new Line(newLines[prefix + k]);
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
