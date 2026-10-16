using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;
using WolfMark.Core.Calendar;

namespace WolfMark.App.Views;

/// <summary>
/// One day of the full calendar (SPEC §5.2): rounded cell filled with tag colours (diagonal bands for
/// several tags), count dots, hatched "today". Focusable and announced by Narrator (M6-05).
/// </summary>
internal sealed partial class CalendarDayCell : ContentControl
{
    private readonly Border _fill = new() { CornerRadius = new CornerRadius(8) };
    private readonly TextBlock _number = new()
    {
        FontFamily = Typo.Display,
        FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) };
    private readonly StackPanel _titles = new() { Spacing = 1, Margin = new Thickness(6, 26, 6, 4) };
    private readonly TagBrushes _brushes;
    private readonly bool _large;
    private DayCellViewModel? _vm;

    public CalendarDayCell(TagBrushes brushes, bool large)
    {
        _brushes = brushes;
        _large = large;
        _number.FontSize = large ? 15 : 14;
        if (large)
        {
            _number.HorizontalAlignment = HorizontalAlignment.Left;
            _number.VerticalAlignment = VerticalAlignment.Top;
            _number.Margin = new Thickness(8, 5, 0, 0);
        }

        var grid = new Grid();
        grid.Children.Add(_fill);
        grid.Children.Add(_number);
        grid.Children.Add(large ? _titles : _dots);

        Content = grid;
        Height = large ? 104 : 42;
        Margin = new Thickness(2.5);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        CornerRadius = new CornerRadius(8);

        PointerEntered += (_, _) => _fill.Opacity = 0.85;
        PointerExited += (_, _) => _fill.Opacity = 1;
    }

    public DayCellViewModel? ViewModel => _vm;

    public double NumberFontSize
    {
        get => _number.FontSize;
        set => _number.FontSize = value;
    }

    public void Bind(DayCellViewModel vm)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnChanged;
        }

        _vm = vm;
        vm.PropertyChanged += OnChanged;
        Update();
    }

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Update();

    public void Update()
    {
        if (_vm is not { } vm)
        {
            return;
        }

        Visibility = vm.InMonth ? Visibility.Visible : Visibility.Collapsed;
        IsTabStop = vm.InMonth;
        _number.Text = vm.Date.Day.ToString(CultureInfo.CurrentCulture);
        var day = vm.Day;

        if (vm.IsToday)
        {
            _fill.Background = TagBrushes.Hatch(Palette.TodayFill.Color, Blend(Palette.TodayFill.Color, Palette.TodayInk.Color, 0.14));
            _fill.BorderBrush = day.Tags.Count > 0 ? _brushes.BrushOf(day.Tags[0].Tag.Length == 0 ? null : day.Tags[0].Tag) : Palette.Accent;
            _fill.BorderThickness = new Thickness(2.5);
            _number.Foreground = Palette.TodayInk;
        }
        else
        {
            _fill.Background = _brushes.DayFill(day.Tags, Palette.EmptyCell);
            _fill.BorderThickness = new Thickness(0);
            _number.Foreground = day.HasNotes
                ? new SolidColorBrush(Palette.ForegroundOn(_brushes.ColorOf(day.Tags[0].Tag.Length == 0 ? null : day.Tags[0].Tag)))
                : vm.IsWeekend ? Palette.InkTertiary : Palette.InkSecondary;
        }

        Opacity = vm.IsDimmed ? 0.18 : 1;

        if (_large)
        {
            _titles.Children.Clear();
            foreach (var t in vm.Titles)
            {
                var line = Typo.Body(t.Title, 11, _number.Foreground);
                line.TextTrimming = TextTrimming.CharacterEllipsis;
                _titles.Children.Add(line);
            }
        }
        else
        {
            _dots.Children.Clear();
            var dotBrush = _number.Foreground;
            for (var i = 0; i < Math.Min(day.NoteCount, 3) && day.NoteCount > 1; i++)
            {
                _dots.Children.Add(new Ellipse { Width = 3.5, Height = 3.5, Fill = dotBrush, Opacity = 0.75 });
            }
        }

        AutomationProperties.SetName(this, vm.AutomationName);
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        255,
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)));
}
