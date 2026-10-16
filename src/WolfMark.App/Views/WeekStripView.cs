using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;

namespace WolfMark.App.Views;

/// <summary>Compact week bar (M6-01): ‹ Mo..Su › with tag rings, counts and task progress.</summary>
internal sealed partial class WeekStripView : UserControl
{
    public WeekStripView(MainViewModel vm, TagBrushes brushes)
    {
        var days = new Grid { ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        for (var i = 0; i < 7; i++)
        {
            days.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 52 });
            var chip = CreateChip(vm, vm.Week.Days[i], brushes);
            Grid.SetColumn(chip, i);
            days.Children.Add(chip);
        }

        var label = Typo.Caps(string.Empty, 11);
        label.Bind(vm.Week, nameof(WeekStripViewModel.WeekLabel), () =>
            label.Text = vm.Week.WeekLabel.ToUpper(System.Globalization.CultureInfo.CurrentCulture));

        var today = new Button
        {
            Content = Typo.Body(Strings.Get(R.Common_Today), 12.5, Palette.InkSecondary),
            Padding = new Thickness(10, 4, 10, 4),
            CornerRadius = new CornerRadius(12),
            Background = Palette.Transparent,
            BorderBrush = Palette.Divider,
            VerticalAlignment = VerticalAlignment.Center,
        };
        today.Click += async (_, _) => await vm.SelectDayAsync(vm.Today);

        var calendar = Ui.Ui.IconButton(Glyphs.Calendar, Strings.Get(R.Toolbar_Calendar), () => _ = vm.OpenCalendarAsync());

        var row = Ui.Ui.Columns(
            (Ui.Ui.Auto, Ui.Ui.IconButton(Glyphs.ChevronLeft, Strings.Get(R.Toolbar_PreviousWeek), () => _ = vm.MoveWeekAsync(-1), 12)),
            (Ui.Ui.Star, days),
            (Ui.Ui.Auto, Ui.Ui.IconButton(Glyphs.ChevronRight, Strings.Get(R.Toolbar_NextWeek), () => _ = vm.MoveWeekAsync(1), 12)),
            (Ui.Ui.Auto, Ui.Ui.HStack(4, today, calendar)));
        row.ColumnSpacing = 6;
        row.VerticalAlignment = VerticalAlignment.Center;

        var labelRow = Ui.Ui.Columns((Ui.Ui.Star, label));
        labelRow.Margin = new Thickness(44, 0, 0, 2);

        Content = Ui.Ui.Rows((Ui.Ui.Auto, labelRow), (Ui.Ui.Auto, row));
        Padding = new Thickness(12, 2, 12, 6);
    }

    private static Button CreateChip(MainViewModel vm, WeekDayViewModel day, TagBrushes brushes)
    {
        var name = Typo.Caps(string.Empty, 10);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        var ring = new DayRing(38) { HorizontalAlignment = HorizontalAlignment.Center };
        var progress = new Rectangle { Height = 3, RadiusX = 1.5, RadiusY = 1.5, Fill = Palette.Accent, HorizontalAlignment = HorizontalAlignment.Left };
        var progressTrack = new Grid { Height = 3, Width = 28, CornerRadius = new CornerRadius(1.5), Background = Palette.SubtleFill, HorizontalAlignment = HorizontalAlignment.Center };
        progressTrack.Children.Add(progress);
        var count = Typo.Body(string.Empty, 11, Palette.InkTertiary);
        count.HorizontalAlignment = HorizontalAlignment.Center;

        var stack = Ui.Ui.VStack(3, name, ring, progressTrack, count);
        var button = new Button
        {
            Content = stack,
            Padding = new Thickness(4, 4, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Palette.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
        };
        button.Click += async (_, _) => await vm.SelectDayAsync(day.Date);

        void Update()
        {
            name.Text = day.DayName.ToUpper(System.Globalization.CultureInfo.CurrentCulture).TrimEnd('.');
            name.Foreground = day.IsToday ? Palette.Accent : Palette.InkTertiary;
            ring.Update(day.Date.Day, day.Day, day.IsToday, day.IsSelected, brushes);
            var tasks = day.Day.OpenTasks + day.Day.DoneTasks;
            progressTrack.Opacity = tasks == 0 ? 0 : 1;
            progress.Width = 28 * day.Day.TaskProgress;
            count.Text = day.Day.NoteCount == 0 ? " " : day.Day.NoteCount.ToString(System.Globalization.CultureInfo.CurrentCulture);
            AutomationProperties.SetName(button, day.AutomationName);
        }

        day.PropertyChanged += (_, _) => Update();
        brushes.Changed += (_, _) => Update();
        Update();
        return button;
    }
}
