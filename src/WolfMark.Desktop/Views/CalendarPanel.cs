using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Calendar;
using WolfMark.Core.Tags;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// Calendar, drawn like src/WolfMark.App/Views/CalendarPanel.cs (ADR 0004): header [‹][›][TODAY] TITLE [expand];
/// the week band in brackets with the week's tag counts below; expanded (Ctrl+Shift+C) the month with weekday
/// bubbles, week-number tags and labels between weeks.
/// </summary>
internal sealed class CalendarPanel : UserControl
{
    private const double WeekColumn = 46;

    private readonly MainViewModel _vm;
    private readonly DayCell[] _bandCells = new DayCell[7];
    private readonly Grid _bandLabel = new() { Margin = new Thickness(0, 6, 0, 0), MinHeight = 18 };
    private readonly Control _band;
    private readonly Grid _month = new();
    private readonly TextBlock _title = new() { FontFamily = Typo.CondensedBold, FontSize = 21, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _expand;

    public CalendarPanel(MainViewModel vm)
    {
        _vm = vm;
        _title.Foreground = Palette.Ink;
        _expand = Ui.IconButton(Icons.Create(Icons.Expand, 14, Palette.InkTertiary), Strings.Get(R.Calendar_Expand), () => _ = vm.ToggleCalendarAsync());
        var today = Ui.TagButton(Strings.Get(R.Common_Today), () => _ = vm.GoToTodayAsync());
        today.Width = 68;
        var header = Ui.Columns(
            (Ui.Px(36), Ui.IconButton(Icons.Create(Icons.ChevronLeft, 16, Palette.InkTertiary), Strings.Get(R.Calendar_Previous), () => _ = vm.MoveCalendarAsync(-1))),
            (Ui.Px(36), Ui.IconButton(Icons.Create(Icons.ChevronRight, 16, Palette.InkTertiary), Strings.Get(R.Calendar_Next), () => _ = vm.MoveCalendarAsync(1))),
            (Ui.Px(76), today),
            (Ui.Star, _title),
            (Ui.Px(36), _expand));
        header.ColumnSpacing = 4;
        header.Margin = new Thickness(0, 0, 0, 8);
        _title.Margin = new Thickness(36, 0, 0, 0);

        _band = BuildBand();
        Content = Ui.VStack(0, header, _band, _month);

        vm.Week.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WeekStripViewModel.Topics))
            {
                RenderBandLabel();
            }

