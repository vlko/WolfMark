using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;
using WolfMark.Core.Notes;

namespace WolfMark.App.Views;

/// <summary>History under the editor (M5-01, M5-02, M5-04): virtualised, infinite scroll, filters, context menu.</summary>
internal sealed partial class HistoryView : UserControl
{
    private readonly MainViewModel _vm;
    private readonly TagBrushes _brushes;
    private readonly ScrollViewer _scroller;

    public HistoryView(MainViewModel vm, TagBrushes brushes)
    {
        _vm = vm;
        _brushes = brushes;
        var history = vm.History;

        var rowFactory = new ElementFactory<HistoryRowViewModel, HistoryRowView>(() => new HistoryRowView(this));
        var headerFactory = new ElementFactory<HistoryHeaderViewModel, HistoryHeaderView>(() => new HistoryHeaderView());
        var repeater = new ItemsRepeater
        {
            ItemsSource = history.Items,
            ItemTemplate = new SelectingElementFactory(item => item is HistoryHeaderViewModel ? headerFactory : rowFactory),
            Layout = new StackLayout { Spacing = 0 },
        };

        _scroller = new ScrollViewer
        {
            Content = repeater,
            Padding = new Thickness(0, 0, 8, 12),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _scroller.ViewChanged += async (_, _) =>
        {
            if (_scroller.VerticalOffset > _scroller.ScrollableHeight - 400)
            {
                await history.LoadMoreAsync();
            }
        };

        var title = Typo.SectionHeader(Strings.Get(R.History_Title), out _, 19);
        var filterChip = new Button
        {
            Padding = new Thickness(10, 2, 8, 2),
            CornerRadius = Radius.Control,
        };
        var filterText = Typo.Caps(string.Empty, 12.5, Palette.Ink);
        filterChip.Content = Ui.Ui.HStack(6, filterText, Typo.Icon(Glyphs.Close, 9));
        filterChip.StyleButton(Palette.SubtleFill, Palette.Ink);
        ToolTipService.SetToolTip(filterChip, Strings.Get(R.History_ClearFilter));
        filterChip.Click += async (_, _) =>
        {
            if (vm.ActiveTag is not null)
            {
                await vm.SetActiveTagAsync(null);
            }

            await history.ClearFiltersAsync();
        };
        filterChip.Bind(history, [nameof(HistoryViewModel.TagFilter), nameof(HistoryViewModel.DayFilter), nameof(HistoryViewModel.OpenTasksOnly)], () =>
        {
            filterChip.Visibility = history.HasFilter ? Visibility.Visible : Visibility.Collapsed;
            filterText.Text = history.FilterLabel.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        });

        var openTasks = new Button
        {
            Content = Ui.Ui.HStack(6, Typo.Icon(Glyphs.Checkbox, 12), Typo.Body(Strings.Get(R.History_OpenTasks), 12, Palette.InkSecondary)),
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = Radius.Control,
        };
        openTasks.StyleButton(Palette.Transparent, Palette.InkSecondary);
        openTasks.Click += async (_, _) => await history.ToggleOpenTasksAsync();

        var conflicts = new Button
        {
            Padding = new Thickness(8, 2, 8, 2),
            CornerRadius = Radius.Control,
        };
        var conflictText = Typo.Body(string.Empty, 12, Palette.InkSecondary);
        conflicts.Content = Ui.Ui.HStack(6, Typo.Icon(Glyphs.Warning, 12), conflictText);
        conflicts.StyleButton(Palette.Transparent, Palette.InkSecondary);
        conflicts.Bind(vm, nameof(MainViewModel.ConflictCount), () =>
        {
            conflicts.Visibility = vm.ConflictCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            conflictText.Text = Strings.Format(R.Conflicts_Count, vm.ConflictCount);
        });
        conflicts.Click += async (_, _) =>
        {
            var list = await vm.Host.History.GetConflictsAsync(vm.Host.ShutdownToken);
            if (list.Count > 0)
            {
                await vm.OpenNoteAsync(list[0].RelativePath);
            }
        };

        var status = Typo.Body(string.Empty, 11.5, Palette.InkTertiary);
        status.Bind(vm, nameof(MainViewModel.StatusText), () => status.Text = vm.StatusText);

        // Title + hairline take the free space; the controls are anchored to the right edge.
        var header = Ui.Ui.Columns(
            (Ui.Ui.Star, title),
            (Ui.Ui.Auto, filterChip),
            (Ui.Ui.Auto, status),
            (Ui.Ui.Auto, conflicts),
            (Ui.Ui.Auto, openTasks));
        header.ColumnSpacing = 10;
        header.Margin = new Thickness(0, 10, 8, 4);

        var empty = Typo.Body(Strings.Get(R.History_Empty), 13, Palette.InkTertiary);
        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.Margin = new Thickness(0, 24, 0, 0);
        empty.Bind(history, nameof(HistoryViewModel.IsEmpty), () => empty.Visibility = history.IsEmpty ? Visibility.Visible : Visibility.Collapsed);

        var body = new Grid();
        body.Children.Add(_scroller);
        body.Children.Add(empty);
        Content = Ui.Ui.Rows((Ui.Ui.Auto, header), (Ui.Ui.Star, body));
    }

    internal MainViewModel ViewModel => _vm;

    internal TagBrushes Brushes => _brushes;

    internal MenuFlyout CreateContextMenu(HistoryRowViewModel row)
    {
        var note = row.Note;
        var host = _vm.Host;
        var menu = new MenuFlyout();

        var open = new MenuFlyoutItem { Text = Strings.Get(R.Menu_OpenInExplorer), Icon = new FontIcon { Glyph = Glyphs.Folder } };
        open.Click += (_, _) =>
        {
            var full = host.Engine.GetFullPath(note.RelativePath);
            _ = Task.Run(() => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"") { UseShellExecute = false }));
        };

