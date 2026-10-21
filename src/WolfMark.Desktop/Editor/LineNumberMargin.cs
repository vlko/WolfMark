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
            if (visualLine.LastDocumentLine.LineNumber > number && owner.GridOf(number - 1) is { } grid && visualLine.TextLines.Count > 0)
            {
                DrawTableNumbers(context, visualLine, grid, view.VerticalOffset, right, muted);
                continue;
            }

            var first = true;
            foreach (var row in visualLine.TextLines)
            {
                var top = visualLine.GetTextLineVisualYPosition(row, VisualYPosition.TextTop) - view.VerticalOffset;
                // A picture row is much taller than text: its number sits where the first text row would.
                var baseline = row.Height > rowHeight * 2.2 ? top + (rowHeight * 0.78) : top + row.Baseline;
                if (!first)
                {
                    DrawWrapMark(context, right, baseline, NumberSize * 0.85, muted);
                    continue;
                }

                var brush = number == caretLine ? active : muted;
                var text = new FormattedText(number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface, NumberSize, brush);
                context.DrawText(text, new Point(right - text.Width, baseline - text.Baseline));
                first = false;
            }
        }
    }

    /// <summary>
    /// ↪ on a wrapped row, drawn as a shape: as text the glyph came from a colour emoji font (a blue ↪️ box) where the
    /// monospaced font lacks it; the Windows editor draws it in the muted text colour.
    /// </summary>
    private static void DrawWrapMark(DrawingContext context, double right, double baseline, double size, IBrush brush)
    {
        var s = size / 10;
        var left = right - (9 * s);
        var top = baseline - (8 * s);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            // A hook curling up on the left, the shaft to the right, the arrow head.
            g.BeginFigure(new Point(left + (3 * s), top + s), false);
            g.ArcTo(new Point(left + (3 * s), top + (6 * s)), new Size(2.5 * s, 2.5 * s), 0, false, SweepDirection.CounterClockwise);
            g.LineTo(new Point(left + (9 * s), top + (6 * s)));
            g.EndFigure(false);
            g.BeginFigure(new Point(left + (6.5 * s), top + (3.5 * s)), false);
            g.LineTo(new Point(left + (9 * s), top + (6 * s)));
            g.LineTo(new Point(left + (6.5 * s), top + (8.5 * s)));
            g.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(brush, Math.Max(1, s), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }

    /// <summary>A table shown as a grid: the first line's number by the header row, the last line's by the last row, dots between.</summary>
    private void DrawTableNumbers(DrawingContext context, VisualLine visualLine, Avalonia.Controls.Grid grid, double offset, double right, IBrush brush)
    {
        var theme = owner.MarkdownTheme;
        var rows = grid.RowDefinitions;
        if (rows.Count == 0)
        {
            return;
        }

        var top = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.LineTop) - offset + TableElementGenerator.GridInset;
        var cellBaseline = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(theme.Font), theme.FontSize, brush).Baseline;
        void Number(int number, double baseline)
        {
            var text = new FormattedText(number.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface, NumberSize, brush);
            context.DrawText(text, new Point(right - text.Width, baseline - text.Baseline));
        }

        var first = top + TableElementGenerator.PadY + cellBaseline;
        Number(visualLine.FirstDocumentLine.LineNumber, first);
        if (rows.Count < 2)
        {
            return; // a header alone: one row, one number
        }

        var lastTop = top + rows.Take(rows.Count - 1).Sum(r => r.ActualHeight);
        var last = lastTop + TableElementGenerator.PadY + cellBaseline;
        Number(visualLine.LastDocumentLine.LineNumber, last);
        var x = right - (NumberSize * 0.31);
        for (var y = first + 6; y < last - NumberSize - 1; y += 5)
        {
            context.DrawEllipse(brush, null, new Point(x, y), 1, 1);
        }
    }
}