            UpdateTitle();
        };
        vm.Week.Days.CollectionChanged += (_, _) => WatchDays();
        WatchDays();
        vm.Calendar.Rebuilt += (_, _) => RenderMonth();
        vm.Calendar.PropertyChanged += (_, _) => UpdateTitle();
        vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsCalendarExpanded):
                    ApplyExpanded();
                    break;
                case nameof(MainViewModel.SelectedDay):
                    RenderMonth();
                    break;
                case nameof(MainViewModel.ActiveTag):
                    RenderBand();
                    RenderMonth();
                    break;
            }
        };
        Palette.Changed += (_, _) =>
        {
            RenderBand();
            RenderBandLabel();
            RenderMonth();
        };
        vm.Host.Tags.Changed += (_, _) => UiThread.Post(() =>
        {
            RenderBand();
            RenderBandLabel();
            RenderMonth();
        });
        ApplyExpanded();
        RenderBand();
    }

    private void WatchDays()
    {
        foreach (var day in _vm.Week.Days)
        {
            day.PropertyChanged -= OnDayChanged;
            day.PropertyChanged += OnDayChanged;
        }

        RenderBand();
    }

    private void OnDayChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RenderBand();

    private void ApplyExpanded()
    {
        var expanded = _vm.IsCalendarExpanded;
        _band.IsVisible = !expanded;
        _month.IsVisible = expanded;
        _expand.Content = Icons.Create(expanded ? Icons.Collapse : Icons.Expand, 14, Palette.InkTertiary);
        ToolTip.SetTip(_expand, Strings.Get(expanded ? R.Calendar_Collapse : R.Calendar_Expand));
        UpdateTitle();
        RenderMonth();
    }

    private void UpdateTitle() =>
        _title.Text = (_vm.IsCalendarExpanded ? _vm.Calendar.Title : _vm.Week.WeekLabel).ToUpper(CultureInfo.CurrentCulture);

    // ---------------------------------------------------------------- week band

    private Grid BuildBand()
    {
        var cells = new Grid();
        for (var i = 0; i < 7; i++)
        {
            cells.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var cell = new DayCell(_vm, height: 64, numberSize: 30, weekBand: true);
            var index = i;
            cell.Clicked = () => _ = _vm.SelectDayAsync(_vm.Week.Days[index].Date);
            Grid.SetColumn(cell, i);
            cells.Children.Add(cell);
            _bandCells[i] = cell;
        }

        var inner = Ui.VStack(0, cells, _bandLabel);
        var grid = Ui.Columns((Ui.Px(14), Bracket(left: true)), (Ui.Star, inner), (Ui.Px(14), Bracket(left: false)));
        grid.ColumnSpacing = 6;
        return grid;
    }

    /// <summary>Thick square bracket framing the band.</summary>
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
        for (var i = 0; i < 7 && i < _vm.Week.Days.Count; i++)
        {
            var d = _vm.Week.Days[i];
            _bandCells[i].Update(d.Date, d.Day, d.IsToday, d.IsPast, d.IsSelected, inMonth: true, Dimmed(d.Day), d.AutomationName);
        }
    }

    private bool Dimmed(CalendarDay day) => _vm.ActiveTag is { } tag && !day.Tags.Any(t => TagName.Comparer.Equals(t.Tag, tag));

    /// <summary>"■ WORK 3  ■ WOLFMARK 5 ————": the week's counts; what does not fit is clipped.</summary>
    private void RenderBandLabel()
    {
        _bandLabel.Children.Clear();
        var topics = _vm.Week.Topics;
        if (topics.Count == 0)
        {
            return;
        }

        var items = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        foreach (var topic in topics)
        {
            var name = topic.Name.Length > 0 ? topic.Name : Strings.Get(R.Week_NoTag);
            var caps = Typo.Caps(name.ToUpper(CultureInfo.CurrentCulture), 12.5, Palette.InkSecondary);
            caps.MaxWidth = 180;
            caps.TextTrimming = TextTrimming.CharacterEllipsis;
            var count = Typo.Caps(topic.Count.ToString(CultureInfo.CurrentCulture), 12.5, Palette.Ink);
            count.Margin = new Thickness(5, 0, 0, 0);
            var item = Ui.HStack(0, Square(topic.Tag), caps, count);
            ToolTip.SetTip(item, $"{name}: {topic.Count}");
            items.Children.Add(item);
        }

        var line = new Border { Height = 1, MinWidth = 16, Background = Palette.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 1, 0, 0) };
        _bandLabel.Children.Add(Ui.Columns((Ui.Auto, items), (Ui.Star, line)));
        _bandLabel.ClipToBounds = true;
    }

    private Rectangle Square(string? tag) => new()
    {
        Width = 8,
        Height = 8,
        Fill = new SolidColorBrush(Palette.ToColor(_vm.Host.Tags.GetColor(tag))),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0),
    };

    // ---------------------------------------------------------------- month grid

    private void RenderMonth()
    {
        _month.Children.Clear();
        _month.RowDefinitions.Clear();
        _month.ColumnDefinitions.Clear();
        if (!_vm.IsCalendarExpanded)
        {
            return;
        }

        var cal = _vm.Calendar;
        _month.ColumnDefinitions.Add(new ColumnDefinition(WeekColumn, GridUnitType.Pixel));
        for (var i = 0; i < 7; i++)
        {
            _month.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        }

        var row = 0;
        AddRow(GridLength.Auto);
        var names = CalendarMath.WeekdayNames(CultureInfo.CurrentCulture, abbreviated: true);
        for (var i = 0; i < 7; i++)
        {
            Place(WeekdayBubble(names[i].TrimEnd('.').ToUpper(CultureInfo.CurrentCulture), i == cal.TodayWeekday), row, i + 1);
        }

        foreach (var week in cal.Weeks)
        {
            AddRow(GridLength.Auto);
            Place(WeekTag(week.Week.WeekNumber, week.IsCurrentWeek), row, 0);
            for (var i = 0; i < 7; i++)
            {
                var d = week.Days[i];
                var cell = new DayCell(_vm, height: 58, numberSize: 22, weekBand: false) { Margin = new Thickness(0, 0, 0, 5) };
                cell.Update(d.Date, d.Day, d.IsToday, d.IsPast, d.Date == _vm.SelectedDay, d.InMonth, d.IsDimmed, d.AutomationName);
                var date = d.Date;
                cell.Clicked = () => _ = _vm.SelectDayAsync(date);
                Place(cell, row, i + 1);
            }

            // Labels between weeks ("■ V1.0 RELEASE ———").
            AddRow(new GridLength(week.Labels.Count > 0 ? 22 : 0, GridUnitType.Pixel));
            if (week.Labels.Count > 0)
            {
                var first = week.Labels[0];
                var column = first.Day.DayNumber - week.Week.Monday.DayNumber;
                var label = LabelLine(first.Tag, first.Title.ToUpper(CultureInfo.CurrentCulture));
                label.Margin = new Thickness(0, 0, 0, 5);
                Place(label, row, column + 1, 7 - column);
            }
        }

        void AddRow(GridLength height)
        {
            _month.RowDefinitions.Add(new RowDefinition(height));
            row = _month.RowDefinitions.Count - 1;
        }

        void Place(Control element, int r, int c, int span = 1)
        {
            Grid.SetRow(element, r);
            Grid.SetColumn(element, c);
            Grid.SetColumnSpan(element, span);
            _month.Children.Add(element);
        }
    }

    /// <summary>"■ FINAL POLISH ————" – coloured square, condensed caps, hairline to the end.</summary>
    private Grid LabelLine(string? tag, string text)
    {
        var caps = Typo.Caps(text, 12.5, Palette.InkSecondary);
        caps.MaxWidth = 260;
        caps.TextTrimming = TextTrimming.CharacterEllipsis;
        caps.VerticalAlignment = VerticalAlignment.Center;
        var line = new Border { Height = 1, Background = Palette.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 0, 0) };
        return Ui.Columns((Ui.Auto, Square(tag)), (Ui.Auto, caps), (Ui.Star, line));
    }

    /// <summary>Grey speech-bubble weekday label; today's weekday in ink.</summary>
    private static StackPanel WeekdayBubble(string text, bool isToday)
    {
        var fill = isToday ? Palette.TodayFill : Palette.LabelGrey;
        var label = Typo.Caps(text, 11.5, isToday ? Palette.TodayInk : Palette.LabelInk);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var box = new Border { Child = label, Background = fill, Padding = new Thickness(0, 1), Margin = new Thickness(5, 0), CornerRadius = new CornerRadius(2) };
        var tail = new Polygon
        {
            Points = [new Point(0, 0), new Point(8, 0), new Point(4, 4)],
            Fill = fill,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var panel = Ui.VStack(0, box, tail);
        panel.Margin = new Thickness(0, 0, 0, 6);
        return panel;
    }

    /// <summary>Week number in a grey tag pointing at its row.</summary>
    private static Grid WeekTag(int number, bool current)
    {
        var fill = current ? Palette.TodayFill : Palette.LabelGrey;
        var text = Typo.Caps(number.ToString(CultureInfo.CurrentCulture), 12, current ? Palette.TodayInk : Palette.LabelInk);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        var grid = Ui.Columns(
            (Ui.Px(28), new Border { Child = text, Background = fill, Height = 20 }),
            (Ui.Px(8), new Polygon { Points = [new Point(0, 0), new Point(7, 10), new Point(0, 20)], Fill = fill, VerticalAlignment = VerticalAlignment.Center }));
        grid.VerticalAlignment = VerticalAlignment.Center;
        grid.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Margin = new Thickness(0, 0, 0, 5);
        return grid;
    }

    /// <summary>
    /// Flat, square day cell (the Windows CalendarDayCell): tag colours as diagonal bands, heavy numerals,
    /// note-count dots, hatched past days and softer tag colours in the week band (the month grid: full colours), two-tone dashed frame for today.
    /// </summary>
    private sealed class DayCell : Border
    {
        private readonly MainViewModel _vm;
        private readonly bool _weekBand; // hatched past days and softer colours (the month grid: full colours)
        private readonly Border _fill = new();
        private readonly Border _hatch = new() { Background = Palette.PastHatch, IsHitTestVisible = false };
        private readonly Rectangle _frame = new() { StrokeThickness = 3, Stroke = Palette.TodayFrame, IsHitTestVisible = false };
        private readonly Rectangle _frameDash = new() { StrokeThickness = 3, Stroke = Palette.TodayFrameDash, StrokeDashArray = [2, 2], IsHitTestVisible = false };
        private readonly Border _selection = new() { BorderBrush = Palette.Ink, BorderThickness = new Thickness(0, 0, 0, 3), IsHitTestVisible = false };
        private readonly TextBlock _number = new() { FontFamily = Typo.DayNumber, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };

        public DayCell(MainViewModel vm, double height, double numberSize, bool weekBand)
        {
            _vm = vm;
            _weekBand = weekBand;
            _number.FontSize = numberSize;
            _number.Margin = new Thickness(0, 0, 0, height * 0.12);
            _dots.Margin = new Thickness(0, 0, 0, height * 0.12);
            var grid = new Grid();
            grid.Children.AddRange([_fill, _hatch, _selection, _frame, _frameDash, _number, _dots]);
            Child = grid;
            Height = height;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Background = Palette.Transparent;
            Cursor = new Cursor(StandardCursorType.Hand);
            PointerEntered += (_, _) => _fill.Opacity = 0.85;
            PointerExited += (_, _) => _fill.Opacity = 1;
            PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton == MouseButton.Left)
                {
                    e.Handled = true;
                    Clicked?.Invoke();
                }
            };
        }

        public Action? Clicked { get; set; }

        public void Update(DateOnly date, CalendarDay day, bool today, bool past, bool selected, bool inMonth, bool dimmed, string tip)
        {
            Opacity = !inMonth ? 0 : dimmed ? 0.2 : 1;
            IsHitTestVisible = inMonth;
            _number.Text = date.Day.ToString(CultureInfo.CurrentCulture);
            _fill.Background = DayFill(day.Tags);
            var hatched = _weekBand && past;
            _hatch.IsVisible = hatched;
            _frame.IsVisible = _frameDash.IsVisible = today;
            _selection.IsVisible = selected && !today;

            IBrush ink;
            if (day.HasNotes)
            {
                // Hatched past days: a shade of the first tag (Palette.PastInk, as on Windows); full fills black/white by the tint.
                var tag = _vm.Host.Tags.GetColor(day.Tags[0].Tag.Length == 0 ? null : day.Tags[0].Tag);
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

            ToolTip.SetTip(this, tip);
        }

        /// <summary>Tag colours as hard diagonal bands, widths by note count (up to four tags).</summary>
        private IBrush DayFill(IReadOnlyList<TagCount> tags)
        {
            if (tags.Count == 0)
            {
                return Palette.EmptyCell;
            }

            // The week band is softer than the tag colour elsewhere (Palette.CalendarTint), as on Windows.
            Color ColorOf(TagCount t) => _weekBand
                ? Palette.CalendarTint(_vm.Host.Tags.GetColor(t.Tag.Length == 0 ? null : t.Tag))
                : Palette.ToColor(_vm.Host.Tags.GetColor(t.Tag.Length == 0 ? null : t.Tag));
            if (tags.Count == 1)
            {
                return new SolidColorBrush(ColorOf(tags[0]));
            }

            var total = (double)tags.Sum(t => t.Count);
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            };
            var offset = 0.0;
            foreach (var t in tags.Take(4))
            {
                var next = offset + (t.Count / total);
                brush.GradientStops.Add(new GradientStop(ColorOf(t), offset));
                brush.GradientStops.Add(new GradientStop(ColorOf(t), next));
                offset = next;
            }

            return brush;
        }
    }
}
