using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Zapisnik.App.Infrastructure;
using Zapisnik.App.Theming;
using Zapisnik.App.Ui;
using Zapisnik.App.ViewModels;
using Zapisnik.Core.Calendar;
using Zapisnik.Core.Tags;

namespace Zapisnik.App.Views;

/// <summary>
/// Full calendar overlay (M6-02..M6-05, SPEC §5.2): virtualised year (or month) grid, legend filter,
/// week "loupe" on hover, keyboard navigation between days.
/// </summary>
internal sealed partial class CalendarView : UserControl
{
    private readonly MainViewModel _vm;
    private readonly Dictionary<DateOnly, CalendarDayCell> _cells = [];
    private readonly ItemsRepeater _months;
    private readonly ScrollViewer _scroller;
    private readonly Canvas _overlay = new();
    private readonly Border _loupe;
    private readonly Grid _loupeDays = new() { ColumnSpacing = 6 };
    private readonly TextBlock _loupeLabel = Typo.Caps(string.Empty, 10.5, Palette.Ink);
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _loupeSwatch = new() { Width = 9, Height = 9, RadiusX = 2, RadiusY = 2 };
    private readonly StackPanel _legend = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly ElementFactory<MonthViewModel, MonthView> _yearFactory;
    private readonly ElementFactory<MonthViewModel, MonthView> _monthFactory;
    private int _loupeGeneration;

