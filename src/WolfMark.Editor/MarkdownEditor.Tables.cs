using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Windows.Foundation;
using WolfMark.Core.Diagnostics;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor;

/// <summary>
/// GFM tables in formatted mode (M4-04). While the caret is outside a table it shows as a grid – header bold on a
/// tinted row, columns sized to their text and aligned as the delimiter row says, cells wrapped when the table is
/// wider than the text column. The grid takes the place of the table's lines: the first line is as tall as the grid,
/// the others take no room. With the caret inside, the table is its Markdown source again (pipes and the delimiter
/// row dimmed), typed like any text; a click on a cell puts the caret into that cell's source.
/// </summary>
public sealed partial class MarkdownEditor
{
    private const float CellPadX = 8;
    private const float CellPadY = 4;
    private const double TableGap = 6;
    private const int MaxGrids = 32;

    private List<MarkdownTable>? _tables; // null: to be found again (the text changed)
    private readonly Dictionary<MarkdownTable, TableGrid> _gridOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, TableGrid> _grids = new(StringComparer.Ordinal); // by source: survives edits elsewhere
    private int _activeTable = -1; // first line of the table holding the caret when last checked
    private int _gridNearCaret; // lines around which RelayoutAll builds grids at once (others: estimate, then idle)
    private int _gridNearView;
    private bool _gridsPending; // a shown table has only an estimated height
    private bool _gridsQueued;

    /// <summary>A table laid out as a grid, in grid coordinates (0, 0 = its top left corner).</summary>
    private sealed class TableGrid(CanvasTextLayout[][] cells, LinePresentation[][] presentations, (int Start, int Length)[][] sources, double[] columnX, double[] rowY) : IDisposable
    {
        public CanvasTextLayout[][] Cells { get; } = cells;

        public LinePresentation[][] Presentations { get; } = presentations;

        public (int Start, int Length)[][] Sources { get; } = sources; // cell ranges in the row's source line

        public double[] ColumnX { get; } = columnX; // left edges, one more for the right edge

        public double[] RowY { get; } = rowY; // top edges, one more for the bottom edge

        public double Width => ColumnX[^1];

        public double Height => RowY[^1];

        public long LastUsed { get; set; }

        public void Dispose()
        {
            foreach (var row in Cells)
            {
                foreach (var cell in row)
                {
                    cell.Dispose();
                }
            }
        }
    }

    private List<MarkdownTable> Tables => _tables ??= FindTables();

    private List<MarkdownTable> FindTables()
    {
        _gridOf.Clear();
        return MarkdownTable.Find(_doc.LineCount, _doc.GetLineText, IsCodeLine);
    }

