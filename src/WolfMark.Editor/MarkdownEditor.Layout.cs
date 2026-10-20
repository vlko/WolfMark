using Microsoft.UI.Dispatching;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Windows.UI;
using WolfMark.Core.Diagnostics;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor;

/// <summary>
/// Line layouts. Every line's height is known (measured on open, on a mode or width change, and for the lines an
/// edit touches), so the scroll extent is exact; a <see cref="CanvasTextLayout"/> is kept only for lines that were
/// drawn recently.
/// </summary>
public sealed partial class MarkdownEditor
{
    private const int MaxLiveLayouts = 400;

    private readonly List<LineView> _lines = [];
    private double[] _tops = [];
    private bool _topsDirty = true;
    private double _totalHeight;
    private float _layoutWidth = 600;
    private CanvasTextFormat? _format;
    private double _emptyLineHeight = 20;

    private double PadLeft => (AlignLeft ? 18 : 24) + Gutter;

    private const double PadTop = 12;
    private const double PadRight = 24;
    private const double PadBottom = 48;

    private sealed class LineView
    {
        public double Height = -1; // of the text; the picture below comes on top (LineHeight)
        public bool ImageChecked;
        public string? ImagePath;
        public bool ImageOnly;
        public bool Exact; // measured; otherwise estimated until the background pass delivers
        public CanvasTextLayout? Layout;
        public LinePresentation? Presentation;
        public long LastUsed;

        public void Drop()
        {
            Layout?.Dispose();
            Layout = null;
            Presentation = null;
        }
    }

    private long _drawStamp;

    private void ResetLines()
    {
        ClearLayouts();
        _lines.Clear();
        for (var i = 0; i < _doc.LineCount; i++)
        {
            _lines.Add(new LineView());
        }

        _topsDirty = true;
    }

    private void ClearLayouts()
    {
        foreach (var line in _lines)
        {
            line.Drop();
        }
    }

    /// <summary>Keeps <see cref="_lines"/> in step with the model after an edit and lays out the retokenized lines.</summary>
    private void SpliceLines(MarkdownUpdate update)
    {
        for (var i = update.FirstLine; i < update.FirstLine + update.RemovedLines; i++)
        {
            _lines[i].Drop();
        }

        _lines.RemoveRange(update.FirstLine, update.RemovedLines);
        var inserted = new LineView[update.InsertedLines];
        for (var k = 0; k < inserted.Length; k++)
        {
            inserted[k] = new LineView();
        }

        _lines.InsertRange(update.FirstLine, inserted);

        // Retokenized lines (the edit plus lines whose block state changed, e.g. after opening a code fence).
        for (var i = update.FirstLine; i < Math.Min(_lines.Count, update.FirstLine + update.LineCount); i++)
        {
            _lines[i].Drop();
            Measure(i);
        }

        _topsDirty = true;
    }