    public CalendarView(MainViewModel vm, TagBrushes brushes)
    {
        _vm = vm;
        Brushes = brushes;
        var cal = vm.Calendar;

        _yearFactory = new ElementFactory<MonthViewModel, MonthView>(() => new MonthView(this, large: false));
        _monthFactory = new ElementFactory<MonthViewModel, MonthView>(() => new MonthView(this, large: true));
        _months = new ItemsRepeater { ItemsSource = cal.Months, ItemTemplate = _yearFactory };
        _scroller = new ScrollViewer
        {
            Content = _months,
            Padding = new Thickness(24, 8, 24, 24),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _scroller.ViewChanged += (_, _) => HideLoupe();

        _loupe = BuildLoupe();
        _overlay.Children.Add(_loupe);

        var body = new Grid();
        body.Children.Add(_scroller);
        body.Children.Add(_overlay);

        var root = Ui.Ui.Rows((Ui.Ui.Auto, BuildHeader()), (Ui.Ui.Star, body), (Ui.Ui.Auto, BuildLegend()));
        Content = Ui.Ui.Card(root);

        cal.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CalendarViewModel.Mode))
            {
                ApplyMode();
            }
        };
        cal.Legend.CollectionChanged += (_, _) => RenderLegend();
        brushes.Changed += (_, _) => _ = cal.LoadAsync();
        ApplyMode();

        KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                _vm.IsCalendarOpen = false;
            }
        };
    }

    public TagBrushes Brushes { get; }

    /// <summary>Scrolls the month containing the selected day into view and focuses it.</summary>
    public void FocusDay(DateOnly day)
    {
        var index = _vm.Calendar.Months.ToList().FindIndex(m => m.Year == day.Year && m.Month == day.Month);
        if (index < 0)
        {
            return;
        }

        var element = _months.GetOrCreateElement(index);
        element.UpdateLayout();
        element.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.1, AnimationDesired = false });
        if (_cells.TryGetValue(day, out var cell))
        {
            cell.Focus(FocusState.Programmatic);
        }
    }

    internal void Register(DateOnly date, CalendarDayCell cell) => _cells[date] = cell;

    internal void Unregister(DateOnly date) => _cells.Remove(date);

    internal void InvokeDay(DateOnly date)
    {
        HideLoupe();
        _vm.IsCalendarOpen = false;
        _ = _vm.SelectDayAsync(date);
    }

    internal void OnCellKeyDown(DateOnly date, KeyRoutedEventArgs e)
    {
        var target = e.Key switch
        {
            Windows.System.VirtualKey.Left => date.AddDays(-1),
            Windows.System.VirtualKey.Right => date.AddDays(1),
            Windows.System.VirtualKey.Up => date.AddDays(-7),
            Windows.System.VirtualKey.Down => date.AddDays(7),
            _ => (DateOnly?)null,
        };

        if (e.Key is Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space)
        {
            e.Handled = true;
            InvokeDay(date);
            return;
        }

        if (target is { } t)
        {
            e.Handled = true;
            if (t.Year != _vm.Calendar.Year && _vm.Calendar.Mode == CalendarMode.Year)
            {
                _ = MoveAndFocusAsync(t);
            }
            else if (_cells.TryGetValue(t, out var cell))
            {
                cell.Focus(FocusState.Keyboard);
                cell.StartBringIntoView();
            }
            else
            {
                FocusDay(t);
            }
        }
    }

    private async Task MoveAndFocusAsync(DateOnly day)
    {
        await _vm.Calendar.GoToAsync(day);
        FocusDay(day);
    }

    // ---------------------------------------------------------------- header + legend

    private Grid BuildHeader()
    {
        var cal = _vm.Calendar;
        var title = new TextBlock { FontFamily = Typo.Display, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Palette.Ink, VerticalAlignment = VerticalAlignment.Center };
        title.Bind(cal, nameof(CalendarViewModel.Title), () => title.Text = cal.Title);

        var today = new Button
        {
            Content = Typo.Body(Strings.Get(R.Common_Today), 12.5),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 3, 10, 3),
            Background = Palette.Transparent,
            BorderBrush = Palette.Divider,
            VerticalAlignment = VerticalAlignment.Center,
        };
        today.Click += async (_, _) =>
        {
            await cal.GoToAsync(_vm.Today);
            FocusDay(_vm.Today);
        };

        var year = SegmentButton(Strings.Get(R.Calendar_Year));
        var month = SegmentButton(Strings.Get(R.Calendar_Month));
        year.Click += async (_, _) => await cal.SetModeAsync(CalendarMode.Year);
        month.Click += async (_, _) => await cal.SetModeAsync(CalendarMode.Month);
        var segments = new Border { Child = Ui.Ui.HStack(2, year, month), Background = Palette.SubtleFill, CornerRadius = new CornerRadius(14), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
        segments.Bind(cal, nameof(CalendarViewModel.Mode), () =>
        {
            year.IsChecked = cal.Mode == CalendarMode.Year;
            month.IsChecked = cal.Mode == CalendarMode.Month;
        });

        var grid = Ui.Ui.Columns(
            (Ui.Ui.Auto, title),
            (Ui.Ui.Auto, Ui.Ui.IconButton(Glyphs.ChevronLeft, Strings.Get(R.Calendar_Previous), () => _ = cal.MoveAsync(-1), 12)),
            (Ui.Ui.Auto, Ui.Ui.IconButton(Glyphs.ChevronRight, Strings.Get(R.Calendar_Next), () => _ = cal.MoveAsync(1), 12)),
            (Ui.Ui.Auto, today),
            (Ui.Ui.Star, null),
            (Ui.Ui.Auto, segments),
            (Ui.Ui.Auto, Ui.Ui.IconButton(Glyphs.Close, Strings.Get(R.Calendar_Close), () => _vm.IsCalendarOpen = false, 12)));
        grid.ColumnSpacing = 6;
        grid.Padding = new Thickness(28, 18, 16, 10);
        return grid;
    }

    private ScrollViewer BuildLegend()
    {
        return new ScrollViewer
        {
            Content = _legend,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(24, 10, 24, 16),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = Palette.Divider,
        };
    }

    private void RenderLegend()
    {
        var cal = _vm.Calendar;
        _legend.Children.Clear();
        _legend.Children.Add(Ui.Ui.Pill(Palette.InkTertiary, Strings.Get(R.Calendar_AllTags), null, () =>
        {
            if (cal.FilterTag is { } f)
            {
                cal.ToggleFilter(f);
                RenderLegend();
            }
        }, active: cal.FilterTag is null));

        foreach (var item in cal.Legend)
        {
            var tag = item.Tag;
            _legend.Children.Add(Ui.Ui.Pill(Brushes.BrushOf(tag), tag, item.Count.ToString(CultureInfo.CurrentCulture), () =>
            {
                cal.ToggleFilter(tag);
                RenderLegend();
            }, item.IsActive));
        }
    }

    private void ApplyMode()
    {
        var large = _vm.Calendar.Mode == CalendarMode.Month;
        _cells.Clear();
        _months.ItemTemplate = large ? _monthFactory : _yearFactory;
        _months.Layout = large
            ? new StackLayout()
            : new UniformGridLayout
            {
                MinItemWidth = 400,
                MinItemHeight = 470,
                MinColumnSpacing = 28,
                MinRowSpacing = 8,
                ItemsStretch = UniformGridLayoutItemsStretch.Fill,
                ItemsJustification = UniformGridLayoutItemsJustification.Start,
            };
        HideLoupe();
    }

    private static ToggleButton SegmentButton(string text) => new()
    {
        Content = Typo.Body(text, 12.5),
        Padding = new Thickness(12, 3, 12, 3),
        MinHeight = 26,
        CornerRadius = new CornerRadius(12),
        BorderThickness = new Thickness(0),
    };

    // ---------------------------------------------------------------- loupe (M6-03)

    private Border BuildLoupe()
    {
        for (var i = 0; i < 9; i++)
        {
            _loupeDays.ColumnDefinitions.Add(new ColumnDefinition { Width = i is 0 or 8 ? GridLength.Auto : new GridLength(1, GridUnitType.Star) });
        }

        var label = Ui.Ui.HStack(6, _loupeSwatch, _loupeLabel);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.Margin = new Thickness(0, 6, 34, 0);

        var border = new Border
        {
            Child = Ui.Ui.VStack(0, _loupeDays, label),
            Background = Palette.Card,
            BorderBrush = Palette.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 10, 10, 10),
            Visibility = Visibility.Collapsed,
            Translation = new System.Numerics.Vector3(0, 0, 32),
            Shadow = new ThemeShadow(),
        };
        border.PointerExited += (_, _) => HideLoupe();
        return border;
    }

    internal void ShowLoupe(FrameworkElement row, WeekRowViewModel week)
    {
        var generation = ++_loupeGeneration;
        Rect bounds;
        try
        {
            bounds = row.TransformToVisual(_overlay).TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight));
        }
        catch (ArgumentException)
        {
            return;
        }

        _loupeDays.Children.Clear();
        AddBracket("[", 0);
        for (var i = 0; i < 7; i++)
        {
            var cellVm = week.Days[i];
            var cell = new CalendarDayCell(Brushes, large: false) { Height = 62, NumberFontSize = 26 };
            cell.Bind(cellVm);
            cell.Visibility = Visibility.Visible;
            cell.Opacity = cellVm.InMonth ? 1 : 0.35;

            var date = cellVm.Date;
            cell.Tapped += (_, e) =>
            {
                e.Handled = true;
                InvokeDay(date);
            };
            Grid.SetColumn(cell, i + 1);
            _loupeDays.Children.Add(cell);
        }

        AddBracket("]", 8);
        _loupeLabel.Text = string.Empty;
        _loupeSwatch.Fill = Palette.Transparent;

        const double Grow = 28;
        _loupe.Width = bounds.Width + (2 * Grow) - 30;
        Canvas.SetLeft(_loupe, Math.Max(0, bounds.X + 30 - Grow));
        Canvas.SetTop(_loupe, Math.Max(0, bounds.Y - 22));
        _loupe.Visibility = Visibility.Visible;

        _ = LoadHighlightAsync(week.Week.Monday, generation);

        void AddBracket(string text, int column)
        {
            var bracket = new TextBlock
            {
                Text = text,
                FontFamily = Typo.Display,
                FontSize = 44,
                FontWeight = FontWeights.Light,
                Foreground = Palette.Ink,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -8, 0, 0),
            };
            Grid.SetColumn(bracket, column);
            _loupeDays.Children.Add(bracket);
        }
    }

    private async Task LoadHighlightAsync(DateOnly monday, int generation)
    {
        var highlight = await _vm.Calendar.GetHighlightAsync(monday);
        if (generation != _loupeGeneration || highlight is null)
        {
            return;
        }

        _loupeSwatch.Fill = Brushes.BrushOf(highlight.Tag);
        var text = highlight.Title is { Length: > 0 } t ? t : highlight.Tag;
        _loupeLabel.Text = text.ToUpper(CultureInfo.CurrentCulture) + "  ·  " + Strings.Notes(highlight.Count).ToUpper(CultureInfo.CurrentCulture);
    }

    private void HideLoupe()
    {
        _loupeGeneration++;
        _loupe.Visibility = Visibility.Collapsed;
    }
}
