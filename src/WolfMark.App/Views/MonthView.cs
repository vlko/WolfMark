using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;
using WolfMark.Core.Calendar;

namespace WolfMark.App.Views;

/// <summary>A month block: title, weekday capsules, ISO week numbers, day cells, week labels.</summary>
internal sealed partial class MonthView : UserControl, IItemView<MonthViewModel>
{
    private readonly CalendarView _owner;
    private readonly bool _large;
    private readonly StackPanel _root = new() { Spacing = 2 };
    private readonly List<DateOnly> _registered = [];

    public MonthView(CalendarView owner, bool large)
    {
        _owner = owner;
        _large = large;
        Content = _root;
        Padding = new Thickness(4, 0, 4, 18);
    }

    public void Bind(MonthViewModel month)
    {
        _root.Children.Clear();
        foreach (var date in _registered)
        {
            _owner.Unregister(date);
        }

        _registered.Clear();

        var title = new TextBlock
        {
            Text = month.Title,
            FontFamily = Typo.Display,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            CharacterSpacing = 40,
            Foreground = Palette.Ink,
            Margin = new Thickness(46, 0, 0, 6),
            Visibility = _large ? Visibility.Collapsed : Visibility.Visible,
        };
        _root.Children.Add(title);
        _root.Children.Add(WeekdayHeader(month));

        foreach (var row in month.Weeks)
        {
            _root.Children.Add(WeekRow(row));
            if (row.Labels.Count > 0 && !_large)
            {
                _root.Children.Add(Labels(row));
            }
        }
    }

    private Grid WeekdayHeader(MonthViewModel month)
    {
        var grid = Columns8();
        var names = CalendarMath.WeekdayNames(CultureInfo.CurrentCulture, abbreviated: true);
        for (var i = 0; i < 7; i++)
        {
            var isToday = i == month.TodayWeekday;
            var label = Typo.Caps(names[i].TrimEnd('.'), 9.5, isToday ? Palette.TodayInk : Palette.InkTertiary);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var capsule = new Border
            {
                Child = label,
                Background = isToday ? Palette.TodayFill : Palette.SubtleFill,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(0, 2, 0, 2),
                Margin = new Thickness(2.5, 0, 2.5, 2),
            };
            Grid.SetColumn(capsule, i + 1);
            grid.Children.Add(capsule);
        }

        return grid;
    }

    private Grid WeekRow(WeekRowViewModel row)
    {
        var grid = Columns8();
        grid.Background = Palette.Transparent; // hit-testable for the loupe
        var weekNumber = new Border
        {
            Child = new TextBlock
            {
                Text = row.Week.WeekNumber.ToString(CultureInfo.CurrentCulture),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = row.IsCurrentWeek ? Palette.TodayInk : Palette.InkTertiary,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
            Background = row.IsCurrentWeek ? Palette.TodayFill : Palette.SubtleFill,
            CornerRadius = new CornerRadius(6),
            Width = 30,
            Padding = new Thickness(0, 2, 0, 2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        grid.Children.Add(weekNumber);

        for (var i = 0; i < 7; i++)
        {
            var cellVm = row.Days[i];
            var cell = new CalendarDayCell(_owner.Brushes, _large);
            cell.Bind(cellVm);
            cell.Tapped += (_, e) =>
            {
                e.Handled = true;
                _owner.InvokeDay(cellVm.Date);
            };
            cell.KeyDown += (_, e) => _owner.OnCellKeyDown(cellVm.Date, e);
            Grid.SetColumn(cell, i + 1);
            grid.Children.Add(cell);
            if (cellVm.InMonth)
            {
                _owner.Register(cellVm.Date, cell);
                _registered.Add(cellVm.Date);
            }
        }

        if (!_large)
        {
            grid.PointerEntered += (_, _) => _owner.ShowLoupe(grid, row);
        }

        return grid;
    }

    private StackPanel Labels(WeekRowViewModel row)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(46, -1, 0, 4) };
        foreach (var label in row.Labels.Take(3))
        {
            var text = Typo.Caps(label.Title, 9, Palette.InkSecondary);
            text.MaxWidth = 150;
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            panel.Children.Add(Ui.Ui.HStack(
                4,
                new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 6, Height = 6, RadiusX = 1, RadiusY = 1, Fill = _owner.Brushes.BrushOf(label.Tag), VerticalAlignment = VerticalAlignment.Center },
                text));
        }

        return panel;
    }

    private static Grid Columns8()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        for (var i = 0; i < 7; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        return grid;
    }
}
