using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using WolfMark.Core.Markdown;

namespace WolfMark.Desktop.Editor;

/// <summary>
/// Formatted mode, caret outside a table (M4-04, as in the Windows editor): the table's lines become one element, a
/// grid – header bold on a tinted row, columns sized to their text and aligned as the delimiter row says, cells
/// wrapped when the table is wider than the text. The element spans every line of the table (as a fold does), so the
/// caret stands before or after it; inside, the table is its Markdown source again. A click on a cell puts the caret
/// at the end of that cell's source.
/// </summary>
internal sealed class TableElementGenerator(MarkdownTextEditor owner) : VisualLineElementGenerator
{
    private const double PadX = 8;
    internal const double PadY = 4;

    /// <summary>From the top of the element to the grid's first row: the margin above and the border.</summary>
    internal const double GridInset = 7;

    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.Document.GetLineByOffset(startOffset);
        return startOffset <= line.Offset && owner.ShownTableAt(line.LineNumber) is { } table && table.FirstLine == line.LineNumber - 1
            ? line.Offset
            : -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        var doc = CurrentContext.Document;
        var line = doc.GetLineByOffset(offset);
        if (offset != line.Offset || owner.ShownTableAt(line.LineNumber) is not { } table || table.FirstLine != line.LineNumber - 1)
        {
            return null;
        }

        var last = doc.GetLineByNumber(table.LastLine + 1);
        var grid = Grid(table, out var element);
        owner.RememberGrid(table.FirstLine, grid);
        return new InlineObjectElement(last.EndOffset - line.Offset, OnBaseline(element));
    }

    private Grid Grid(MarkdownTable table, out Control element)
    {
        var theme = owner.MarkdownTheme;
        var doc = CurrentContext.Document;
        var rule = new SolidColorBrush(theme.Muted, 0.55);
        var blocks = new TextBlock[table.RowCount, table.ColumnCount];
        var ends = new int[table.RowCount, table.ColumnCount];
        var natural = new double[table.ColumnCount];
        for (var r = 0; r < table.RowCount; r++)
        {
            var line = doc.GetLineByNumber(table.LineOfRow(r) + 1);
            var text = doc.GetText(line);
            var tokens = owner.TokensOf(line) ?? [];
            var cells = MarkdownTable.Cells(text);
            for (var c = 0; c < table.ColumnCount; c++)
            {
                (int Start, int Length) cell = c < cells.Length ? cells[c] : (text.Length, 0);
                var block = Cell(text.Substring(cell.Start, cell.Length), MarkdownTable.CellTokens(tokens, cell), theme, header: r == 0);
                block.TextAlignment = table.Aligns[c] switch
                {
                    TableAlign.Center => TextAlignment.Center,
                    TableAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                };
                block.Measure(Size.Infinity);
                natural[c] = Math.Max(natural[c], Math.Ceiling(block.DesiredSize.Width) + 2 * PadX + 1);
                blocks[r, c] = block;
                ends[r, c] = line.Offset + cell.Start + cell.Length;
            }
        }

        var available = Math.Max(80, CurrentContext.TextView.Bounds.Width - 40);
        var widths = MarkdownTable.FitColumns(natural, available, minimum: 2 * PadX + 24);
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var width in widths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(width, GridUnitType.Pixel));
        }

        for (var r = 0; r < table.RowCount; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var c = 0; c < table.ColumnCount; c++)
            {
                var end = ends[r, c];
                var border = new Border
                {
                    Child = blocks[r, c],
                    Padding = new Thickness(PadX, PadY),
                    Background = r == 0 ? new SolidColorBrush(theme.CodeBackground) : Brushes.Transparent,
                    BorderBrush = rule,
                    BorderThickness = new Thickness(0, 0, c < table.ColumnCount - 1 ? 1 : 0, r < table.RowCount - 1 ? (r == 0 ? 1.5 : 0.75) : 0),
                    Cursor = new Cursor(StandardCursorType.Ibeam),
                };
                blocks[r, c].TextWrapping = TextWrapping.Wrap;
                border.PointerPressed += (_, e) =>
                {
                    e.Handled = true;
                    owner.PlaceCaret(end);
                };
                Avalonia.Controls.Grid.SetRow(border, r);
                Avalonia.Controls.Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }
        }

        element = new Border
        {
            Child = grid,
            BorderBrush = rule,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            ClipToBounds = true,
            Margin = new Thickness(0, GridInset - 1, 0, 3), // below: the line's descent adds ~3 px
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        return grid;
    }

    /// <summary>
    /// An inline object's baseline is <see cref="TextBlock.BaselineOffsetProperty"/>, 0 unless set: AvaloniaEdit then
    /// hangs the object below the text baseline and the row gets a font ascent (~14 px) of empty space above it.
    /// With the baseline at its bottom it sits on the text line like a tall glyph.
    /// </summary>
    internal static Control OnBaseline(Control control)
    {
        control.Measure(Size.Infinity);
        TextBlock.SetBaselineOffset(control, control.DesiredSize.Height);
        return control;
    }

    /// <summary>A cell's text with the delimiters hidden and its inline formats as runs.</summary>
    private static TextBlock Cell(string text, MarkdownToken[] tokens, EditorTheme theme, bool header)
    {
        var presentation = LinePresentation.Create(text, tokens, hideSyntax: true);
        var shown = presentation.Text;
        var cuts = new SortedSet<int> { 0, shown.Length };
        foreach (var t in presentation.Tokens)
        {
            cuts.Add(Math.Clamp(t.Start, 0, shown.Length));
            cuts.Add(Math.Clamp(t.End, 0, shown.Length));
        }

        var block = new TextBlock { FontFamily = theme.Font, FontSize = theme.FontSize, Foreground = new SolidColorBrush(theme.Text) };
        var bounds = cuts.ToArray();
        for (var k = 0; k + 1 < bounds.Length; k++)
        {
            var (from, to) = (bounds[k], bounds[k + 1]);
            var run = new Run(shown[from..to]) { FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal };
            foreach (var t in presentation.Tokens.Where(t => t.Start <= from && t.End >= to))
            {
                switch (t.Kind)
                {
                    case MarkdownTokenKind.Strong:
                        run.FontWeight = FontWeight.Bold;
                        break;
                    case MarkdownTokenKind.Emphasis:
                        run.FontStyle = FontStyle.Italic;
                        break;
                    case MarkdownTokenKind.Strikethrough:
                        run.TextDecorations = TextDecorations.Strikethrough;
                        break;
                    case MarkdownTokenKind.InlineCode:
                        run.FontFamily = theme.MonoFont;
                        run.Foreground = new SolidColorBrush(theme.Code);
                        break;
                    case MarkdownTokenKind.LinkText or MarkdownTokenKind.WikiLink or MarkdownTokenKind.AutoLink:
                        run.Foreground = new SolidColorBrush(theme.Link);
                        run.TextDecorations = TextDecorations.Underline;
                        break;
                    case MarkdownTokenKind.Hashtag:
                        run.FontWeight = FontWeight.Bold;
                        run.Foreground = new SolidColorBrush(theme.TagColor(shown.Substring(t.Start + 1, Math.Max(0, t.Length - 1))));
                        break;
                }
            }

            block.Inlines!.Add(run);
        }

        return block;
    }
}