    /// <summary>Inside a fenced code block or the front matter (fence lines included): never a table.</summary>
    private bool IsCodeLine(int line)
    {
        if (_doc.GetStateAtLineStart(line).Kind is LineStateKind.FencedCode or LineStateKind.FrontMatter)
        {
            return true;
        }

        foreach (var t in _doc.GetTokenArray(line))
        {
            if (t.Kind is MarkdownTokenKind.CodeFence or MarkdownTokenKind.CodeBlock or MarkdownTokenKind.FrontMatter)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The text changed: tables are found again when next asked for.</summary>
    private void InvalidateTables() => _tables = null;

    private MarkdownTable? TableAt(int line)
    {
        var tables = Tables;
        int lo = 0, hi = tables.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var t = tables[mid];
            if (line < t.FirstLine)
            {
                hi = mid - 1;
            }
            else if (line > t.LastLine)
            {
                lo = mid + 1;
            }
            else
            {
                return t;
            }
        }

        return null;
    }

    /// <summary>The table drawn as a grid over this line: formatted mode, caret elsewhere.</summary>
    private MarkdownTable? ShownTable(int line) =>
        _mode == MarkdownEditorMode.Formatted && _lines.Count > 0 && TableAt(line) is { } t && !t.Contains(_doc.GetLineAtOffset(_caret)) ? t : null;

    /// <summary>
    /// The caret went into or out of a table: its lines change between grid and source, the heights with them.
    /// Called on every caret move and edit (cheap: one table lookup).
    /// </summary>
    private void SyncActiveTable()
    {
        var active = _mode == MarkdownEditorMode.Formatted && TableAt(_doc.GetLineAtOffset(_caret)) is { } t ? t.FirstLine : -1;
        if (active != _activeTable)
        {
            _activeTable = active;
            _topsDirty = true;
        }
    }

    /// <summary>
    /// The grid of a shown table's first line, or 0 for its other lines; null if the line is no grid. Grids near the
    /// caret or the view are built at once; a table further away gets an estimate until <see cref="BuildPendingGrids"/>
    /// builds it when the UI is idle – a width change (window resize) rebuilt every grid of a long note on every step.
    /// </summary>
    private double? TableLineHeight(int line)
    {
        if (ShownTable(line) is not { } table)
        {
            return null;
        }

        if (line != table.FirstLine)
        {
            return 0;
        }

        var near = Math.Abs(table.FirstLine - _gridNearCaret) <= ExactAround || Math.Abs(table.FirstLine - _gridNearView) <= ExactAround;
        if ((CachedGrid(table) ?? (near ? GridOf(table) : null)) is { } grid)
        {
            return grid.Height + 2 * TableGap;
        }

        _gridsPending = true;
        return (table.RowCount * (_emptyLineHeight + 2 * CellPadY)) + 2 * TableGap;
    }

    /// <summary>RelayoutAll: grids around these lines are built at once.</summary>
    private void SetGridWindow(int caretLine, int viewLine) => (_gridNearCaret, _gridNearView) = (caretLine, viewLine);

    /// <summary>After the heights were summed: grids left with an estimate are built later, a few per idle moment.</summary>
    private void QueueGridBuild()
    {
        if (!_gridsPending || _gridsQueued)
        {
            return;
        }

        _gridsPending = false;
        _gridsQueued = _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, BuildPendingGrids);
    }

    private void BuildPendingGrids()
    {
        _gridsQueued = false;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var built = false;
        var more = false;
        foreach (var table in Tables)
        {
            if (ShownTable(table.FirstLine) is null || CachedGrid(table) is not null)
            {
                continue;
            }

            if (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds > 6)
            {
                more = true; // the rest on the next idle moment: input and frames go first
                break;
            }

            GridOf(table);
            built = true;
        }

        if (built)
        {
            var anchor = ViewAnchor();
            _topsDirty = true;
            UpdateCanvasSize();
            RestoreViewAnchor(anchor);
            _canvas.Invalidate();
        }

        if (more)
        {
            _gridsPending = true;
            QueueGridBuild();
        }
    }

    /// <summary>The grid if it is built already (none is built here).</summary>
    private TableGrid? CachedGrid(MarkdownTable table)
    {
        if (_gridOf.TryGetValue(table, out var grid))
        {
            return grid;
        }

        if (_grids.TryGetValue(TableKey(table), out grid))
        {
            _gridOf[table] = grid;
            return grid;
        }

        return null;
    }

    private TableGrid GridOf(MarkdownTable table)
    {
        if (!_gridOf.TryGetValue(table, out var grid))
        {
            var key = TableKey(table);
            if (!_grids.TryGetValue(key, out grid))
            {
                PruneGrids();
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                grid = BuildGrid(table);
                EditorTrace.Current?.RecordSince(EditorPhase.Measure, started, table.RowCount * table.ColumnCount, () => $"table grid {table.RowCount}×{table.ColumnCount} at line {table.FirstLine + 1}");
                _grids[key] = grid;
            }

            _gridOf[table] = grid;
        }

        grid.LastUsed = _drawStamp;
        return grid;
    }

    private string TableKey(MarkdownTable table)
    {
        var parts = new string[table.LineCount];
        for (var k = 0; k < parts.Length; k++)
        {
            parts[k] = _doc.GetLineText(table.FirstLine + k);
        }

        return string.Join('\n', parts);
    }

