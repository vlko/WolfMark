using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Windows.Foundation;
using WolfMark.Core.Diagnostics;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor.Win2D;

/// <summary>Drawing: only the lines inside the invalidated regions, from the cached layouts.</summary>
public sealed partial class CanvasMarkdownEditor
{
    private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        _drawStamp++;
        var drawn = 0;
        foreach (var region in args.InvalidatedRegions)
        {
            try
            {
                using var ds = sender.CreateDrawingSession(region);
                UseExactPixelOrigin(ds);
                drawn += DrawRegion(ds, region);
            }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80070057))
            {
                // E_INVALIDARG: the canvas shrank between the invalidation and this callback; a new pass follows.
            }
        }

        TrimLayouts();
        EditorTrace.Current?.RecordSince(EditorPhase.CanvasDraw, started, drawn);
    }

    /// <summary>
    /// Pixel units with the DIP scale in the transform: the session origin is then a whole pixel, and redraws of
    /// the same text land on the same pixels (in DIP units the atlas slot offset is a float, and text shifted by a
    /// fraction of a pixel between redraws – seen and measured in WinUIRichEditor).
    /// </summary>
    private static void UseExactPixelOrigin(CanvasDrawingSession ds)
    {
        var scale = ds.Dpi / 96f;
        ds.Units = CanvasUnits.Pixels;
        ds.Transform = Matrix3x2.CreateScale(scale);
    }

    private int DrawRegion(CanvasDrawingSession ds, Rect region)
    {
        if (_doc.Length == 0 && PlaceholderText.Length > 0)
        {
            using var placeholder = new CanvasTextLayout(ds, PlaceholderText, _format, _layoutWidth, 0);
            ds.DrawTextLayout(placeholder, (float)PadLeft, (float)PadTop, _theme.Muted);
        }

        var (selStart, selEnd) = Selection;
        var caretLine = _doc.GetLineAtOffset(_caret);
        var selection = WithAlpha(_theme.Link, 0x40);
        var count = 0;
        for (var i = LineAtY(region.Top); i < _lines.Count; i++)
        {
            var top = LineTop(i);
            if (top > region.Bottom)
            {
                break;
            }

            var layout = EnsureLayout(i);
            var presentation = _lines[i].Presentation!;
            var x = (float)PadLeft;
            var y = (float)top;
            var lineStart = _doc.GetLineStart(i);
            var lineEnd = lineStart + presentation.SourceLength;

            foreach (var run in CodeRuns(presentation))
            {
                foreach (var r in layout.GetCharacterRegions(run.Start, run.Length))
                {
                    ds.FillRectangle(Offset(r.LayoutBounds, x, y), _theme.CodeBackground);
                }
            }

            if (selEnd > lineStart && selStart <= lineEnd && selEnd > selStart)
            {
                var from = presentation.ToShown(Math.Max(selStart, lineStart) - lineStart);
                var to = presentation.ToShown(Math.Min(selEnd, lineEnd) - lineStart);
                if (to > from)
                {
                    foreach (var r in layout.GetCharacterRegions(from, to - from))
                    {
                        ds.FillRectangle(Offset(r.LayoutBounds, x, y), selection);
                    }
                }

                if (selEnd > lineEnd)
                {
                    // The line break is selected too: a small block after the line's end.
                    var end = layout.GetCaretPosition(presentation.Text.Length, false, out var endRegion);
                    ds.FillRectangle(x + end.X, y + (float)endRegion.LayoutBounds.Y, 6, (float)endRegion.LayoutBounds.Height, selection);
                }
            }

            ds.DrawTextLayout(layout, x, y, _theme.Text);
            DrawComposition(ds, i, layout, presentation, x, y);

            if (i == caretLine && _hasFocus && _caretOn)
            {
                var caret = CaretRect(_caret);
                ds.FillRectangle((float)caret.X, (float)caret.Y, 1.5f, (float)caret.Height, _theme.Text);
            }

            count++;
        }

        return count;
    }

    private static Rect Offset(Rect r, float x, float y) => new(r.X + x, r.Y + y, r.Width, r.Height);

    /// <summary>The caret at a source offset, in canvas coordinates (x, top, height of its row).</summary>
    private Rect CaretRect(int offset)
    {
        var line = _doc.GetLineAtOffset(offset);
        var layout = EnsureLayout(line);
        var presentation = _lines[line].Presentation!;
        var shown = presentation.ToShown(offset - _doc.GetLineStart(line));
        var point = layout.GetCaretPosition(shown, false, out var region);
        var height = region.LayoutBounds.Height > 0 ? region.LayoutBounds.Height : _emptyLineHeight;
        return new Rect(PadLeft + point.X, LineTop(line) + region.LayoutBounds.Y, 1.5, height);
    }

    /// <summary>The source offset at a canvas point.</summary>
    private int OffsetAt(Point point)
    {
        var line = LineAtY(point.Y);
        var layout = EnsureLayout(line);
        var presentation = _lines[line].Presentation!;
        var x = (float)(point.X - PadLeft);
        var y = (float)Math.Clamp(point.Y - LineTop(line), 0, Math.Max(0, layout.LayoutBounds.Height - 1));
        layout.HitTest(x, y, out var region, out var trailing);
        var shown = presentation.Text.Length == 0 ? 0 : region.CharacterIndex + (trailing ? region.CharacterCount : 0);
        return _doc.GetLineStart(line) + presentation.ToSource(shown);
    }

    private void EnsureCaretVisible()
    {
        if (_scroll.ViewportHeight <= 0)
        {
            return;
        }

        var caret = CaretRect(_caret);
        var top = _scroll.VerticalOffset;
        var bottom = top + _scroll.ViewportHeight;
        if (caret.Y < top + 4)
        {
            _scroll.ChangeView(null, Math.Max(0, caret.Y - 24), null, disableAnimation: true);
        }
        else if (caret.Bottom > bottom - 4)
        {
            _scroll.ChangeView(null, caret.Bottom - _scroll.ViewportHeight + 24, null, disableAnimation: true);
        }
    }
}