    private void RecreateFormat()
    {
        _format?.Dispose();
        _format = new CanvasTextFormat
        {
            FontFamily = MarkdownTheme.ToFaceName(_theme.FontFamily),
            FontSize = (float)_theme.FontSize,
            WordWrapping = CanvasWordWrapping.Wrap,
        };
        using var probe = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), "Xg", _format, 1000, 0);
        _emptyLineHeight = probe.LayoutBounds.Height;
    }

    /// <summary>
    /// Everything laid out again: open, mode switch, theme or width change. The lines around the view and the
    /// caret are measured at once; the others get an estimate and are measured in parallel off the UI thread
    /// (5,000 lines on the UI thread took ~330 ms). The first visible line keeps its place on screen.
    /// </summary>
    /// <param name="keepView">False when a new note replaced the text: the old scroll position means nothing.</param>
    private void RelayoutAll(bool keepView = true)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var anchor = keepView ? ViewAnchor() : null;
        ClearLayouts();
        if (_format is null)
        {
            RecreateFormat();
        }

        var generation = ++_measureGeneration;
        var caretLine = _doc.GetLineAtOffset(_caret);
        var viewLine = anchor?.Line ?? caretLine;
        var pending = new List<int>();
        for (var i = 0; i < _lines.Count; i++)
        {
            var view = _lines[i];
            if (Math.Abs(i - caretLine) <= ExactAround || Math.Abs(i - viewLine) <= ExactAround)
            {
                Measure(i);
            }
            else
            {
                view.Height = Estimate(i);
                view.Exact = false;
                pending.Add(i);
            }
        }

        _topsDirty = true;
        UpdateCanvasSize();
        RestoreViewAnchor(anchor);
        _canvas.Invalidate();
        EditorTrace.Current?.RecordSince(EditorPhase.Measure, started, _lines.Count - pending.Count, () => $"view, {_lines.Count} lines, {_mode}, width {_layoutWidth:0}");
        if (pending.Count > 0)
        {
            MeasureInBackground(generation, pending);
        }
    }

    private const int ExactAround = 80;

    private int _measureGeneration;

    /// <summary>Height guess from the text length, until the line is measured.</summary>
    private double Estimate(int line)
    {
        var text = _doc.GetLineText(line);
        var scale = text.StartsWith('#') ? 1.4 : 1.0;
        var perRow = Math.Max(10, _layoutWidth / (_theme.FontSize * 0.5 * scale));
        return _emptyLineHeight * scale * Math.Max(1, Math.Ceiling(text.Length / perRow));
    }

    /// <summary>
    /// Measures the given lines on the thread pool (DirectWrite layouts are thread-safe; colours are skipped) and
    /// hands the heights back to the UI thread. Lines are matched by their text instance: an edit meanwhile
    /// measured its own lines already, and a newer relayout (generation) drops the result.
    /// </summary>
    private void MeasureInBackground(int generation, List<int> lines)
    {
        var texts = new string[lines.Count];
        var tokens = new MarkdownToken[lines.Count][];
        for (var k = 0; k < lines.Count; k++)
        {
            texts[k] = _doc.GetLineText(lines[k]);
            tokens[k] = _doc.GetTokenArray(lines[k]); // never changed after creation: safe to read off the UI thread
        }

        var theme = _theme;
        var width = _layoutWidth;
        var hide = _mode == MarkdownEditorMode.Formatted;
        var empty = _emptyLineHeight;
        var family = MarkdownTheme.ToFaceName(theme.FontFamily);
        _ = Task.Run(() =>
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var heights = new double[texts.Length];
            var device = CanvasDevice.GetSharedDevice();
            Parallel.For(
                0,
                texts.Length,
                () => new CanvasTextFormat { FontFamily = family, FontSize = (float)theme.FontSize, WordWrapping = CanvasWordWrapping.Wrap },
                (k, _, format) =>
                {
                    var presentation = LinePresentation.Create(texts[k], tokens[k], hide);
                    using var layout = new CanvasTextLayout(device, presentation.Text, format, width, 0);
                    ApplyStyles(layout, presentation, theme, measureOnly: true);
                    heights[k] = Math.Max(layout.LayoutBounds.Height, presentation.Text.Length == 0 ? empty : 0);
                    return format;
                },
                format => format.Dispose());
            EditorTrace.Current?.RecordSince(EditorPhase.Measure, started, texts.Length, () => $"background, {texts.Length} lines");
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => ApplyMeasured(generation, texts, heights));
        });
    }

    private void ApplyMeasured(int generation, string[] texts, double[] heights)
    {
        if (generation != _measureGeneration)
        {
            return; // laid out again meanwhile (mode, width, theme, another note)
        }

        var measured = new Dictionary<string, double>(texts.Length, ReferenceEqualityComparer.Instance);
        for (var k = 0; k < texts.Length; k++)
        {
            measured.TryAdd(texts[k], heights[k]);
        }

        var anchor = ViewAnchor();
        for (var i = 0; i < _lines.Count; i++)
        {
            var view = _lines[i];
            if (!view.Exact && measured.TryGetValue(_doc.GetLineText(i), out var height))
            {
                view.Height = height;
                view.Exact = true;
            }
        }

        _topsDirty = true;
        UpdateCanvasSize();
        RestoreViewAnchor(anchor);
        _canvas.Invalidate();
    }

    /// <summary>The line at the top of the view and how far its top lies above the viewport.</summary>
    private (int Line, double Delta)? ViewAnchor()
    {
        if (_lines.Count == 0 || _scroll.VerticalOffset <= 0)
        {
            return null;
        }

        var line = LineAtY(_scroll.VerticalOffset);
        return (line, LineTop(line) - _scroll.VerticalOffset);
    }

    /// <summary>Scrolls so the anchor line sits where it was – now, and again once the new extent is laid out.</summary>
    private void RestoreViewAnchor((int Line, double Delta)? anchor)
    {
        if (anchor is not { } a || a.Line >= _lines.Count)
        {
            return;
        }

        var y = Math.Max(0, LineTop(a.Line) - a.Delta);
        _scroll.ChangeView(null, y, null, disableAnimation: true);
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (a.Line < _lines.Count)
            {
                _scroll.ChangeView(null, Math.Max(0, LineTop(a.Line) - a.Delta), null, disableAnimation: true);
            }
        });
    }

    private void Measure(int line)
    {
        var view = _lines[line];
        var layout = EnsureLayout(line);
        view.Height = Math.Max(layout.LayoutBounds.Height, view.Presentation!.Text.Length == 0 ? _emptyLineHeight : 0);
        view.Exact = true;
    }

    private CanvasTextLayout EnsureLayout(int line)
    {
        var view = _lines[line];
        view.LastUsed = _drawStamp;
        if (view.Layout is { } existing)
        {
            return existing;
        }

        if (_format is null)
        {
            RecreateFormat();
        }

        var presentation = LinePresentation.Create(_doc.GetLineText(line), _doc.GetTokens(line), _mode == MarkdownEditorMode.Formatted);
        var layout = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), presentation.Text, _format, _layoutWidth, 0);
        ApplyStyles(layout, presentation, _theme);
        view.Presentation = presentation;
        view.Layout = layout;
        return layout;
    }

    /// <summary>Token formats on a line's layout, in lexer order (later tokens refine earlier ones).</summary>
    /// <param name="measureOnly">Off the UI thread, for heights: colours (and the app's tag colour lookup) are left out.</param>
    private static void ApplyStyles(CanvasTextLayout layout, LinePresentation presentation, MarkdownTheme theme, bool measureOnly = false)
    {
        var size = (float)theme.FontSize;
        var mono = MarkdownTheme.ToFaceName(theme.MonoFontFamily);

        void SetColor(int start, int count, Color color)
        {
            if (!measureOnly)
            {
                layout.SetColor(start, count, color);
            }
        }

        foreach (var t in presentation.Tokens)
        {
            var (s, n) = (t.Start, t.Length);
            switch (t.Kind)
            {
                case MarkdownTokenKind.Heading1 or MarkdownTokenKind.Heading2 or MarkdownTokenKind.Heading3
                    or MarkdownTokenKind.Heading4 or MarkdownTokenKind.Heading5 or MarkdownTokenKind.Heading6:
                    layout.SetFontSize(s, n, size * HeadingScale(t.Kind));
                    layout.SetFontWeight(s, n, FontWeights.SemiBold);
                    SetColor(s, n, theme.Heading);
                    break;
                case MarkdownTokenKind.Syntax or MarkdownTokenKind.LinkUrl or MarkdownTokenKind.HorizontalRule:
                    SetColor(s, n, theme.Muted);
                    break;
                case MarkdownTokenKind.Strong:
                    layout.SetFontWeight(s, n, FontWeights.Bold);
                    break;
                case MarkdownTokenKind.Emphasis:
                    layout.SetFontStyle(s, n, Windows.UI.Text.FontStyle.Italic);
                    break;
                case MarkdownTokenKind.Strikethrough:
                    layout.SetStrikethrough(s, n, true);
                    break;
                case MarkdownTokenKind.InlineCode or MarkdownTokenKind.CodeBlock:
                    layout.SetFontFamily(s, n, mono);
                    SetColor(s, n, theme.Code);
                    break;
                case MarkdownTokenKind.CodeFence or MarkdownTokenKind.FrontMatter:
                    layout.SetFontFamily(s, n, mono);
                    SetColor(s, n, theme.Muted);
                    layout.SetFontSize(s, n, size * 0.9f);
                    break;
                case MarkdownTokenKind.LinkText or MarkdownTokenKind.WikiLink or MarkdownTokenKind.AutoLink:
                    SetColor(s, n, theme.Link);
                    layout.SetUnderline(s, n, true);
                    break;
                case MarkdownTokenKind.Image:
                    SetColor(s, n, theme.Link);
                    break;
                case MarkdownTokenKind.ListMarker:
                    SetColor(s, n, theme.Muted);
                    layout.SetFontWeight(s, n, FontWeights.Bold);
                    break;
                case MarkdownTokenKind.TaskOpen or MarkdownTokenKind.TaskDone:
                    SetColor(s, n, t.Kind == MarkdownTokenKind.TaskOpen ? theme.TaskOpen : theme.TaskDone);
                    layout.SetFontWeight(s, n, FontWeights.Bold);
                    layout.SetFontFamily(s, n, mono);
                    break;
                case MarkdownTokenKind.TaskDoneText:
                    SetColor(s, n, theme.TaskDone);
                    layout.SetStrikethrough(s, n, true);
                    break;
                case MarkdownTokenKind.Quote:
                    SetColor(s, n, theme.Quote);
                    layout.SetFontStyle(s, n, Windows.UI.Text.FontStyle.Italic);
                    break;
                case MarkdownTokenKind.Hashtag:
                    if (!measureOnly)
                    {
                        layout.SetColor(s, n, theme.TagColor(presentation.Text.Substring(s + 1, n - 1)));
                    }

                    layout.SetFontWeight(s, n, FontWeights.Bold);
                    break;
            }
        }
    }

    private static float HeadingScale(MarkdownTokenKind kind) => kind switch
    {
        MarkdownTokenKind.Heading1 => 1.6f,
        MarkdownTokenKind.Heading2 => 1.35f,
        MarkdownTokenKind.Heading3 => 1.18f,
        _ => 1.05f,
    };

    /// <summary>Code backgrounds of a line, in shown columns.</summary>
    private static IEnumerable<MarkdownToken> CodeRuns(LinePresentation presentation) =>
        presentation.Tokens.Where(t => t.Kind is MarkdownTokenKind.InlineCode or MarkdownTokenKind.CodeBlock);

    private void EnsureTops()
    {
        if (!_topsDirty)
        {
            return;
        }

        if (_tops.Length < _lines.Count + 1)
        {
            _tops = new double[Math.Max(_lines.Count + 1, _tops.Length * 2)];
        }

        double y = 0;
        for (var i = 0; i < _lines.Count; i++)
        {
            _tops[i] = y;
            y += LineHeight(i);
        }

        _tops[_lines.Count] = y;
        _totalHeight = y;
        _topsDirty = false;
    }

    /// <summary>Top of a line in canvas coordinates.</summary>
    private double LineTop(int line)
    {
        EnsureTops();
        return PadTop + _tops[line];
    }

    /// <summary>The line at canvas y (clamped to the first and last line).</summary>
    private int LineAtY(double y)
    {
        EnsureTops();
        y -= PadTop;
        var lo = 0;
        var hi = _lines.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (_tops[mid] <= y)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return Math.Max(0, lo);
    }

    private void UpdateCanvasSize()
    {
        EnsureTops();
        _canvas.Width = Math.Max(1, _scroll.ViewportWidth);
        _canvas.Height = Math.Min(16_000_000, Math.Max(_scroll.ViewportHeight, PadTop + _totalHeight + PadBottom));
    }

    /// <summary>Width the lines wrap at (before the first layout pass the last known one).</summary>
    private float TextWidth() => _scroll.ViewportWidth > 0 ? (float)Math.Max(50, _scroll.ViewportWidth - PadLeft - PadRight) : _layoutWidth;

    private void OnViewportResized()
    {
        var width = TextWidth();
        if (Math.Abs(width - _layoutWidth) > 0.5f)
        {
            _layoutWidth = width;
            RelayoutAll();
        }
        else
        {
            UpdateCanvasSize();
        }

        UpdateOverflow();
    }

    /// <summary>After a draw pass: layouts of lines not drawn for a while go (they are made again when needed).</summary>
    private void TrimLayouts()
    {
        var live = _lines.Count(l => l.Layout is not null);
        if (live <= MaxLiveLayouts)
        {
            return;
        }

        foreach (var line in _lines.Where(l => l.Layout is not null).OrderBy(l => l.LastUsed).Take(live - MaxLiveLayouts / 2).ToList())
        {
            line.Drop();
        }
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}
