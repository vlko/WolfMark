using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor;

/// <summary>
/// Line numbers in the left padding: one per line of the Markdown text (a wrapped line has one number on its
/// first row, an image line one number at the top of its picture). Like the previews they sit on a layer in
/// document coordinates that only moves on scroll; only the lines around the viewport are measured, again
/// once the view leaves the measured band.
/// </summary>
public sealed partial class MarkdownEditor
{
    private const int MinNumberDigits = 3;
    private const int MaxWrapRows = 200;
    private const string WrapGlyph = "\u21AA"; // ↪ on the rows a long line wraps onto

    private readonly Canvas _lineNumbers = new() { IsHitTestVisible = false };
    private readonly Canvas _lineNumberLayer = new() { RenderTransform = new TranslateTransform() };
    private readonly List<(Grid Cell, TextBlock Text)> _numberPool = [];

    // A faint band behind the numbers: they read as a margin, not as part of the note.
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _numberBand = new() { Visibility = Visibility.Collapsed };
    private bool _showLineNumbers;
    private bool _numbersPending;
    private int _numberDigits = MinNumberDigits;
    private Point _numberOrigin;

    // Document band whose lines are measured; scrolling inside it needs no new measuring.
    private double _numberBandTop;
    private double _numberBandBottom = -1;
    private SolidColorBrush? _numberBrush;
    private SolidColorBrush? _activeNumberBrush;

    /// <summary>Shows a number before each line.</summary>
    public bool ShowLineNumbers
    {
        get => _showLineNumbers;
        set
        {
            if (_showLineNumbers == value)
            {
                return;
            }

            _showLineNumbers = value;
            UpdatePadding();
            ScheduleLineNumbers();
        }
    }

    private void InitLineNumbers()
    {
        _lineNumbers.Children.Add(_numberBand);
        _lineNumbers.Children.Add(_lineNumberLayer);
    }

    private double NumberFontSize => Math.Round(_theme.FontSize * 0.78);

    /// <summary>Room the numbers take left of the text (0 when hidden).</summary>
    private double LineNumberGutter => _showLineNumbers ? Math.Ceiling(_numberDigits * NumberFontSize * 0.62) + 14 : 0;