        var copy = new MenuFlyoutItem { Text = Strings.Get(R.Menu_CopyLink), Icon = new FontIcon { Glyph = Glyphs.Link } };
        copy.Click += (_, _) =>
        {
            var data = new DataPackage();
            data.SetText("[[" + note.RelativePath[..^NoteName.Extension.Length] + "]]");
            Clipboard.SetContent(data);
        };

        var tagMenu = new MenuFlyoutSubItem { Text = Strings.Get(R.Menu_ChangeTag), Icon = new FontIcon { Glyph = Glyphs.Tag } };
        tagMenu.IsEnabled = note.IsStandard && !note.IsClaudeCode;
        tagMenu.Items.Add(TagItem(null));
        menu.Opening += async (_, _) =>
        {
            while (tagMenu.Items.Count > 1)
            {
                tagMenu.Items.RemoveAt(1);
            }

            foreach (var t in (await host.History.GetAllTagsAsync(host.ShutdownToken)).Take(15))
            {
                tagMenu.Items.Add(TagItem(t.Tag));
            }
        };

        var delete = new MenuFlyoutItem { Text = Strings.Get(R.Menu_Delete), Icon = new FontIcon { Glyph = Glyphs.Delete } };
        delete.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Strings.Get(R.Delete_ConfirmTitle),
                Content = row.Title,
                PrimaryButtonText = Strings.Get(R.Menu_Delete),
                CloseButtonText = Strings.Get(R.Common_Cancel),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            if (string.Equals(_vm.Editor.Path?.RelativePath, note.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                await _vm.Editor.DeleteAsync();
            }
            else if (NotePath.TryParse(note.RelativePath, out var path))
            {
                await Task.Run(() => host.Engine.DeleteAsync(path, CancellationToken.None));
            }
        };

        menu.Items.Add(tagMenu);
        menu.Items.Add(open);
        menu.Items.Add(copy);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(delete);
        return menu;

        MenuFlyoutItem TagItem(string? tag)
        {
            var item = new MenuFlyoutItem { Text = tag ?? Strings.Get(R.Tag_None) };
            item.Click += async (_, _) =>
            {
                if (string.Equals(_vm.Editor.Path?.RelativePath, note.RelativePath, StringComparison.OrdinalIgnoreCase))
                {
                    await _vm.Editor.ChangeTagAsync(tag);
                }
                else if (NotePath.TryParse(note.RelativePath, out var path))
                {
                    await Task.Run(() => host.Engine.ChangeTagAsync(path, tag, CancellationToken.None));
                }
            };
            return item;
        }
    }
}

internal sealed partial class HistoryHeaderView : UserControl, IItemView<HistoryHeaderViewModel>
{
    private readonly TextBlock _label;

    public HistoryHeaderView()
    {
        Content = Typo.SectionHeader(string.Empty, out _label, 15);
        _label.Foreground = Palette.InkTertiary;
        Padding = new Thickness(Metrics.TimeGutter + 13, 14, 0, 4);
    }

    public void Bind(HistoryHeaderViewModel item) => _label.Text = item.Label.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
}

