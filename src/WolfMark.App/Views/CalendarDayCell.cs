using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WolfMark.App.Theming;
using WolfMark.Core.Calendar;

namespace WolfMark.App.Views;

/// <summary>State a day cell renders (shared by the week band and the month grid).</summary>
internal readonly record struct DayCellState(
    DateOnly Date,
    CalendarDay Day,
    bool IsToday,
    bool IsPast,
    bool IsSelected,
    bool InMonth,
    bool IsDimmed,
    string AutomationName);

/// <summary>
/// Flat, square day cell (ADR 0004, reference <c>expected_calendar.png</c>): tag colours as diagonal
/// bands, heavy numerals, note-count dots, hatched past days and softer tag colours in the week band (the month grid: full colours), two-tone dashed frame for today.
/// Focusable and announced by Narrator (M6-05).
/// </summary>
internal sealed partial class CalendarDayCell : ContentControl
{
    private readonly TagBrushes _brushes;
    private readonly bool _weekBand; // hatched past days and softer colours (the month grid: full colours)
    private readonly Border _fill = new();
    // Stripes "\" over a past day, drawn as lines: a repeating gradient brush made WinUI fade other elements (Palette.PastHatch).
    private readonly Microsoft.UI.Xaml.Shapes.Path _hatch = new()
    {
        Stroke = Palette.PastHatch,
        StrokeThickness = HatchStep / 2 / Math.Sqrt(2), // half of the period, measured across the stripe
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
    };
    private readonly Rectangle _frame = new() { StrokeThickness = 3, Stroke = Palette.TodayFrame, IsHitTestVisible = false };
    private readonly Rectangle _frameDash = new() { StrokeThickness = 3, Stroke = Palette.TodayFrameDash, StrokeDashArray = [2, 2], IsHitTestVisible = false };
    private readonly Border _selection = new() { BorderBrush = Palette.Ink, BorderThickness = new Thickness(0, 0, 0, 3), IsHitTestVisible = false };
    // Own keyboard-focus marker instead of the system focus visual: the system's black/white frame came and
    // went with the focus, so the selected day looked lighter as soon as the editor took the focus.
    private readonly Rectangle _focus = new() { StrokeThickness = 2, Stroke = Palette.Accent, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock _number = new() { FontFamily = Typo.DayNumber, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
    // Open tasks of the day: a small box and their number in the corner (M9-01).
    private readonly Rectangle _taskBox = new() { Width = 6, Height = 6, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _taskCount = new() { FontFamily = Typo.CondensedBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _tasks = new() { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 5, 0), IsHitTestVisible = false };
    private const double HatchStep = 8; // px between the stripes along the cell, as the gradient 4 px across had

    private DayCellState _state;
    private int _colors = -1; // TagBrushes.Generation the cell was drawn with
    private bool _hover;

    public CalendarDayCell(TagBrushes brushes, double height, double numberSize, bool weekBand)
    {
        _brushes = brushes;
        _weekBand = weekBand;
        _number.FontSize = numberSize;
        _number.Margin = new Thickness(0, 0, 0, height * 0.12);
        _dots.Margin = new Thickness(0, 0, 0, height * 0.12);

        var grid = new Grid();
        grid.Children.Add(_fill);
        grid.Children.Add(_hatch);
        grid.Children.Add(_selection);
        grid.Children.Add(_frame);
        grid.Children.Add(_frameDash);
        grid.Children.Add(_focus);
        grid.Children.Add(_number);
        grid.Children.Add(_dots);
        _taskCount.FontSize = weekBand ? 11 : 10;
        _tasks.Children.Add(_taskBox);
        _tasks.Children.Add(_taskCount);
        grid.Children.Add(_tasks);

        Content = grid;
        Height = height;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        GotFocus += (_, _) => _focus.Visibility = FocusState == FocusState.Keyboard ? Visibility.Visible : Visibility.Collapsed;
        LostFocus += (_, _) => _focus.Visibility = Visibility.Collapsed;
        CornerRadius = Radius.None;

        if (weekBand)
        {
            grid.SizeChanged += (_, e) => _hatch.Data = Stripes(e.NewSize.Width, e.NewSize.Height);
        }

        PointerEntered += (_, _) => SetHover(true);
        PointerExited += (_, _) => SetHover(false);
        // A cell hidden or covered under the pointer gets no PointerExited: the lighter fill stayed until the next hover.
        PointerCanceled += (_, _) => SetHover(false);
        PointerCaptureLost += (_, _) => SetHover(false);
        Unloaded += (_, _) => SetHover(false);
    }

    public DateOnly Date => _state.Date;

    /// <summary>Lines "\" (45°) every <see cref="HatchStep"/> px, cut to the cell.</summary>
    private static GeometryGroup Stripes(double width, double height)
    {
        var stripes = new GeometryGroup();
        for (var x = -height; x < width; x += HatchStep)
        {
            // The line x + y from the top edge to the bottom one, without what is left or right of the cell.
            var top = Math.Max(0, -x);
            var bottom = Math.Min(height, width - x);
            if (bottom > top)
            {
                stripes.Children.Add(new LineGeometry
                {
                    StartPoint = new Windows.Foundation.Point(x + top, top),
                    EndPoint = new Windows.Foundation.Point(x + bottom, bottom),
                });
            }
        }

        return stripes;
    }

    private void SetHover(bool hover)
    {
        _hover = hover;
        _fill.Opacity = hover ? 0.85 : 1;
    }

    /// <summary>The same picture: the days compared by what is drawn (every load brings a new list of tags).</summary>
    private static bool Same(DayCellState a, DayCellState b) =>
        a.Date == b.Date && a.IsToday == b.IsToday && a.IsPast == b.IsPast && a.IsSelected == b.IsSelected
        && a.InMonth == b.InMonth && a.IsDimmed == b.IsDimmed && a.AutomationName == b.AutomationName
        && a.Day.NoteCount == b.Day.NoteCount && a.Day.OpenTasks == b.Day.OpenTasks
        && a.Day.Tags.SequenceEqual(b.Day.Tags);

    public void Update(DayCellState state)
    {
        // Nothing to redraw: the band is refreshed after every save, mostly with the same days. New brushes and dots
        // each time were work for nothing (M6-09).
        if (_colors == _brushes.Generation && Same(state, _state))
        {
            return;
        }

        _state = state;
        _colors = _brushes.Generation;
        _fill.Opacity = _hover ? 0.85 : 1;
        var day = state.Day;
        Opacity = !state.InMonth ? 0 : state.IsDimmed ? 0.2 : 1;
        IsTabStop = state.InMonth;
        IsHitTestVisible = state.InMonth;
        _number.Text = state.Date.Day.ToString(CultureInfo.CurrentCulture);

        _fill.Background = _brushes.DayFill(day.Tags, Palette.EmptyCell, soft: _weekBand);
        _hatch.Visibility = _weekBand && state.IsPast ? Visibility.Visible : Visibility.Collapsed;
        _frame.Visibility = _frameDash.Visibility = state.IsToday ? Visibility.Visible : Visibility.Collapsed;
        _selection.Visibility = state.IsSelected && !state.IsToday ? Visibility.Visible : Visibility.Collapsed;

        var hatched = _weekBand && state.IsPast;
        Brush ink;
        if (day.HasNotes)
        {
            // Hatched past days: a dark (light theme) or light (dark theme) shade of the first tag (Palette.PastInk);
            // full fills black/white by the tag's calendar tint. Days without notes: grey (past), light grey (future).
            var tag = _brushes.ColorOf(day.Tags[0].Tag.Length == 0 ? null : day.Tags[0].Tag);
            ink = new SolidColorBrush(hatched ? Palette.PastInk(tag) : _weekBand ? Palette.ForegroundOnTint(tag) : Palette.ForegroundOn(tag));
        }
        else
        {
            ink = hatched ? Palette.InkSecondary : Palette.InkTertiary;
        }

        _number.Foreground = ink;

        _dots.Children.Clear();
        for (var i = 0; i < Math.Min(day.NoteCount, 5) && day.NoteCount > 1; i++)
        {
            _dots.Children.Add(new Rectangle { Width = 4, Height = 4, Fill = ink, Opacity = hatched ? 1 : 0.8 });
        }

        _tasks.Visibility = day.OpenTasks > 0 ? Visibility.Visible : Visibility.Collapsed;
        _taskCount.Text = day.OpenTasks.ToString(CultureInfo.CurrentCulture);
        // The bands run diagonally across a wide cell: its top right corner is about 0.9 of the way. The count there
        // takes its contrast from that band, the number from the first one.
        var cornerInk = ink;
        if (day.Tags.Count > 1 && day.TagAtShare(0.9) is { } cornerTag)
        {
            var corner = _brushes.ColorOf(cornerTag.Length == 0 ? null : cornerTag);
            cornerInk = new SolidColorBrush(hatched ? Palette.PastInk(corner) : _weekBand ? Palette.ForegroundOnTint(corner) : Palette.ForegroundOn(corner));
        }

        _taskCount.Foreground = _taskBox.Stroke = cornerInk;

        AutomationProperties.SetName(this, state.AutomationName);
    }
}
