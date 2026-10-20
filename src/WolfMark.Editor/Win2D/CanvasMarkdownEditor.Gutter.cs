using System.Globalization;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor.Win2D;

/// <summary>
/// Line numbers in a gutter left of the text and the marker chips of the caret line – the same look as the
/// RichEdit editor, but drawn in the same pass as the text from the line layouts (no position queries).
/// </summary>
public sealed partial class CanvasMarkdownEditor
{
    private const int MinNumberDigits = 3;
    private const string WrapGlyph = "↪"; // ↪ on the rows a long line wraps onto

    private bool _showLineNumbers;
    private int _numberDigits = MinNumberDigits;
    private CanvasTextFormat? _numberFormat;
    private CanvasTextFormat? _chipFormat;
    private float _numberBaseline;
    private readonly Dictionary<string, CanvasTextLayout> _numberLayouts = new(StringComparer.Ordinal);
    private float _numberLayoutWidth;

    /// <summary>Shows a number before each line.</summary>
    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set
        {
            if (_showLineNumbers != value)
            {
                _showLineNumbers = value;
                OnViewportResized(); // the text gets narrower or wider
                _canvas.Invalidate();
            }
        }
    }

    private float NumberSize => (float)Math.Round(_theme.FontSize * 0.78);

    /// <summary>Room the numbers take left of the text (0 when hidden).</summary>
    private double Gutter => _showLineNumbers ? Math.Ceiling(_numberDigits * NumberSize * 0.62) + 14 : 0;

    /// <summary>More lines than digits (1,000th line): the gutter grows and the text is laid out again.</summary>
    private void UpdateNumberDigits()
    {
        var digits = Math.Max(MinNumberDigits, _doc.LineCount.ToString(CultureInfo.InvariantCulture).Length);
        if (digits != _numberDigits)
        {
            _numberDigits = digits;
            if (_showLineNumbers)
            {
                OnViewportResized();
            }
        }
    }

    private void EnsureGutterFormats()
    {
        if (_numberFormat is not null && Math.Abs(_numberFormat.FontSize - NumberSize) < 0.01f)
        {
            return;
        }

        _numberFormat?.Dispose();
        _chipFormat?.Dispose();
        ClearNumberLayouts();
        var mono = MarkdownTheme.ToFaceName(_theme.MonoFontFamily);
        _numberFormat = new CanvasTextFormat
        {
            FontFamily = mono,
            FontSize = NumberSize,
            HorizontalAlignment = CanvasHorizontalAlignment.Right,
            WordWrapping = CanvasWordWrapping.NoWrap,
        };
        _chipFormat = new CanvasTextFormat { FontFamily = mono, FontSize = 9, WordWrapping = CanvasWordWrapping.NoWrap };
        using var probe = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), "0", _numberFormat, 100, 0);
        _numberBaseline = probe.LineMetrics[0].Baseline;
    }

    /// <summary>The tinted band behind the numbers, over the drawn region.</summary>
    private void DrawGutterBand(CanvasDrawingSession ds, Rect region)
    {
        if (!_showLineNumbers)
        {
            return;
        }

        var left = (float)(PadLeft - Gutter - 4);
        ds.FillRectangle(left, (float)region.Top, (float)Gutter, (float)region.Height, WithAlpha(_theme.Text, 12));
    }

    /// <summary>The line's number on its first row (baselines aligned) and ↪ on the rows it wraps onto.</summary>
    private void DrawLineNumber(CanvasDrawingSession ds, int line, CanvasTextLayout layout, float top, bool active)
    {
        if (!_showLineNumbers)
        {
            return;
        }

        EnsureGutterFormats();
        var width = (float)Gutter - 10;
        var left = (float)(PadLeft - Gutter - 4);
        var color = active ? _theme.Text : _theme.Muted;
        var rowTop = 0f;
        var first = true;
        if (Math.Abs(width - _numberLayoutWidth) > 0.01f)
        {
            ClearNumberLayouts(); // right-aligned in the gutter: a wider gutter needs new layouts
            _numberLayoutWidth = width;
        }

        foreach (var row in layout.LineMetrics)
        {
            var y = top + rowTop + row.Baseline - _numberBaseline;
            var label = first ? (line + 1).ToString(CultureInfo.InvariantCulture) : WrapGlyph;
            ds.DrawTextLayout(NumberLayout(label, width), left, y, first ? color : _theme.Muted);
            first = false;
            rowTop += row.Height;
        }
    }

    /// <summary>A number's layout, kept: the same few hundred numbers are drawn on every pass while scrolling.</summary>
    private CanvasTextLayout NumberLayout(string label, float width)
    {
        if (!_numberLayouts.TryGetValue(label, out var layout))
        {
            if (_numberLayouts.Count > 2_000)
            {
                ClearNumberLayouts();
            }

            layout = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), label, _numberFormat, width, 0);
            _numberLayouts[label] = layout;
        }

        return layout;
    }

    private void ClearNumberLayouts()
    {
        foreach (var layout in _numberLayouts.Values)
        {
            layout.Dispose();
        }

        _numberLayouts.Clear();
    }

    /// <summary>
    /// Formatted mode keeps the markers hidden on every line; the caret line shows them as small chips at the top
    /// corners before and after the formatted text (as the RichEdit editor), so nothing shifts.
    /// </summary>
    private void DrawChips(CanvasDrawingSession ds, int line, CanvasTextLayout layout, LinePresentation presentation, float x, float y)
    {
        if (_mode != MarkdownEditorMode.Formatted || line != _doc.GetLineAtOffset(_caret))
        {
            return;
        }

        var text = _doc.GetLineText(line);
        var tokens = _doc.GetTokens(line);
        foreach (var t in tokens)
        {
            if (t.Kind is >= MarkdownTokenKind.Heading1 and <= MarkdownTokenKind.Heading6)
            {
                var marker = tokens.FirstOrDefault(s => s.Kind == MarkdownTokenKind.Syntax && s.Start == 0);
                if (marker.End < text.Length)
                {
                    Chip("H" + (t.Kind - MarkdownTokenKind.Heading1 + 1), marker.End, before: true);
                }

                continue;
            }

            if (t.Kind == MarkdownTokenKind.Quote)
            {
                var marker = tokens.LastOrDefault(s => s.Kind == MarkdownTokenKind.Syntax && s.End <= text.Length && text[s.Start] == '>');
                if (marker.Length > 0 && marker.End < text.Length)
                {
                    Chip(">", marker.End, before: true);
                }

                continue;
            }

            if (t.Kind is not (MarkdownTokenKind.Strong or MarkdownTokenKind.Emphasis or MarkdownTokenKind.InlineCode
                or MarkdownTokenKind.Strikethrough or MarkdownTokenKind.LinkText) || t.Length == 0)
            {
                continue;
            }

            var open = tokens.FirstOrDefault(s => s.Kind == MarkdownTokenKind.Syntax && s.End == t.Start && s.Length > 0);
            var close = tokens.FirstOrDefault(s => s.Kind == MarkdownTokenKind.Syntax && s.Start == t.End && s.Length > 0);
            if (open.Length == 0 || close.Length == 0)
            {
                continue;
            }

            Chip(text.Substring(open.Start, open.Length), t.Start, before: true);
            Chip(t.Kind == MarkdownTokenKind.LinkText ? "]↗" : text.Substring(close.Start, close.Length), t.End, before: false);
        }

        void Chip(string label, int sourceColumn, bool before)
        {
            EnsureGutterFormats();
            var shown = presentation.ToShown(sourceColumn);
            var at = layout.GetCaretPosition(shown, false, out var region);
            using var chip = new CanvasTextLayout(ds, label, _chipFormat, 200, 0);
            var w = (float)chip.LayoutBounds.Width + 4;
            var h = (float)chip.LayoutBounds.Height;
            var left = x + at.X + (before ? -w + 2 : -2);
            var top = y + (float)region.LayoutBounds.Y - h * 0.55f;
            ds.FillRoundedRectangle(left, top, w, h, 2, 2, WithAlpha(_theme.CodeBackground, 230));
            ds.DrawTextLayout(chip, left + 2, top, _theme.Muted);
        }
    }
}
