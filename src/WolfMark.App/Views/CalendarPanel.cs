using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;
using WolfMark.Core.Calendar;
using VirtualKey = Windows.System.VirtualKey;

namespace WolfMark.App.Views;

/// <summary>
/// Calendar panel at the top of the window (M6-01..M6-05, ADR 0004). Collapsed it is the bracketed week
/// band (<c>expected_calendar.png</c>); expanded it becomes the month grid (<c>full_calendar.png</c>) in
/// place – no overlay. All controls sit left of the title so their position never depends on its length.
/// </summary>
internal sealed partial class CalendarPanel : UserControl
{
    private const double WeekColumn = 46;

    private readonly MainViewModel _vm;
    private readonly TagBrushes _brushes;
    private readonly Grid _band;
    private readonly CalendarDayCell[] _bandCells = new CalendarDayCell[7];
    private readonly Grid _bandLabel = new();
    private readonly Grid _month = new() { RowSpacing = 0 };
    private readonly Dictionary<DateOnly, CalendarDayCell> _monthCells = [];
    private readonly List<(DayCellViewModel Vm, System.ComponentModel.PropertyChangedEventHandler Handler, CalendarDayCell Cell)> _monthBindings = [];
    private readonly TextBlock _title;
    private readonly Button _expand;

    public CalendarPanel(MainViewModel vm, TagBrushes brushes)
    {
        _vm = vm;
        _brushes = brushes;

        // Header: [◀][▶][TODAY] TITLE ………… [expand] – controls never move.
        _title = new TextBlock { FontFamily = Typo.CondensedBold, FontSize = 21, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center, CharacterSpacing = 20 };
        _expand = Ui.Ui.IconButton(Glyphs.Expand, Strings.Get(R.Calendar_Expand), () => _ = vm.ToggleCalendarAsync(), 13);
        var header = Ui.Ui.Columns(
            (Ui.Ui.Px(36), Ui.Ui.IconButton(Glyphs.ChevronLeft, Strings.Get(R.Calendar_Previous), () => _ = vm.MoveCalendarAsync(-1), 11)),
            (Ui.Ui.Px(36), Ui.Ui.IconButton(Glyphs.ChevronRight, Strings.Get(R.Calendar_Next), () => _ = vm.MoveCalendarAsync(1), 11)),
            (Ui.Ui.Px(76), Ui.Ui.TagButton(Strings.Get(R.Common_Today), () => _ = vm.SelectDayAsync(vm.Today))),
            (Ui.Ui.Star, _title),
            (Ui.Ui.Px(36), _expand));
        header.ColumnSpacing = 4;
        header.Margin = new Thickness(0, 0, 0, 8);
        ((FrameworkElement)header.Children[3]).Margin = new Thickness(10, 0, 0, 0);

        _band = BuildBand();
        Content = Ui.Ui.Rows((Ui.Ui.Auto, header), (Ui.Ui.Auto, _band), (Ui.Ui.Auto, _month));

        vm.Week.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(WeekStripViewModel.LabelText) or nameof(WeekStripViewModel.LabelTag) or nameof(WeekStripViewModel.LabelDayIndex))
            {
                RenderBandLabel();
            }

