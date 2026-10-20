using System.Buffers;

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
        var length = line.Length;
        bool[]? rented = null;
        var hidden = length <= 512 ? stackalloc bool[length] : (rented = ArrayPool<bool>.Shared.Rent(length)).AsSpan(0, length);
        hidden.Clear();
        var anyHidden = false;
        if (hideSyntax)
        {
            foreach (var t in tokens)
            {
                if (IsHiding(t.Kind))
                {
                    var start = Math.Clamp(t.Start, 0, length);
                    var end = Math.Clamp(t.End, start, length);
                    hidden[start..end].Fill(true);
                    anyHidden |= end > start;
                }
            }
        }

        try
        {
            var shownBefore = new int[length + 1];
            if (!anyHidden)
            {
                // Everything shown: the source itself, one column map for both directions.
                for (var i = 0; i <= length; i++)
                {
                    shownBefore[i] = i;
                }

                return new LinePresentation(line, tokens as MarkdownToken[] ?? [.. tokens], shownBefore, shownBefore);
            }

            var shown = 0;
            for (var i = 0; i < length; i++)
            {
                shownBefore[i] = shown;
                shown += hidden[i] ? 0 : 1;
            }

            shownBefore[length] = shown;
            var sourceOfShown = new int[shown + 1];
            for (int i = 0, d = 0; i < length; i++)
            {
                if (!hidden[i])
                {
                    sourceOfShown[d++] = i;
                }
            }

            sourceOfShown[shown] = length;
            var text = string.Create(shown, (line, sourceOfShown), static (span, state) =>
            {
                for (var d = 0; d < span.Length; d++)
                {
                    span[d] = state.line[state.sourceOfShown[d]];
                }
            });

            var shownTokens = new List<MarkdownToken>(tokens.Count);
            foreach (var t in tokens)
            {
                if (IsHiding(t.Kind))
                {
                    continue;
                }

                var start = shownBefore[Math.Clamp(t.Start, 0, length)];
                var end = shownBefore[Math.Clamp(t.End, 0, length)];
                if (end > start)
                {
                    shownTokens.Add(new MarkdownToken(start, end - start, t.Kind));
                }
            }

            return new LinePresentation(text, [.. shownTokens], sourceOfShown, shownBefore);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<bool>.Shared.Return(rented);
            }
        }
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