    private void ScheduleLineNumbers()
    {
        if (_numbersPending)
        {
            return;
        }

        _numbersPending = _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _numbersPending = false;
            LayoutLineNumbers();
        });
    }

    /// <summary>On scroll: move the layer; measure again only outside the measured band.</summary>
    private void ScrollLineNumbers()
    {
        if (!_showLineNumbers || _scrollViewer is not { } sv)
        {
            return;
        }

        var transform = (TranslateTransform)_lineNumberLayer.RenderTransform;
        transform.Y = _numberOrigin.Y - sv.VerticalOffset;
        UpdateNumberClip(sv);
        if (sv.VerticalOffset < _numberBandTop || sv.VerticalOffset + sv.ViewportHeight > _numberBandBottom)
        {
            ScheduleLineNumbers();
        }
    }

    private void LayoutLineNumbers()
    {
        if (!_showLineNumbers || _scrollViewer is not { } sv || sv.ViewportHeight <= 0)
        {
            ClearLineNumbers();
            return;
        }

        var starts = LineStarts(_text);
        var digits = Math.Max(MinNumberDigits, starts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        if (digits != _numberDigits)
        {
            _numberDigits = digits;
            UpdatePadding(); // wider gutter; the text moves, so measure after RichEdit's layout
            ScheduleLineNumbers();
            return;
        }

        _numberOrigin = ViewportOrigin();
        var toDocument = ScrollShift() + sv.VerticalOffset;
        var margin = sv.ViewportHeight / 2;
        var bandTop = Math.Max(0, sv.VerticalOffset - margin);
        var bandBottom = sv.VerticalOffset + sv.ViewportHeight + margin;

        // Line tops grow with the line index: binary search for the first line of the band.
        var lo = 0;
        var hi = starts.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (LineRow(mid, starts).Top + toDocument <= bandTop)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        var size = NumberFontSize;
        var cellHeight = Math.Ceiling(size * 1.6);
        var width = LineNumberGutter - 10;
        _numberBrush ??= new SolidColorBrush(_theme.Muted);
        _numberBrush.Color = _theme.Muted;
        _activeNumberBrush ??= new SolidColorBrush(_theme.Text);
        _activeNumberBrush.Color = _theme.Text;
        var font = new FontFamily(_theme.MonoFontFamily);

        var used = 0;
        for (var line = lo; line < starts.Count; line++)
        {
            var (top, baseline) = LineRow(line, starts);
            if (top + toDocument > bandBottom)
            {
                break;
            }

            Place(PooledNumber(used++), (line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), size, line, baseline);
            foreach (var (rowTop, rowBaseline) in ContinuationRows(line, starts, top))
            {
                if (rowTop + toDocument > bandBottom)
                {
                    break;
                }

                Place(PooledNumber(used++), WrapGlyph, size * 0.85, -1, rowBaseline);
            }
        }

        void Place((Grid Cell, TextBlock Text) number, string label, double fontSize, int line, double baseline)
        {
            var (cell, text) = number;
            text.Text = label;
            text.FontSize = fontSize;
            text.FontFamily = font;
            text.Foreground = line >= 0 && line == _activeLine ? _activeNumberBrush : _numberBrush;
            text.Tag = line; // -1: a wrap mark, never highlighted
            cell.Width = width;
            cell.Height = cellHeight;
            Canvas.SetLeft(cell, -LineNumberGutter);
            Canvas.SetTop(cell, baseline + toDocument - cellHeight);
        }

        while (_numberPool.Count > used)
        {
            _lineNumberLayer.Children.Remove(_numberPool[^1].Cell);
            _numberPool.RemoveAt(_numberPool.Count - 1);
        }

        _numberBandTop = sv.VerticalOffset - margin / 2;
        _numberBandBottom = sv.VerticalOffset + sv.ViewportHeight + margin / 2;
        var transform = (TranslateTransform)_lineNumberLayer.RenderTransform;
        transform.X = _numberOrigin.X;
        transform.Y = _numberOrigin.Y - sv.VerticalOffset;
        UpdateNumberClip(sv);
        UpdateNumberBand(sv);
    }

    private void UpdateNumberBand(ScrollViewer sv)
    {
        var tint = _theme.Text;
        tint.A = 12;
        if (_numberBand.Fill is not SolidColorBrush brush || brush.Color != tint)
        {
            _numberBand.Fill = new SolidColorBrush(tint);
        }

        _numberBand.Width = LineNumberGutter;
        _numberBand.Height = Math.Max(0, sv.ViewportHeight);
        Canvas.SetLeft(_numberBand, _numberOrigin.X - LineNumberGutter - 4);
        Canvas.SetTop(_numberBand, _numberOrigin.Y);
        _numberBand.Visibility = Visibility.Visible;
    }

    private void ClearLineNumbers()
    {
        _lineNumberLayer.Children.Clear();
        _numberPool.Clear();
        _numberBandBottom = -1;
        _numberBand.Visibility = Visibility.Collapsed;
    }

    private void UpdateNumberClip(ScrollViewer sv) =>
        _lineNumbers.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, _numberOrigin.Y, Math.Max(0, ActualWidth), Math.Max(0, sv.ViewportHeight)),
        };

    /// <summary>The caret line's number is drawn in the text colour.</summary>
    private void HighlightActiveLineNumber()
    {
        foreach (var (_, text) in _numberPool)
        {
            var brush = text.Tag is int line && line == _activeLine ? _activeNumberBrush : _numberBrush;
            if (brush is not null && !ReferenceEquals(text.Foreground, brush))
            {
                text.Foreground = brush;
            }
        }
    }

    private (Grid Cell, TextBlock Text) PooledNumber(int index)
    {
        if (index < _numberPool.Count)
        {
            return _numberPool[index];
        }

        // The number's bottom is its baseline, so it sits on the baseline of the line it belongs to.
        var text = new TextBlock
        {
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            TextLineBounds = TextLineBounds.TrimToBaseline,
        };
        var cell = new Grid();
        cell.Children.Add(text);
        _numberPool.Add((cell, text));
        _lineNumberLayer.Children.Add(cell);
        return (cell, text);
    }

    /// <summary>
    /// Top and baseline of a line's first row in GetRect coordinates, measured on its first visible character
    /// (formatted mode hides leading syntax such as "# ", and hidden text has no usable position).
    /// </summary>
    private (double Top, double Baseline) LineRow(int line, List<int> starts)
    {
        var start = starts[line];
        var end = line + 1 < starts.Count ? starts[line + 1] - 1 : _text.Length;
        var lineText = _text[start..end];
        var doc = _box.TextDocument;
        if (IsCollapsedImageLine(lineText))
        {
            // The picture starts on the line's tiny row: the number reads like the first text row beside it.
            doc.GetRange(start, start + 1).GetRect(PointOptions.ClientCoordinates, out var image, out _);
            return (image.Top, image.Top + TextRowHeight * 0.78);
        }

        var at = start + FirstVisible(line, lineText);
        doc.GetRange(at, at + 1).GetRect(PointOptions.ClientCoordinates, out var rect, out _);
        // The character's cell: its descent is about a fifth of the height. A paragraph mark's rectangle may
        // include an image's reserved SpaceAfter, so it is capped at one text row.
        var height = rect.Height <= 0 ? TextRowHeight : at == end ? Math.Min(rect.Height, TextRowHeight) : rect.Height;
        return (rect.Top, rect.Top + height * 0.78);
    }

    /// <summary>
    /// Rows after the first of a line that wraps (top and baseline in GetRect coordinates), walked with
    /// RichEdit's display-line moves. Measured on the first visible character of each row.
    /// </summary>
    private IEnumerable<(double Top, double Baseline)> ContinuationRows(int line, List<int> starts, double firstTop)
    {
        var start = starts[line];
        var end = line + 1 < starts.Count ? starts[line + 1] - 1 : _text.Length;
        var lineText = _text[start..end];
        if (lineText.Length == 0 || IsCollapsedImageLine(lineText))
        {
            yield break;
        }

        var doc = _box.TextDocument;
        doc.GetRange(end, end + 1).GetRect(PointOptions.ClientCoordinates, out var mark, out _);
        if (mark.Top <= firstTop + TextRowHeight / 2)
        {
            yield break; // one row
        }

        var range = doc.GetRange(start + FirstVisible(line, lineText), start + FirstVisible(line, lineText));
        var lastTop = firstTop;
        for (var i = 0; i < MaxWrapRows; i++)
        {
            if (range.Move(TextRangeUnit.Line, 1) == 0 || range.StartPosition >= end)
            {
                yield break; // past the line (its paragraph mark ends the last row)
            }

            var at = start + FirstVisible(line, lineText, range.StartPosition - start);
            doc.GetRange(at, at + 1).GetRect(PointOptions.ClientCoordinates, out var rect, out _);
            if (rect.Top <= lastTop + 1)
            {
                continue;
            }

            lastTop = rect.Top;
            var height = rect.Height <= 0 ? TextRowHeight : at == end ? Math.Min(rect.Height, TextRowHeight) : rect.Height;
            yield return (rect.Top, rect.Top + height * 0.78);
        }
    }

    private int FirstVisible(int line, string lineText, int from = 0)
    {
        if (_mode != MarkdownEditorMode.Formatted
            || !_lineTokens.TryGetValue(line, out var cached)
            || !string.Equals(cached.Text, lineText, StringComparison.Ordinal))
        {
            return from;
        }

        var pos = from;
        while (pos < lineText.Length && IsHidden(cached.Tokens, pos))
        {
            pos++;
        }

        return pos; // == length: the paragraph mark
    }

    private static bool IsHidden(MarkdownToken[] tokens, int pos)
    {
        foreach (var t in tokens)
        {
            if (t.Kind is MarkdownTokenKind.Syntax or MarkdownTokenKind.LinkUrl && (t.Start <= pos && pos < t.End))
            {
                return true;
            }
        }

        return false;
    }

    private static List<int> LineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (var i = text.IndexOf('\n'); i >= 0; i = text.IndexOf('\n', i + 1))
        {
            starts.Add(i + 1);
        }

        return starts;
    }
}
