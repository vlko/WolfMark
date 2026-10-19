using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using WolfMark.Core.Markdown;

namespace WolfMark.Desktop.Editor;

/// <summary>
/// Formatted mode keeps the Markdown markers hidden; on the caret line they appear as tiny chips at the top
/// corners before and after the formatted text, so revealing them never shifts the text (ADR 0001; the
/// overlay of src/WolfMark.Editor/MarkdownEditor.Formats.cs). An overlay above the whole editor, not a TextView
/// layer: a chip before the first character sits left of the text, which the TextView would clip.
/// </summary>
internal sealed class MarkerChips : Control
{
    private readonly MarkdownTextEditor _owner;
    private readonly TextView _textView;

    public MarkerChips(MarkdownTextEditor owner, TextView textView)
    {
        _owner = owner;
        _textView = textView;
        IsHitTestVisible = false;
        textView.ScrollOffsetChanged += (_, _) => InvalidateVisual();
        textView.VisualLinesChanged += (_, _) => InvalidateVisual();
    }

    private static readonly HashSet<MarkdownTokenKind> Inline =
    [
        MarkdownTokenKind.Strong, MarkdownTokenKind.Emphasis, MarkdownTokenKind.Strikethrough,
        MarkdownTokenKind.InlineCode, MarkdownTokenKind.LinkText, MarkdownTokenKind.WikiLink,
    ];

    public override void Render(DrawingContext drawingContext)
    {
        var owner = _owner;
        var textView = _textView;
        if (!textView.VisualLinesValid || owner.Mode != WolfMark.Core.Settings.EditorMode.Formatted || textView.Document is not { } doc
            || owner.CaretLine is not (> 0 and var number) || number > doc.LineCount)
        {
            return;
        }

        var line = doc.GetLineByNumber(number);
        if (owner.TokensOf(line) is not { } tokens)
        {
            return;
        }

        var text = doc.GetText(line);
        var theme = owner.MarkdownTheme;
        void Chip(string label, int column, bool before)
        {
            if (column < 0 || column > text.Length)
            {
                return;
            }

            var location = doc.GetLocation(line.Offset + column);
            var inView = textView.GetVisualPosition(new AvaloniaEdit.TextViewPosition(location), VisualYPosition.TextTop) - textView.ScrollOffset;
            if (textView.TranslatePoint(inView, this) is not { } position)
            {
                return;
            }

            var formatted = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(theme.MonoFont), 9, new SolidColorBrush(theme.Muted));
            var width = formatted.Width + 4;
            var height = formatted.Height;
            var left = before ? position.X - width + 2 : position.X - 2;
            var top = position.Y - (height * 0.55);
            drawingContext.DrawRectangle(new SolidColorBrush(theme.CodeBackground, 0.9), null, new Rect(left, top, width, height), 2, 2);
            drawingContext.DrawText(formatted, new Point(left + 2, top));
        }

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

            if (!Inline.Contains(t.Kind) || t.Length == 0)
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
    }
}
