using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace WolfMark.Desktop.Editor;

/// <summary>
/// Line numbers as on Windows (src/WolfMark.Editor/MarkdownEditor.LineNumbers.cs): a faint band, one number per
/// Markdown line on its first row (the caret line darker), ↪ on the rows a long line wraps onto; an image line
/// has its number at the top of the picture.
/// </summary>
internal sealed class LineNumberMargin(MarkdownTextEditor owner) : AbstractMargin
{
    private const string WrapGlyph = "↪";

    // Clear strip between the band and the text: the marker chips before a line's first character sit there.
    private const double ChipGap = 12;

    private double NumberSize => Math.Round(owner.MarkdownTheme.FontSize * 0.78);

    private Typeface Typeface => new(owner.MarkdownTheme.MonoFont);

    protected override Size MeasureOverride(Size availableSize)
    {
        var lines = TextView?.Document?.LineCount ?? 1;
        var digits = Math.Max(3, lines.ToString(CultureInfo.InvariantCulture).Length);
        return new Size(Math.Ceiling(digits * NumberSize * 0.62) + 18 + ChipGap, 0);
    }

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        }

        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        }

        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var theme = owner.MarkdownTheme;
        var tint = theme.Text;
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(12, tint.R, tint.G, tint.B)), new Rect(0, 0, Bounds.Width - 4 - ChipGap, Bounds.Height));
        if (TextView is not { VisualLinesValid: true } view)
        {
            return;
        }

        var muted = new SolidColorBrush(theme.Muted);
        var active = new SolidColorBrush(theme.Text);
        var caretLine = owner.CaretLine;
        var rowHeight = theme.FontSize * 1.4;
        var right = Bounds.Width - 12 - ChipGap;
        foreach (var visualLine in view.VisualLines)
        {
            var number = visualLine.FirstDocumentLine.LineNumber;
            var first = true;
            foreach (var row in visualLine.TextLines)
            {
                var top = visualLine.GetTextLineVisualYPosition(row, VisualYPosition.TextTop) - view.VerticalOffset;
                // A picture row is much taller than text: its number sits where the first text row would.
                var baseline = row.Height > rowHeight * 2.2 ? top + (rowHeight * 0.78) : top + row.Baseline;
                var label = first ? number.ToString(CultureInfo.InvariantCulture) : WrapGlyph;
                var size = first ? NumberSize : NumberSize * 0.85;
                var brush = first && number == caretLine ? active : muted;
                var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface, size, brush);
                context.DrawText(text, new Point(right - text.Width, baseline - text.Baseline));
                first = false;
            }
        }
    }
}
