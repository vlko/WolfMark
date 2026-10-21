using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using WolfMark.Core.Markdown;

namespace WolfMark.Desktop.Editor;

/// <summary>
/// Colours, weights and sizes of the Markdown tokens – the rules of the Windows editor's StyleTokens
/// (src/WolfMark.Editor/MarkdownEditor.cs), applied per visible line from the highlighter's snapshot.
/// </summary>
internal sealed class MarkdownColorizer(MarkdownTextEditor owner) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        if (owner.TokensOf(line) is not { } tokens)
        {
            return;
        }

        var theme = owner.MarkdownTheme;
        var start = line.Offset;
        foreach (var token in tokens)
        {
            if (token.Length <= 0 || token.End > line.Length)
            {
                continue;
            }

            Action<VisualLineElement>? style = token.Kind switch
            {
                MarkdownTokenKind.Heading1 or MarkdownTokenKind.Heading2 or MarkdownTokenKind.Heading3
                    or MarkdownTokenKind.Heading4 or MarkdownTokenKind.Heading5 or MarkdownTokenKind.Heading6 => e =>
                    {
                        e.TextRunProperties.SetFontRenderingEmSize(theme.HeadingSize(token.Kind));
                        Weight(e, FontWeight.Bold);
                        e.TextRunProperties.SetForegroundBrush(Brush(theme.Heading));
                    },
                MarkdownTokenKind.Syntax or MarkdownTokenKind.LinkUrl or MarkdownTokenKind.HorizontalRule => e => e.TextRunProperties.SetForegroundBrush(Brush(theme.Muted)),
                MarkdownTokenKind.Strong => e => Weight(e, FontWeight.Bold),
                MarkdownTokenKind.Emphasis => e => Style(e, FontStyle.Italic),
                MarkdownTokenKind.Strikethrough => e => e.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough),
                MarkdownTokenKind.InlineCode or MarkdownTokenKind.CodeBlock => e =>
                {
                    Font(e, theme.MonoFont);
                    e.TextRunProperties.SetForegroundBrush(Brush(theme.Code));
                    e.TextRunProperties.SetBackgroundBrush(Brush(theme.CodeBackground));
                },
                MarkdownTokenKind.CodeFence or MarkdownTokenKind.FrontMatter => e =>
                {
                    Font(e, theme.MonoFont);
                    e.TextRunProperties.SetForegroundBrush(Brush(theme.Muted));
                    e.TextRunProperties.SetFontRenderingEmSize(theme.FontSize * 0.9);
                },
                MarkdownTokenKind.LinkText or MarkdownTokenKind.WikiLink or MarkdownTokenKind.AutoLink => e =>
                {
                    e.TextRunProperties.SetForegroundBrush(Brush(theme.Link));
                    e.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                },
                MarkdownTokenKind.Image => e => e.TextRunProperties.SetForegroundBrush(Brush(theme.Link)),
                MarkdownTokenKind.ListMarker => e =>
                {
                    e.TextRunProperties.SetForegroundBrush(Brush(theme.Muted));
                    Weight(e, FontWeight.Bold);
                },
                MarkdownTokenKind.TaskOpen or MarkdownTokenKind.TaskDone => e =>
                {
                    e.TextRunProperties.SetForegroundBrush(Brush(token.Kind == MarkdownTokenKind.TaskOpen ? theme.TaskOpen : theme.TaskDone));
                    Font(e, theme.MonoFont);
                    Weight(e, FontWeight.Bold);
                },
                MarkdownTokenKind.TaskDoneText => e =>
                {
                    e.TextRunProperties.SetForegroundBrush(Brush(theme.TaskDone));
                    e.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough);
                },
                MarkdownTokenKind.Quote => e =>
                {
                    e.TextRunProperties.SetForegroundBrush(Brush(theme.Quote));
                    Style(e, FontStyle.Italic);
                },
                MarkdownTokenKind.Hashtag => e =>
                {
                    e.TextRunProperties.SetForegroundBrush(Brush(theme.TagColor(CurrentContext.Document.GetText(start + token.Start + 1, token.Length - 1))));
                    Weight(e, FontWeight.Bold);
                },
                _ => null,
            };

            if (style is not null)
            {
                ChangeLinePart(start + token.Start, start + token.End, style);
            }
        }

        DimTableSyntax(line, theme);
    }

    /// <summary>A table's source (raw mode, or the caret inside): pipes and the delimiter row dimmed.</summary>
    private void DimTableSyntax(DocumentLine line, EditorTheme theme)
    {
        if (owner.TableAt(line.LineNumber - 1) is not { } table || line.Length == 0)
        {
            return;
        }

        void Dim(VisualLineElement e) => e.TextRunProperties.SetForegroundBrush(Brush(theme.Muted));
        if (line.LineNumber - 1 == table.FirstLine + 1)
        {
            ChangeLinePart(line.Offset, line.EndOffset, Dim);
            return;
        }

        foreach (var pipe in MarkdownTable.Pipes(CurrentContext.Document.GetText(line)))
        {
            ChangeLinePart(line.Offset + pipe, line.Offset + pipe + 1, Dim);
        }
    }

    private static readonly Dictionary<Color, IBrush> Brushes = [];

    private static IBrush Brush(Color color)
    {
        if (!Brushes.TryGetValue(color, out var brush))
        {
            brush = new ImmutableSolidColorBrush(color);
            Brushes[color] = brush;
        }

        return brush;
    }

    private static void Weight(VisualLineElement e, FontWeight weight)
    {
        var t = e.TextRunProperties.Typeface;
        e.TextRunProperties.SetTypeface(new Typeface(t.FontFamily, t.Style, weight, t.Stretch));
    }

    private static void Style(VisualLineElement e, FontStyle style)
    {
        var t = e.TextRunProperties.Typeface;
        e.TextRunProperties.SetTypeface(new Typeface(t.FontFamily, style, t.Weight, t.Stretch));
    }

    private static void Font(VisualLineElement e, FontFamily family)
    {
        var t = e.TextRunProperties.Typeface;
        e.TextRunProperties.SetTypeface(new Typeface(family, t.Style, t.Weight, t.Stretch));
    }
}