            UpdateTitle();
        };
        foreach (var day in vm.Week.Days)
        {
            day.PropertyChanged += (_, _) => RenderBand();
        }

        vm.Calendar.Rebuilt += (_, _) => RenderMonth();
        vm.Calendar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CalendarViewModel.Title))
            {
                UpdateTitle();
            }
        };
        vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsCalendarExpanded):
                    ApplyExpanded();
                    break;
                case nameof(MainViewModel.SelectedDay):
                    UpdateMonthCells();
                    break;
                case nameof(MainViewModel.ActiveTag):
                    RenderBand();
                    break;
            }
        };
        brushes.Changed += (_, _) =>
        {
            RenderBand();
            RenderMonth();
        };

        ApplyExpanded();
        RenderBand();
    }

    /// <summary>Moves keyboard focus into the visible calendar (Ctrl+Shift+C).</summary>
    public void FocusSelectedDay()
    {
        if (_vm.IsCalendarExpanded && _monthCells.TryGetValue(_vm.SelectedDay, out var cell))
        {
            cell.Focus(FocusState.Keyboard);
        }
        else
        {
            _bandCells.FirstOrDefault(c => c.Date == _vm.SelectedDay)?.Focus(FocusState.Keyboard);
        }
    }

    private void ApplyExpanded()
    {
        var expanded = _vm.IsCalendarExpanded;
        _band.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        _month.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ((FontIcon)_expand.Content).Glyph = expanded ? Glyphs.Collapse : Glyphs.Expand;
        var tip = Strings.Get(expanded ? R.Calendar_Collapse : R.Calendar_Expand);
        ToolTipService.SetToolTip(_expand, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_expand, tip);
        UpdateTitle();
        RenderMonth();
    }

    private void UpdateTitle() =>
        _title.Text = _vm.IsCalendarExpanded ? _vm.Calendar.Title : _vm.Week.WeekLabel.ToUpper(CultureInfo.CurrentCulture);

    // ---------------------------------------------------------------- week band

    private Grid BuildBand()
    {
        var cells = new Grid();
        for (var i = 0; i < 7; i++)
        {
            cells.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var cell = new CalendarDayCell(_brushes, height: 64, numberSize: 30, hatchPast: true);
            var index = i;
            cell.Tapped += (_, e) =>
            {
                e.Handled = true;
                _ = _vm.SelectDayAsync(_vm.Week.Days[index].Date);
            };
            cell.KeyDown += (_, e) => OnBandKey(index, e);
            Grid.SetColumn(cell, i);
            cells.Children.Add(cell);
            _bandCells[i] = cell;
        }

        for (var i = 0; i < 7; i++)
        {
            _bandLabel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        _bandLabel.Margin = new Thickness(0, 6, 0, 0);
        _bandLabel.MinHeight = 18;

        var grid = Ui.Ui.Columns(
            (Ui.Ui.Px(14), Bracket(left: true)),
            (Ui.Ui.Star, Ui.Ui.Rows((Ui.Ui.Auto, cells), (Ui.Ui.Auto, _bandLabel))),
            (Ui.Ui.Px(14), Bracket(left: false)));
        grid.ColumnSpacing = 6;
        return grid;
    }

    /// <summary>Thin black square bracket framing the band.</summary>
    private static Border Bracket(bool left) => new()
    {
        BorderBrush = Palette.Ink,
        BorderThickness = left ? new Thickness(3, 3, 0, 3) : new Thickness(0, 3, 3, 3),
        Height = 76,
        Width = 12,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, -6, 0, 0),
        HorizontalAlignment = left ? HorizontalAlignment.Right : HorizontalAlignment.Left,
    };

    private void RenderBand()
    {
        for (var i = 0; i < 7; i++)
        {
            var d = _vm.Week.Days[i];
            _bandCells[i].Update(new DayCellState(d.Date, d.Day, d.IsToday, d.IsPast, d.IsSelected, InMonth: true,
                IsDimmed: _vm.ActiveTag is { } tag && !d.Day.Tags.Any(t => Core.Tags.TagName.Comparer.Equals(t.Tag, tag)),
                d.AutomationName));
        }
    }

    private void RenderBandLabel()
    {
        _bandLabel.Children.Clear();
        if (_vm.Week.LabelText is not { Length: > 0 } text)
        {
            return;
        }

        var label = LabelLine(_vm.Week.LabelTag, text);
        Grid.SetColumn(label, Math.Clamp(_vm.Week.LabelDayIndex, 0, 6));
        Grid.SetColumnSpan(label, 7 - Math.Clamp(_vm.Week.LabelDayIndex, 0, 6));
        _bandLabel.Children.Add(label);
    }

    /// <summary>"■ FINAL POLISH ————" – coloured square, condensed caps, hairline to the end.</summary>
    private Grid LabelLine(string? tag, string text)
    {
        var caps = Typo.Caps(text, 12.5, Palette.InkSecondary);
        caps.MaxWidth = 260;
        caps.TextTrimming = TextTrimming.CharacterEllipsis;
        var line = new Border { Height = 1, Background = Palette.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 0, 0) };
        var grid = Ui.Ui.Columns(
            (Ui.Ui.Auto, new Rectangle { Width = 8, Height = 8, Fill = _brushes.BrushOf(tag), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) }),
            (Ui.Ui.Auto, caps),
            (Ui.Ui.Star, line));
        return grid;
    }

    private void OnBandKey(int index, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left when index > 0:
                _bandCells[index - 1].Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
            case VirtualKey.Right when index < 6:
                _bandCells[index + 1].Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
            case VirtualKey.Enter or VirtualKey.Space:
                _ = _vm.SelectDayAsync(_vm.Week.Days[index].Date);
                e.Handled = true;
                break;
        }
    }

    // ---------------------------------------------------------------- month grid

    private void RenderMonth()
    {
        if (!_vm.IsCalendarExpanded)
        {
            return;
        }

        var cal = _vm.Calendar;
        foreach (var (vmCell, handler, _) in _monthBindings)
        {
            vmCell.PropertyChanged -= handler;
        }

        _monthBindings.Clear();
        _month.Children.Clear();
        _month.RowDefinitions.Clear();
        _month.ColumnDefinitions.Clear();
        _monthCells.Clear();
        _month.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WeekColumn) });
        for (var i = 0; i < 7; i++)
        {
            _month.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var row = 0;
        AddRow(GridLength.Auto);
        var names = CalendarMath.WeekdayNames(CultureInfo.CurrentCulture, abbreviated: true);
        for (var i = 0; i < 7; i++)
        {
            var bubble = WeekdayBubble(names[i].TrimEnd('.'), i == cal.TodayWeekday);
            Place(bubble, row, i + 1);
        }

        foreach (var week in cal.Weeks)
        {
            AddRow(GridLength.Auto);
            Place(WeekTag(week.Week.WeekNumber, week.IsCurrentWeek), row, 0);
            for (var i = 0; i < 7; i++)
            {
                var vmCell = week.Days[i];
                var cell = new CalendarDayCell(_brushes, height: 46, numberSize: 19, hatchPast: false);
                cell.Update(State(vmCell));
                System.ComponentModel.PropertyChangedEventHandler handler = (_, _) => cell.Update(State(vmCell));
                vmCell.PropertyChanged += handler;
                _monthBindings.Add((vmCell, handler, cell));
                var date = vmCell.Date;
                cell.Tapped += (_, e) =>
                {
                    e.Handled = true;
                    _ = _vm.SelectDayAsync(date);
                };
                cell.KeyDown += (_, e) => OnMonthKey(date, e);
                Place(cell, row, i + 1);
                if (vmCell.InMonth)
                {
                    _monthCells[date] = cell;
                }
            }

            // Labels between weeks ("■ REFRAME HANDOFF ———").
            AddRow(new GridLength(week.Labels.Count > 0 ? 22 : 4));
            if (week.Labels.Count > 0)
            {
                var first = week.Labels[0];
                var column = first.Day.DayNumber - week.Week.Monday.DayNumber;
                var label = LabelLine(first.Tag, first.Title);
                label.Margin = new Thickness(0, 3, 0, 3);
                Place(label, row, column + 1, 7 - column);
            }
        }

        void AddRow(GridLength height)
        {
            _month.RowDefinitions.Add(new RowDefinition { Height = height });
            row = _month.RowDefinitions.Count - 1;
        }

        void Place(FrameworkElement element, int r, int c, int span = 1)
        {
            Grid.SetRow(element, r);
            Grid.SetColumn(element, c);
            Grid.SetColumnSpan(element, span);
            _month.Children.Add(element);
        }
    }

    private void UpdateMonthCells()
    {
        foreach (var (vmCell, _, cell) in _monthBindings)
        {
            cell.Update(State(vmCell));
        }
    }

    private DayCellState State(DayCellViewModel c) =>
        new(c.Date, c.Day, c.IsToday, c.IsPast, c.Date == _vm.SelectedDay, c.InMonth, c.IsDimmed, c.AutomationName);

    /// <summary>Grey speech-bubble weekday label; today's weekday in ink.</summary>
    private static StackPanel WeekdayBubble(string text, bool isToday)
    {
        var fill = isToday ? Palette.TodayFill : Palette.LabelGrey;
        var label = Typo.Caps(text, 11.5, isToday ? Palette.TodayInk : Palette.LabelInk);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var box = new Border { Child = label, Background = fill, Padding = new Thickness(0, 1, 0, 1), Margin = new Thickness(5, 0, 5, 0), CornerRadius = new CornerRadius(2) };
        var tail = new Polygon
        {
            Points = { new Point(0, 0), new Point(8, 0), new Point(4, 4) },
            Fill = fill,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var panel = Ui.Ui.VStack(0, box, tail);
        panel.Margin = new Thickness(0, 0, 0, 4);
        return panel;
    }

    /// <summary>Week number in a grey tag pointing at its row.</summary>
    private static Grid WeekTag(int number, bool current)
    {
        var fill = current ? Palette.TodayFill : Palette.LabelGrey;
        var text = Typo.Caps(number.ToString(CultureInfo.CurrentCulture), 12, current ? Palette.TodayInk : Palette.LabelInk);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        var grid = Ui.Ui.Columns(
            (Ui.Ui.Px(28), new Border { Child = text, Background = fill, Height = 20 }),
            (Ui.Ui.Px(8), new Polygon { Points = { new Point(0, 0), new Point(7, 10), new Point(0, 20) }, Fill = fill, VerticalAlignment = VerticalAlignment.Center }));
        grid.VerticalAlignment = VerticalAlignment.Center;
        grid.HorizontalAlignment = HorizontalAlignment.Left;
        return grid;
    }

    private void OnMonthKey(DateOnly date, KeyRoutedEventArgs e)
    {
        DateOnly? target = e.Key switch
        {
            VirtualKey.Left => date.AddDays(-1),
            VirtualKey.Right => date.AddDays(1),
            VirtualKey.Up => date.AddDays(-7),
            VirtualKey.Down => date.AddDays(7),
            _ => null,
        };

        if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            e.Handled = true;
            _ = _vm.SelectDayAsync(date);
            return;
        }

        if (target is not { } t)
        {
            return;
        }

        e.Handled = true;
        if (_monthCells.TryGetValue(t, out var cell))
        {
            cell.Focus(FocusState.Keyboard);
        }
        else
        {
            _ = MoveAndFocusAsync(t);
        }
    }

    private async Task MoveAndFocusAsync(DateOnly day)
    {
        await _vm.Calendar.GoToAsync(day);
        if (_monthCells.TryGetValue(day, out var cell))
        {
            cell.Focus(FocusState.Keyboard);
        }
    }
}