    /// <summary>Grids of tables edited away or scrolled out of use go once there are many.</summary>
    private void PruneGrids()
    {
        if (_grids.Count < MaxGrids)
        {
            return;
        }

        foreach (var (key, grid) in _grids.OrderBy(g => g.Value.LastUsed).Take(MaxGrids / 2).ToList())
        {
            _grids.Remove(key);
            grid.Dispose();
            foreach (var (table, g) in _gridOf.Where(p => p.Value == grid).ToList())
            {
                _gridOf.Remove(table);
            }
        }
    }

    /// <summary>Width, theme or mode changed: every grid is laid out again when needed.</summary>
    private void ClearGrids()
    {
        foreach (var grid in _grids.Values)
        {
            grid.Dispose();
        }

        _grids.Clear();
        _gridOf.Clear();
    }

    private TableGrid BuildGrid(MarkdownTable table)
    {
        var rows = table.RowCount;
        var columns = table.ColumnCount;
        var cells = new CanvasTextLayout[rows][];
        var presentations = new LinePresentation[rows][];
        var sources = new (int Start, int Length)[rows][];
        var natural = new double[columns];
        var device = CanvasDevice.GetSharedDevice();
        if (_format is null)
        {
            RecreateFormat();
        }

        for (var r = 0; r < rows; r++)
        {
            var line = table.LineOfRow(r);
            var text = _doc.GetLineText(line);
            var tokens = _doc.GetTokenArray(line);
            var found = MarkdownTable.Cells(text);
            cells[r] = new CanvasTextLayout[columns];
            presentations[r] = new LinePresentation[columns];
            sources[r] = new (int Start, int Length)[columns];
            for (var c = 0; c < columns; c++)
            {
                (int Start, int Length) source = c < found.Length ? found[c] : (text.Length, 0); // a short row: empty cells
                var presentation = LinePresentation.Create(text.Substring(source.Start, source.Length), MarkdownTable.CellTokens(tokens, source), hideSyntax: true);
                var layout = new CanvasTextLayout(device, presentation.Text, _format, 100_000, 0);
                ApplyStyles(layout, presentation, _theme);
                if (r == 0)
                {
                    layout.SetFontWeight(0, presentation.Text.Length, FontWeights.SemiBold);
                }

                layout.HorizontalAlignment = table.Aligns[c] switch
                {
                    TableAlign.Center => CanvasHorizontalAlignment.Center,
                    TableAlign.Right => CanvasHorizontalAlignment.Right,
                    _ => CanvasHorizontalAlignment.Left,
                };
                layout.Options = _theme.ColorFonts ? CanvasDrawTextOptions.EnableColorFont : CanvasDrawTextOptions.Default;
                natural[c] = Math.Max(natural[c], Math.Ceiling(layout.LayoutBounds.Width) + 2 * CellPadX);
                cells[r][c] = layout;
                presentations[r][c] = presentation;
                sources[r][c] = source;
            }
        }

        var widths = MarkdownTable.FitColumns(natural, Math.Max(80, _layoutWidth), minimum: 2 * CellPadX + 24);
        var columnX = new double[columns + 1];
        for (var c = 0; c < columns; c++)
        {
            columnX[c + 1] = columnX[c] + widths[c];
        }

        var rowY = new double[rows + 1];
        for (var r = 0; r < rows; r++)
        {
            var height = _emptyLineHeight;
            for (var c = 0; c < columns; c++)
            {
                var layout = cells[r][c];
                layout.RequestedSize = new Size(widths[c] - 2 * CellPadX, 0); // wraps a cell the column is too narrow for
                height = Math.Max(height, layout.LayoutBounds.Height);
            }

            rowY[r + 1] = rowY[r] + height + 2 * CellPadY;
        }

        return new TableGrid(cells, presentations, sources, columnX, rowY);
    }

