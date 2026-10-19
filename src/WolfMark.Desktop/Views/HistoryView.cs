using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// History (M5-01): day headers and note rows – last edit as the lead time, creation below, title and
/// preview, tag; virtualised, the next page loads near the end.
/// </summary>
internal sealed class HistoryView : UserControl
{
    private readonly MainViewModel _vm;

    public HistoryView(MainViewModel vm)
    {
        _vm = vm;
        // HISTORY ———— [☐ Open tasks]
        var openTasks = new CheckBox { Content = Typo.Body(Strings.Get(R.History_OpenTasks), 13.5, Palette.InkSecondary), Margin = new Thickness(16, 0, 12, 0) };
        openTasks.IsCheckedChanged += async (_, _) =>
        {
            if ((openTasks.IsChecked == true) != vm.History.OpenTasksOnly)
            {
                await vm.History.ToggleOpenTasksAsync();
            }
        };
        var title = Ui.Columns((Ui.Star, Ui.SectionHeader(Strings.Get(R.History_Title))), (Ui.Auto, openTasks));
        title.Margin = new Thickness(0, 8, 0, 2);

        var list = new ItemsControl
        {
            ItemsSource = vm.History.Items,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
            {
                HistoryHeaderViewModel header => Header(header),
                HistoryRowViewModel row => new HistoryRow(vm, row),
                _ => null,
            }, supportsRecycling: false),
        };
        var scroll = new ScrollViewer { Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        scroll.ScrollChanged += async (_, _) =>
        {
            if (scroll.Offset.Y + scroll.Viewport.Height > scroll.Extent.Height - 200)
            {
                await _vm.History.LoadMoreAsync();
            }
        };

        var empty = Typo.Body(Strings.Get(R.History_Empty), 13, Palette.InkTertiary);
        empty.Margin = new Thickness(Metrics.TimeGutter + 13, 8, 0, 0);
        void UpdateEmpty() => empty.IsVisible = vm.History.IsEmpty;
        Ui.Bind(vm.History, nameof(HistoryViewModel.IsEmpty), UpdateEmpty);
        vm.History.Items.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                scroll.Offset = default;
            }
        };

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        Grid.SetRow(empty, 1);
        Grid.SetRow(scroll, 2);
        grid.Children.Add(title);
        grid.Children.Add(empty);
        grid.Children.Add(scroll);
        Content = grid;
    }

    private static Control Header(HistoryHeaderViewModel header)
    {
        var label = Typo.Caps(header.Label.ToUpper(CultureInfo.CurrentCulture), 15, Palette.InkTertiary);
        label.Margin = new Thickness(0, 10, 0, 4);
        return label;
    }

    private sealed class HistoryRow : Button
    {
        private readonly HistoryRowViewModel _row;

        public HistoryRow(MainViewModel vm, HistoryRowViewModel row)
        {
            _row = row;
            var note = row.Note;
            var time = Typo.LeadTime(out var setTime, 24, Palette.InkSecondary);
            setTime(note.ModifiedAt);
            var created = Typo.Caps(row.CreatedText, 10.5, Palette.InkTertiary);
            created.HorizontalAlignment = HorizontalAlignment.Right;
            var times = Ui.VStack(0, time, created);
            times.HorizontalAlignment = HorizontalAlignment.Right;
            times.VerticalAlignment = VerticalAlignment.Center;

            var preview = Typo.Body(note.Preview, 12.5, Palette.InkSecondary);
            preview.IsVisible = note.Preview.Length > 0;
            var text = Ui.VStack(1, Typo.Body(row.Title, 14), preview);
            text.Margin = new Thickness(12, 0, 0, 0);
            text.VerticalAlignment = VerticalAlignment.Center;

            var tag = Typo.Caps((note.PrimaryTag ?? string.Empty).ToUpper(CultureInfo.CurrentCulture), 11.5, Palette.InkTertiary);
            var tasks = Typo.Body(row.TaskText, 11.5, Palette.InkTertiary);
            var right = Ui.VStack(2, tag, tasks);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Center;

            var bar = new Border
            {
                Width = 3,
                Margin = new Thickness(0, 4),
                Background = note.PrimaryTag is { } t ? new SolidColorBrush(Palette.ToColor(vm.Host.Tags.GetColor(t))) : Brushes.Transparent,
            };
            // Conflict copy, read-only Claude Code note or a file still in the cloud (OneDrive placeholder).
            var badgeIcon = note.IsConflict ? Icons.Warning : note.IsClaudeCode ? Icons.Lock : note.IsPlaceholder ? Icons.Cloud : null;
            Control? badge = badgeIcon is null ? null : Icons.Create(badgeIcon, 12, Palette.InkTertiary);
            if (badge is not null)
            {
                badge.Margin = new Thickness(8, 0);
                badge.VerticalAlignment = VerticalAlignment.Center;
            }

            Content = Ui.Columns(
                (Ui.Px(3), bar),
                (Ui.Px(Metrics.TimeGutter - 3 - Metrics.TimeGutterGap), times),
                (Ui.Px(Metrics.TimeGutterGap), null),
                (Ui.Px(1), new DottedLine()),
                (Ui.Star, text),
                (Ui.Auto, badge),
                (Ui.Auto, right));

            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            BorderThickness = new Thickness(0);
            CornerRadius = new CornerRadius(0);
            Padding = new Thickness(0, 4, 10, 4);
            Foreground = Palette.Ink;
            Click += async (_, _) => await vm.OpenNoteAsync(_row.Note.RelativePath);
            Ui.Bind(row, nameof(HistoryRowViewModel.IsCurrent), UpdateCurrent);
            Palette.Changed += (_, _) => UpdateCurrent();
        }

        private void UpdateCurrent() => Background = _row.IsCurrent ? Palette.SubtleFill : Palette.Transparent;
    }
}
