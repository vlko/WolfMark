namespace WolfMark.Core.Markdown;

/// <summary>
/// What one line shows in the editor. In formatted mode the delimiters (<see cref="MarkdownTokenKind.Syntax"/>,
/// <see cref="MarkdownTokenKind.LinkUrl"/>) are left out of the shown text; columns map between the source line and
/// the shown text. A caret between shown characters sits after the hidden ones before it: at the start of
/// "**bold**" it is inside the bold text (after "**"), at its end after the closing "**".
/// </summary>
public sealed class LinePresentation
{
    private readonly int[] _sourceOfShown; // source column of each shown column (one more for the line end)
    private readonly int[] _shownBefore;   // shown characters before each source column (one more for the end)

    private LinePresentation(string text, MarkdownToken[] tokens, int[] sourceOfShown, int[] shownBefore)
    {
        Text = text;
        Tokens = tokens;
        _sourceOfShown = sourceOfShown;
        _shownBefore = shownBefore;
    }

    /// <summary>The shown text.</summary>
    public string Text { get; }

    /// <summary>Tokens in shown columns, in lexer order (later ones refine earlier ones); hidden tokens left out.</summary>
    public IReadOnlyList<MarkdownToken> Tokens { get; }

    public int SourceLength => _shownBefore.Length - 1;

    public static bool IsHiding(MarkdownTokenKind kind) => kind is MarkdownTokenKind.Syntax or MarkdownTokenKind.LinkUrl;

    public static LinePresentation Create(string line, IReadOnlyList<MarkdownToken> tokens, bool hideSyntax)
    {
        var hidden = new bool[line.Length];
        if (hideSyntax)
        {
            foreach (var t in tokens)
            {
                if (IsHiding(t.Kind))
                {
                    for (var i = Math.Max(0, t.Start); i < Math.Min(line.Length, t.End); i++)
                    {
                        hidden[i] = true;
                    }
                }
            }
        }

        var shownBefore = new int[line.Length + 1];
        var sourceOfShown = new List<int>(line.Length + 1);
        var chars = new char[line.Length];
        var shown = 0;
        for (var i = 0; i < line.Length; i++)
        {
            shownBefore[i] = shown;
            if (!hidden[i])
            {
                chars[shown++] = line[i];
                sourceOfShown.Add(i);
            }
        }

        shownBefore[line.Length] = shown;
        sourceOfShown.Add(line.Length);

        var shownTokens = new List<MarkdownToken>(tokens.Count);
        foreach (var t in tokens)
        {
            if (hideSyntax && IsHiding(t.Kind))
            {
                continue;
            }

            var start = shownBefore[Math.Clamp(t.Start, 0, line.Length)];
            var end = shownBefore[Math.Clamp(t.End, 0, line.Length)];
            if (end > start)
            {
                shownTokens.Add(new MarkdownToken(start, end - start, t.Kind));
            }
        }

        // Every column shown keeps the source as is: one array instead of a mapping per column.
        var text = shown == line.Length ? line : new string(chars, 0, shown);
        return new LinePresentation(text, [.. shownTokens], [.. sourceOfShown], shownBefore);
    }

    /// <summary>Source column of a caret at <paramref name="shownColumn"/> (see the class remarks).</summary>
    public int ToSource(int shownColumn) => _sourceOfShown[Math.Clamp(shownColumn, 0, Text.Length)];

    /// <summary>Shown column of a caret at <paramref name="sourceColumn"/>; inside hidden text it is the next shown position.</summary>
    public int ToShown(int sourceColumn) => _shownBefore[Math.Clamp(sourceColumn, 0, SourceLength)];

    /// <summary>Source range of the shown characters [<paramref name="shownStart"/>, <paramref name="shownEnd"/>).</summary>
    public (int Start, int End) SourceRangeOf(int shownStart, int shownEnd)
    {
        shownStart = Math.Clamp(shownStart, 0, Text.Length);
        shownEnd = Math.Clamp(shownEnd, shownStart, Text.Length);
        return shownEnd == shownStart
            ? (ToSource(shownStart), ToSource(shownStart))
            : (_sourceOfShown[shownStart], _sourceOfShown[shownEnd - 1] + 1);
    }
}
