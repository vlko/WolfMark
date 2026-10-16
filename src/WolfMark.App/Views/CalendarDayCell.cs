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
/// bands, heavy numerals, note-count dots, hatched past days (band only), two-tone dashed frame for today.
/// Focusable and announced by Narrator (M6-05).
/// </summary>
internal sealed partial class CalendarDayCell : ContentControl
{
    private readonly TagBrushes _brushes;
    private readonly bool _hatchPast;
    private readonly Border _fill = new();
    private readonly Border _hatch = new() { Background = Palette.PastHatch, IsHitTestVisible = false };
    private readonly Rectangle _frame = new() { StrokeThickness = 3, Stroke = Palette.TodayFrame, IsHitTestVisible = false };
    private readonly Rectangle _frameDash = new() { StrokeThickness = 3, Stroke = Palette.TodayFrameDash, StrokeDashArray = [2, 2], IsHitTestVisible = false };
    private readonly Border _selection = new() { BorderBrush = Palette.Ink, BorderThickness = new Thickness(0, 0, 0, 3), IsHitTestVisible = false };
    private readonly TextBlock _number = new() { FontFamily = Typo.DayNumber, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
    private DayCellState _state;

    public CalendarDayCell(TagBrushes brushes, double height, double numberSize, bool hatchPast)
    {
        _brushes = brushes;
        _hatchPast = hatchPast;
        _number.FontSize = numberSize;
        _number.Margin = new Thickness(0, 0, 0, height * 0.12);
        _dots.Margin = new Thickness(0, 0, 0, height * 0.12);

        var grid = new Grid();
        grid.Children.Add(_fill);
        grid.Children.Add(_hatch);
        grid.Children.Add(_selection);
        grid.Children.Add(_frame);
        grid.Children.Add(_frameDash);
        grid.Children.Add(_number);
        grid.Children.Add(_dots);

        Content = grid;
        Height = height;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        CornerRadius = Radius.None;

        PointerEntered += (_, _) => _fill.Opacity = 0.85;
        PointerExited += (_, _) => _fill.Opacity = 1;
    }

    public DateOnly Date => _state.Date;

    public void Update(DayCellState state)
    {
        _state = state;
        var day = state.Day;
        Opacity = !state.InMonth ? 0 : state.IsDimmed ? 0.2 : 1;
        IsTabStop = state.InMonth;
        IsHitTestVisible = state.InMonth;
        _number.Text = state.Date.Day.ToString(CultureInfo.CurrentCulture);

        _fill.Background = _brushes.DayFill(day.Tags, Palette.EmptyCell);
        _hatch.Visibility = _hatchPast && state.IsPast ? Visibility.Visible : Visibility.Collapsed;
        _frame.Visibility = _frameDash.Visibility = state.IsToday ? Visibility.Visible : Visibility.Collapsed;
        _selection.Visibility = state.IsSelected && !state.IsToday ? Visibility.Visible : Visibility.Collapsed;

        Brush ink = day.HasNotes
            ? new SolidColorBrush(Palette.ForegroundOn(_brushes.ColorOf(day.Tags[0].Tag.Length == 0 ? null : day.Tags[0].Tag)))
            : Palette.InkTertiary;
        _number.Foreground = ink;

        _dots.Children.Clear();
        for (var i = 0; i < Math.Min(day.NoteCount, 5) && day.NoteCount > 1; i++)
        {
            _dots.Children.Add(new Rectangle { Width = 4, Height = 4, Fill = ink, Opacity = 0.8 });
        }

        AutomationProperties.SetName(this, state.AutomationName);
    }
}