    /// <summary>The grid at its table's first line: header tint, cell text, rules, selected rows.</summary>
    private void DrawTable(CanvasDrawingSession ds, MarkdownTable table, double top, int selStart, int selEnd)
    {
        if (CachedGrid(table) is null)
        {
            // Scrolled to a table before the idle pass built it: its real height from now on.
            _topsDirty = true;
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                UpdateCanvasSize();
                _canvas.Invalidate();
            });
        }

        var grid = GridOf(table);
        var x = (float)PadLeft;
        var y = (float)(top + TableGap);
        var width = (float)grid.Width;
        var height = (float)grid.Height;
        var rule = WithAlpha(_theme.Muted, 0x90);
        ds.FillRectangle(x, y, width, (float)grid.RowY[1], _theme.CodeBackground);
        var selection = WithAlpha(_theme.Link, 0x40);
        for (var r = 0; r < table.RowCount; r++)
        {
            var line = table.LineOfRow(r);
            var lineStart = _doc.GetLineStart(line);
            var lineEnd = lineStart + _doc.GetLineText(line).Length;
            if (selEnd > selStart && selEnd > lineStart && selStart <= lineEnd)
            {
                ds.FillRectangle(x, y + (float)grid.RowY[r], width, (float)(grid.RowY[r + 1] - grid.RowY[r]), selection);
            }

            for (var c = 0; c < table.ColumnCount; c++)
            {
                ds.DrawTextLayout(grid.Cells[r][c], x + (float)grid.ColumnX[c] + CellPadX, y + (float)grid.RowY[r] + CellPadY, _theme.Text);
            }
        }

        for (var r = 1; r < table.RowCount; r++)
        {
            var ry = y + (float)grid.RowY[r];
            ds.DrawLine(x, ry, x + width, ry, rule, r == 1 ? 1.2f : 0.6f);
        }

        for (var c = 1; c < table.ColumnCount; c++)
        {
            var cx = x + (float)grid.ColumnX[c];
            ds.DrawLine(cx, y, cx, y + height, rule, 0.6f);
        }

        ds.DrawRoundedRectangle(x, y, width, height, 3, 3, rule, 1f);
    }

    /// <summary>A point on a shown grid: the source offset in the clicked cell (the grid opens as source there).</summary>
    private int TableOffsetAt(MarkdownTable table, Point point)
    {
        var grid = GridOf(table);
        var gx = point.X - PadLeft;
        var gy = point.Y - LineTop(table.FirstLine) - TableGap;
        var r = 0;
        while (r < table.RowCount - 1 && gy >= grid.RowY[r + 1])
        {
            r++;
        }

        var c = 0;
        while (c < table.ColumnCount - 1 && gx >= grid.ColumnX[c + 1])
        {
            c++;
        }

        var line = table.LineOfRow(r);
        var source = grid.Sources[r][c];
        var presentation = grid.Presentations[r][c];
        grid.Cells[r][c].HitTest(
            (float)(gx - grid.ColumnX[c] - CellPadX),
            (float)Math.Clamp(gy - grid.RowY[r] - CellPadY, 0, Math.Max(0, grid.Cells[r][c].LayoutBounds.Height - 1)),
            out var region,
            out var trailing);
        var shown = presentation.Text.Length == 0 ? 0 : region.CharacterIndex + (trailing ? region.CharacterCount : 0);
        return _doc.GetLineStart(line) + source.Start + presentation.ToSource(shown);
    }

    /// <summary>Raw source of a table (caret inside, or raw mode): pipes and the delimiter row dimmed.</summary>
    private void DimTableSyntax(int line, CanvasTextLayout layout, LinePresentation presentation)
    {
        if (TableAt(line) is not { } table)
        {
            return;
        }

        if (line == table.FirstLine + 1)
        {
            layout.SetColor(0, presentation.Text.Length, _theme.Muted);
            return;
        }

        foreach (var pipe in MarkdownTable.Pipes(_doc.GetLineText(line)))
        {
            var shown = presentation.ToShown(pipe);
            if (shown < presentation.Text.Length && presentation.ToSource(shown) == pipe)
            {
                layout.SetColor(shown, 1, _theme.Muted);
            }
        }
    }
}