internal sealed partial class HistoryRowView : UserControl, IItemView<HistoryRowViewModel>
{
    private readonly HistoryView _owner;
    private readonly Button _button;
    private readonly SolidColorBrush _rowFill = new(Microsoft.UI.Colors.Transparent);
    private readonly Rectangle _bar = new() { Width = 3, Margin = new Thickness(0, 4, 0, 4) };
    private const double BarWidth = 3;

    private readonly StackPanel _time;
    private readonly Action<DateTime?> _setTime;
    private readonly TextBlock _created = Typo.Caps(string.Empty, 10.5, Palette.InkTertiary);
    private readonly TextBlock _title = Typo.Body(string.Empty, 14);
    private readonly TextBlock _preview = Typo.Body(string.Empty, 12.5, Palette.InkSecondary);
    private readonly TextBlock _tag = Typo.Body(string.Empty, 11.5, Palette.InkTertiary);
    private readonly TextBlock _tasks = Typo.Body(string.Empty, 11.5, Palette.InkTertiary);
    private readonly FontIcon _badge = Typo.Icon(string.Empty, 12, Palette.InkTertiary);
    private HistoryRowViewModel? _item;

    public HistoryRowView(HistoryView owner)
    {
        _owner = owner;
        // Last edit as the lead time (history is ordered by it), creation in small caps below.
        _time = Typo.LeadTime(out _setTime, 24, Palette.InkSecondary);
        _created.CharacterSpacing = 10;
        _created.HorizontalAlignment = HorizontalAlignment.Right;
        _created.Margin = new Thickness(0, 1, 0, 0);
        var times = Ui.Ui.VStack(0, _time, _created);
        times.HorizontalAlignment = HorizontalAlignment.Right;
        times.VerticalAlignment = VerticalAlignment.Center;

        var text = Ui.Ui.VStack(1, _title, _preview);
        var right = Ui.Ui.VStack(2, _tag, _tasks);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        _tag.HorizontalAlignment = HorizontalAlignment.Right;
        _tasks.HorizontalAlignment = HorizontalAlignment.Right;

        text.Margin = new Thickness(12, 0, 0, 0);
        text.VerticalAlignment = VerticalAlignment.Center;
        var grid = Ui.Ui.Columns(
            (Ui.Ui.Px(BarWidth), _bar),
            (Ui.Ui.Px(Metrics.TimeGutter - BarWidth - Metrics.TimeGutterGap), times),
            (Ui.Ui.Px(Metrics.TimeGutterGap), null),
            (Ui.Ui.Px(1), Ui.Ui.DottedLine()),
            (Ui.Ui.Star, text),
            (Ui.Ui.Auto, _badge),
            (Ui.Ui.Auto, right));
        grid.ColumnSpacing = 0;
        _badge.Margin = new Thickness(8, 0, 8, 0);

        _button = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, 4, 10, 4),
            CornerRadius = Radius.None,
        };
        // Own brush: the "current note" fill changes, hover must keep following it.
        _button.StyleButton(_rowFill, Palette.Ink);
        _button.Click += async (_, _) =>
        {
            if (_item is not null)
            {
                await _owner.ViewModel.OpenNoteAsync(_item.Note.RelativePath);
            }
        };
        _button.ContextRequested += (_, e) =>
        {
            if (_item is not null)
            {
                _owner.CreateContextMenu(_item).ShowAt(_button);
                e.Handled = true;
            }
        };
        Content = _button;
    }

    public void Bind(HistoryRowViewModel item)
    {
        if (_item is not null)
        {
            _item.PropertyChanged -= OnItemChanged;
        }

        _item = item;
        item.PropertyChanged += OnItemChanged;
        var note = item.Note;
        _bar.Fill = _owner.Brushes.BrushOf(note.PrimaryTag);
        _setTime(note.ModifiedAt);
        _created.Text = item.CreatedText;
        _title.Text = item.Title;
        _preview.Text = note.Preview;
        _preview.Visibility = note.Preview.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _tag.Text = (note.PrimaryTag ?? string.Empty).ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        _tag.FontFamily = Typo.CondensedMedium;
        _tasks.Text = item.TaskText;
        _badge.Glyph = note.IsConflict ? Glyphs.Warning : note.IsClaudeCode ? Glyphs.Lock : note.IsPlaceholder ? Glyphs.Cloud : string.Empty;
        AutomationProperties.SetName(_button, $"{item.TimeText} {item.Title} {note.PrimaryTag}");
        UpdateCurrent();
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdateCurrent();

    private void UpdateCurrent() => _rowFill.Color = _item?.IsCurrent == true ? Palette.SubtleFill.Color : Microsoft.UI.Colors.Transparent;
}
