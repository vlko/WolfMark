using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Desktop.Theming;
using WolfMark.Index.Queries;

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
        var openTasksLabel = Typo.Body(vm.History.OpenTasksLabel, 13.5, Palette.InkSecondary);
        var openTasks = new CheckBox { Content = openTasksLabel, Margin = new Thickness(16, 0, 12, 0) };
        Ui.Bind(vm.History, nameof(HistoryViewModel.OpenTasksLabel), () => openTasksLabel.Text = vm.History.OpenTasksLabel);
        openTasks.IsCheckedChanged += async (_, _) =>
        {
            if ((openTasks.IsChecked == true) != vm.History.OpenTasksOnly)
            {
                await vm.History.ToggleOpenTasksAsync();
            }
        };
        // The filter can also change elsewhere (the chip below, a tag in the rail): the box follows the view model.
        Ui.Bind(vm.History, nameof(HistoryViewModel.OpenTasksOnly), () => openTasks.IsChecked = vm.History.OpenTasksOnly);

        // What the history is filtered by; a click clears it (as in the Windows app).
        var filterText = Typo.Caps(string.Empty, 12.5, Palette.Ink);
        var filterChip = Ui.FlatButton(Ui.HStack(6, filterText, Icons.Create(Icons.Close, 9, Palette.Ink)));
        filterChip.Padding = new Thickness(10, 2, 8, 2);
        filterChip.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(filterChip, Strings.Get(R.History_ClearFilter));
        Avalonia.Automation.AutomationProperties.SetName(filterChip, Strings.Get(R.History_ClearFilter));
        filterChip.Click += async (_, _) =>
        {
            if (vm.ActiveTag is not null)
            {
                await vm.SetActiveTagAsync(null);
            }

            await vm.History.ClearFiltersAsync();
        };
        Ui.Bind(vm.History, [nameof(HistoryViewModel.TagFilter), nameof(HistoryViewModel.DayFilter), nameof(HistoryViewModel.OpenTasksOnly)], () =>
        {
            filterChip.IsVisible = vm.History.HasFilter;
            filterText.Text = vm.History.FilterLabel.ToUpper(System.Globalization.CultureInfo.CurrentCulture);
        });
        var title = Ui.Columns((Ui.Star, Ui.SectionHeader(Strings.Get(R.History_Title))), (Ui.Auto, filterChip), (Ui.Auto, openTasks));
        title.Margin = new Thickness(0, 8, 0, 2);

        var list = new ItemsControl
        {
            ItemsSource = vm.History.Items,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
            {
                HistoryHeaderViewModel header => Header(header),
                HistoryRowViewModel row => new HistoryRow(vm, row),
                HistoryTaskViewModel task => new TaskRow(vm, task),
                HistoryExternalRowViewModel external => new ExternalRow(vm, external),
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
        Ui.Bind(vm.History, [nameof(HistoryViewModel.IsEmpty), nameof(HistoryViewModel.EmptyText)], () =>
        {
            empty.IsVisible = vm.History.IsEmpty;
            empty.Text = vm.History.EmptyText;
        });
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
            // One sentence for a screen reader instead of every text block of the row.
            Avalonia.Automation.AutomationProperties.SetName(this, $"{row.TimeText} {row.Title} {note.PrimaryTag}");
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
            ContextMenu = CreateMenu(vm, note, row.Title);
            Ui.Bind(row, nameof(HistoryRowViewModel.IsCurrent), UpdateCurrent);
            Ui.OnPaletteChanged(this, UpdateCurrent);
        }

        private void UpdateCurrent() => Background = _row.IsCurrent ? Palette.SubtleFill : Palette.Transparent;

        /// <summary>M5-04, as in the Windows app: change tag, open folder, copy link, move to trash.</summary>
        private ContextMenu CreateMenu(MainViewModel vm, NoteSummary note, string title)
        {
            var host = vm.Host;
            var tagMenu = new MenuItem { Header = Strings.Get(R.Menu_ChangeTag), IsEnabled = note.IsStandard && !note.IsClaudeCode };
            var folder = new MenuItem { Header = Strings.Get(R.Menu_OpenFolder) };
            folder.Click += (_, _) => ShowFolder(this, host.Engine.GetFullPath(note.RelativePath));
            var copy = new MenuItem { Header = Strings.Get(R.Menu_CopyLink) };
            copy.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(MainViewModel.NoteLink(note.RelativePath));
                }
            };
            var copyAppLink = new MenuItem { Header = Strings.Get(R.Menu_CopyAppLink) };
            copyAppLink.Click += async (_, _) =>
            {
                if (WolfMark.Core.Notes.NotePath.TryParse(note.RelativePath, out var path) && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(WolfMark.Core.Links.AppLink.ForNote(path).ToString());
                }
            };
            var delete = new MenuItem { Header = Strings.Get(R.Menu_Delete) };
            delete.Click += async (_, _) =>
            {
                if (await ConfirmDialog.AskAsync(this, Strings.Get(R.Delete_ConfirmTitle), title, Strings.Get(R.Menu_Delete)))
                {
                    await vm.DeleteNoteAsync(note.RelativePath);
                }
            };

            var menu = new ContextMenu { Items = { tagMenu, folder, copy, copyAppLink, new Separator(), delete } };
            menu.Opening += async (_, _) =>
            {
                // The tags of the moment: "no tag" first, then the most used ones.
                tagMenu.Items.Clear();
                tagMenu.Items.Add(TagItem(null));
                foreach (var t in (await host.GetTagListAsync(host.ShutdownToken)).Take(15))
                {
                    tagMenu.Items.Add(TagItem(t.Tag));
                }
            };
            return menu;

            MenuItem TagItem(string? tag)
            {
                var item = new MenuItem { Header = tag ?? Strings.Get(R.Tag_None) };
                item.Click += async (_, _) => await vm.ChangeNoteTagAsync(note.RelativePath, tag);
                return item;
            }
        }
    }

    /// <summary>
    /// An open task under its note in "Open tasks" (M9-01): the box ticks it off (not in a read-only Claude Code
    /// note), the text opens the note at the task.
    /// </summary>
    private sealed class TaskRow : Grid
    {
        public TaskRow(MainViewModel vm, HistoryTaskViewModel task)
        {
            var box = new CheckBox
            {
                IsEnabled = task.CanComplete,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(box, Strings.Get(R.Task_Complete));
            Avalonia.Automation.AutomationProperties.SetName(box, Strings.Get(R.Task_Complete) + ": " + task.Text);
            box.Click += async (_, _) => await vm.CompleteTaskAsync(task);

            var text = Typo.Body(task.Text, 13.5);
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            var open = new Button
            {
                Content = text,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Palette.Transparent,
                Foreground = Palette.Ink,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(8, 3, 10, 3),
            };
            open.Click += async (_, _) => await vm.OpenNoteAsync(task.Note.RelativePath, task.Line);

            // A failed tick comes back unticked; a ticked one is struck through until the list reloads without it.
            Ui.Bind(task, nameof(HistoryTaskViewModel.IsDone), () =>
            {
                box.IsChecked = task.IsDone;
                text.Opacity = task.IsDone ? 0.5 : 1;
                text.TextDecorations = task.IsDone ? TextDecorations.Strikethrough : null;
            });

            ColumnDefinitions = new ColumnDefinitions($"{Metrics.TimeGutter},1,Auto,*");
            var line = new DottedLine();
            SetColumn(line, 1);
            SetColumn(box, 2);
            SetColumn(open, 3);
            Children.AddRange([line, box, open]);
        }
    }

    /// <summary>The file manager on the file's folder (a missing file: the folder it was in).</summary>
    private static void ShowFolder(Control owner, string fullPath)
    {
        if (TopLevel.GetTopLevel(owner)?.Launcher is { } launcher && Path.GetDirectoryName(fullPath) is { } directory)
        {
            _ = launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory));
        }
    }

    /// <summary>
    /// An external file: when it was last opened or saved, "external" instead of the creation, the file name where
    /// the tag is (full path on hover); a click opens it in its own window, the menu drops it from the history.
    /// </summary>
    private sealed class ExternalRow : Button
    {
        public ExternalRow(MainViewModel vm, HistoryExternalRowViewModel row)
        {
            var time = Typo.LeadTime(out var setTime, 24, Palette.InkSecondary);
            setTime(row.TouchedAt);
            var external = Typo.Caps(row.ExternalText, 10.5, Palette.InkTertiary);
            external.HorizontalAlignment = HorizontalAlignment.Right;
            var times = Ui.VStack(0, time, external);
            times.HorizontalAlignment = HorizontalAlignment.Right;
            times.VerticalAlignment = VerticalAlignment.Center;

            var preview = Typo.Body(row.Preview, 12.5, Palette.InkSecondary);
            preview.IsVisible = row.Preview.Length > 0;
            var text = Ui.VStack(1, Typo.Body(row.Title, 14), preview);
            text.Margin = new Thickness(12, 0, 0, 0);
            text.VerticalAlignment = VerticalAlignment.Center;

            var fileName = Typo.Body(row.FileName, 11.5, Palette.InkTertiary);
            fileName.VerticalAlignment = VerticalAlignment.Center;
            var badge = Icons.Create(row.IsMissing ? Icons.Warning : Icons.Document, 12, Palette.InkTertiary);
            badge.Margin = new Thickness(8, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;

            Content = Ui.Columns(
                (Ui.Px(3), new Border { Width = 3, Margin = new Thickness(0, 4), Background = Palette.Divider }),
                (Ui.Px(Metrics.TimeGutter - 3 - Metrics.TimeGutterGap), times),
                (Ui.Px(Metrics.TimeGutterGap), null),
                (Ui.Px(1), new DottedLine()),
                (Ui.Star, text),
                (Ui.Auto, badge),
                (Ui.Auto, fileName));

            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            BorderThickness = new Thickness(0);
            CornerRadius = new CornerRadius(0);
            Padding = new Thickness(0, 4, 10, 4);
            Foreground = Palette.Ink;
            Background = Palette.Transparent;
            Opacity = row.IsMissing ? 0.5 : 1;
            ToolTip.SetTip(this, row.FullPath);
            Avalonia.Automation.AutomationProperties.SetName(this, $"{row.ExternalText} {row.Title} {row.FullPath}");
            Click += async (_, _) => await vm.OpenFileAsync(row.FullPath);

            var open = new MenuItem { Header = Strings.Get(R.Menu_Open), IsEnabled = !row.IsMissing };
            open.Click += async (_, _) => await vm.OpenFileAsync(row.FullPath);
            var folder = new MenuItem { Header = Strings.Get(R.Menu_OpenFolder) };
            folder.Click += (_, _) => ShowFolder(this, row.FullPath);
            var remove = new MenuItem { Header = Strings.Get(R.Menu_RemoveFromHistory) };
            remove.Click += (_, _) => vm.RemoveExternalFile(row.FullPath);
            ContextMenu = new ContextMenu { Items = { open, folder, new Separator(), remove } };
        }
    }
}
