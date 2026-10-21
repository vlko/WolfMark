using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WolfMark.App.Ui;

/// <summary>
/// Toolbar that wraps whole groups instead of hiding buttons on narrow windows. Children are, in order:
/// the left group, then groups anchored to the right edge. When the row is too narrow the last group moves
/// to its own row (right-aligned), then the next one – in the worst case every group gets its own row.
/// </summary>
internal sealed partial class ToolbarPanel : Panel
{
    private readonly List<List<int>> _rows = [];

    public double Spacing { get; set; } = 12;

    public double RowSpacing { get; set; } = 6;

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Children.Count;
        var widths = new double[count];
        for (var i = 0; i < count; i++)
        {
            Children[i].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widths[i] = Children[i].DesiredSize.Width;
        }

        var available = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;

        // Keep as many groups as possible in the first row; the rest each get a row, last group lowest.
        _rows.Clear();
        var inFirstRow = count;
        while (inFirstRow > 1 && RowWidth(widths, 0, inFirstRow) > available)
        {
            inFirstRow--;
        }

        _rows.Add([.. Enumerable.Range(0, inFirstRow)]);
        for (var i = inFirstRow; i < count; i++)
        {
            _rows.Add([i]);
        }

        double height = 0, width = 0;
        foreach (var row in _rows)
        {
            height += row.Max(i => Children[i].DesiredSize.Height);
            width = Math.Max(width, RowWidth(widths, row[0], row.Count));
        }

        height += RowSpacing * (_rows.Count - 1);
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var y = 0.0;
        foreach (var row in _rows)
        {
            var rowHeight = row.Max(i => Children[i].DesiredSize.Height);

            // The left group (index 0) sits at the left edge; every other group is packed from the right.
            var right = finalSize.Width;
            for (var k = row.Count - 1; k >= 0; k--)
            {
                var child = Children[row[k]];
                var size = child.DesiredSize;
                var x = row[k] == 0 ? 0 : right - size.Width;
                child.Arrange(new Rect(x, y + ((rowHeight - size.Height) / 2), size.Width, size.Height));
                right = size.Width > 0 ? x - Spacing : x; // a collapsed group leaves no gap
            }

            y += rowHeight + RowSpacing;
        }

        return finalSize;
    }

    private double RowWidth(double[] widths, int start, int count)
    {
        var sum = 0.0;
        var shown = 0;
        for (var i = start; i < start + count; i++)
        {
            sum += widths[i];
            shown += widths[i] > 0 ? 1 : 0;
        }

        return sum + (Spacing * Math.Max(0, shown - 1));
    }
}
