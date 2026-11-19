using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WolfMark.App;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Links;
using WolfMark.Core.Settings;
using WolfMark.Desktop.Editor;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// Main window, laid out like the Windows one (src/WolfMark.App/Views/MainWindow.cs): day heading, week band,
/// note bar, editor with its time gutter, history and status bar on one flat paper surface.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly CalendarPanel _calendar;
    private readonly FindBar _findBar;
    private readonly AppHost _host;
    private readonly MarkdownTextEditor _editor = new();
    private readonly SearchPalette _search;
    private TrayIcon? _tray;
    private QuickNoteWindow? _quickNote;
    private bool _exitRequested;
    private readonly RowDefinition _historyRow;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly TextBlock _fullViewTimes = Typo.Caps(string.Empty, 12, Palette.InkTertiary);
    private readonly Dictionary<string, ExternalNoteWindow> _externalWindows = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool _closing;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        _host = vm.Host;
        Title = Strings.Get(R.AppTitle);
        Background = Palette.WindowTint;
        Palette.Changed += (_, _) => Background = Palette.WindowTint;
        RestorePlacement();

        _historyRow = new RowDefinition(Math.Max(120, _host.Settings.Current.HistoryPaneHeight), GridUnitType.Pixel);
        var main = new Grid { Margin = new Thickness(24, 4, 8, 0) };
        main.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // day heading, calendar controls
        main.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // full view bar
        main.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // calendar
        main.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // note bar
        main.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star) { MinHeight = 120 });
        main.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // splitter
        main.RowDefinitions.Add(_historyRow);

        var calendar = _calendar = new CalendarPanel(vm) { Margin = new Thickness(0, 0, 0, 12) };
        // date left, calendar navigation and title right – one row, as on Android (M6-07)
        var dateRow = Ui.Columns((Ui.Star, DayHeading()), (Ui.Auto, calendar.Header));
        dateRow.ColumnSpacing = 12;
        dateRow.Margin = new Thickness(0, 0, 0, 14);
        var noteBar = new NoteBar(vm, _editor) { Margin = new Thickness(0, 0, 0, 10) };
        var splitter = new GridSplitter
        {
            Height = 6,
            Background = Palette.Transparent,
            ResizeDirection = GridResizeDirection.Rows,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        splitter.DragCompleted += (_, _) => _host.Settings.Current.HistoryPaneHeight = _historyRow.ActualHeight;
        var editorCell = new Grid();
        var editorPane = EditorPane();
        editorCell.Children.Add(editorPane);
        editorCell.Children.Add(ClaudeBadge());
        _findBar = new FindBar(vm.Find, _editor);
        editorCell.Children.Add(_findBar);
        var fullViewBar = FullViewBar();
        var history = new HistoryView(vm);
        Add(main, dateRow, 0);
        Add(main, fullViewBar, 1);
        Add(main, calendar, 2);
        Add(main, noteBar, 3);
        Add(main, editorCell, 4);
        Add(main, splitter, 5);
        Add(main, history, 6);

        var rail = new TagRail(vm) { Margin = new Thickness(0, 8, 0, 0) };
        var content = Ui.Columns((Ui.Star, main), (Ui.Auto, rail));
        // With the month open in a low window the panes need more than there is: the history is cut at the bottom
        // instead of being drawn over the notices and the status bar.
        content.ClipToBounds = true;
        WireLayout(main, fullViewBar, editorPane, editorCell, noteBar, splitter, [dateRow, calendar, history, rail]);
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        var notices = new Notices(vm);
        var status = new StatusBar(vm);
        var top = TopBar();
        Grid.SetRow(content, 1);
        Grid.SetRow(notices, 2);
        Grid.SetRow(status, 3);
        root.Children.Add(top);
        root.Children.Add(content);
        root.Children.Add(notices);
        root.Children.Add(status);
        _search = new SearchPalette(vm) { IsVisible = false };
        Grid.SetRowSpan(_search, 4);
        root.Children.Add(_search);
        Content = root;
        Icon = LoadIcon(_host.Settings.Current.AppIcon);

        WireEditor();
        WireOverlays();
        _vm.ExternalFileRequested += (_, path) => OpenExternal(path);
        WireShortcuts();
        WireLifecycle();
        ApplyTray();
    }

    /// <param name="ready">The start note is in the editor and it has the focus; the panels are still loading.</param>
    public Task InitializeAsync(Action? ready = null) => _vm.InitializeAsync(() =>
    {
        _editor.FocusEditor();
        ready?.Invoke();
    });

    internal MainViewModel ViewModel => _vm;

    /// <summary>The image of an icon variant (Settings → Appearance); the built-in icon when it is missing.</summary>
    internal static Bitmap? IconImage(string id)
    {
        var uri = new Uri($"avares://WolfMark.Desktop/Assets/Icons/{id}.png");
        return AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : null;
    }

    private static WindowIcon LoadIcon(string? id)
    {
        var uri = new Uri($"avares://WolfMark.Desktop/Assets/Icons/{AppIconCatalog.Normalize(id)}.png");
        return new WindowIcon(AssetLoader.Open(AssetLoader.Exists(uri) ? uri : new Uri("avares://WolfMark.Desktop/Assets/WolfMark-256.png")));
    }

    /// <summary>The chosen icon on this window, its quick note and the tray – also as a preview from Settings.</summary>
    internal void ApplyIcon(string? id)
    {
        Icon = LoadIcon(id);
        if (_quickNote is not null)
        {
            _quickNote.Icon = Icon;
        }

        if (_tray is not null)
        {
            _tray.Icon = Icon;
        }
    }

    internal void ScrollEditorToStart() => _editor.ScrollToStart();

    internal ExternalNoteWindow? ExternalWindowOf(string fullPath) => _externalWindows.GetValueOrDefault(fullPath);

    /// <summary>Ends the app (the tray's Exit, the screenshot run) – also when closing only hides to the tray.</summary>
    internal void Exit()
    {
        _exitRequested = true;
        Close();
    }

    /// <summary>A file from outside the notes tree in its own window (or the window it already has).</summary>
    private void OpenExternal(string fullPath)
    {
        if (_externalWindows.TryGetValue(fullPath, out var open))
        {
            open.BringToFront();
            return;
        }

        BringToFront();
        var window = new ExternalNoteWindow(this, _vm, new ExternalDocumentViewModel(_host, fullPath, _vm.TouchExternalFile));
        _externalWindows[fullPath] = window;
        window.Closed += (_, _) => _externalWindows.Remove(fullPath);
        window.Show();
        _ = window.LoadAsync();
    }

    /// <summary>
    /// A <c>wolfmark://</c> link (M8-03) from the protocol, the jump list or another launch: the quick note window,
    /// or the main window on its day, note or search.
    /// </summary>
    public async Task OpenLinkAsync(AppLink link)
    {
        if (link.Kind == AppLinkKind.New)
        {
            await ShowQuickNoteAsync();
            return;
        }

        BringToFront();
        if (link.Kind == AppLinkKind.Search)
        {
            _vm.IsFullView = false;
            _vm.OpenSearch();
            if (link.Query is { } query)
            {
                _search.SetText(query);
            }

            return;
        }

        await _vm.OpenLinkAsync(link);
    }

    /// <summary>The quick note window with a new note (<c>--quick-note</c>, M8-01); built on first use.</summary>
    public async Task ShowQuickNoteAsync()
    {
        if (_closing)
        {
            return;
        }

        _quickNote ??= new QuickNoteWindow(this, _vm);
        await _quickNote.ShowNewAsync();
    }

    /// <summary>Before the app ends: each external window closes, asking about unsaved changes. False = the user cancelled.</summary>
    private async Task<bool> CloseExternalWindowsAsync()
    {
        foreach (var window in _externalWindows.Values.ToList())
        {
            if (!await window.TryCloseAsync())
            {
                return false;
            }
        }

        return true;
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }

    private static void Add(Grid grid, Control child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private Control DayHeading()
    {
        var number = Typo.RisoNumber(out var setNumber, 86);
        var month = new TextBlock { FontFamily = Typo.Lead, FontSize = 23, LetterSpacing = 1.4, Foreground = Palette.InkSecondary };
        var weekday = new TextBlock { FontFamily = Typo.CondensedBold, FontSize = 32, LetterSpacing = 0.6, Foreground = Palette.Ink, Margin = new Thickness(0, -6, 0, 0) };
        var text = Ui.VStack(0, month, weekday);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = new Thickness(0, 6, 0, 0);
        Ui.Bind(_vm, nameof(MainViewModel.SelectedDay), () =>
        {
            var heading = NoteTexts.DayHeading(_vm.SelectedDay);
            setNumber(heading.Number);
            month.Text = heading.Month;
            weekday.Text = heading.Weekday;
        });
        var grid = Ui.Columns((Ui.Auto, number), (Ui.Star, text));
        grid.ColumnSpacing = 14;
        return grid;
    }

    /// <summary>Created time above the dotted line, the editor right of it (as in history rows).</summary>
    private Grid EditorPane()
    {
        var created = Typo.LeadTime(out var setCreated, 32, Palette.InkSecondary);
        created.VerticalAlignment = VerticalAlignment.Top;
        var modified = Typo.Caps(string.Empty, 12, Palette.InkTertiary);
        modified.HorizontalAlignment = HorizontalAlignment.Right;
        modified.TextAlignment = TextAlignment.Right;
        modified.TextWrapping = TextWrapping.Wrap;
        var continues = new Button
        {
            Content = Ui.HStack(4, Typo.Caps(Strings.Get(R.Editor_Continues).ToUpper(CultureInfo.CurrentCulture), 11.5, Palette.Accent), Icons.Create(Icons.ChevronDown, 12, Palette.Accent)),
            Padding = new Thickness(4, 2, 0, 2),
            Background = Palette.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsVisible = false,
        };
        continues.Click += (_, _) => _editor.ScrollToEnd();
        _editor.OverflowChanged += (_, more) => continues.IsVisible = more;
        void UpdateGutter()
        {
            var editor = _vm.Editor;
            setCreated(editor.CreatedAt);
            (modified.Text, _fullViewTimes.Text) = NoteTexts.Times(editor.CreatedAt, editor.ModifiedAt);
        }

        Ui.Bind(_vm.Editor, [nameof(NoteEditorViewModel.CreatedAt), nameof(NoteEditorViewModel.ModifiedAt)], UpdateGutter);
        // Notes that link to this one (M9-04): a small button under the time, its menu opens one of them.
        var backlinksText = Typo.Caps(string.Empty, 11.5, Palette.Accent);
        backlinksText.TextWrapping = TextWrapping.Wrap;
        backlinksText.TextAlignment = TextAlignment.Right;
        var backlinks = Ui.FlatButton(backlinksText, Palette.Transparent);
        backlinks.Padding = new Thickness(4, 2, 0, 2);
        backlinks.HorizontalAlignment = HorizontalAlignment.Right;
        backlinks.IsVisible = false;
        ToolTip.SetTip(backlinks, Strings.Get(R.Backlinks_Tooltip));
        Ui.Bind(_vm, nameof(MainViewModel.Backlinks), () =>
        {
            var notes = _vm.Backlinks;
            backlinks.IsVisible = notes.Count > 0;
            backlinksText.Text = Strings.Format(R.Backlinks_Button, notes.Count).ToUpper(CultureInfo.CurrentCulture);
            Avalonia.Automation.AutomationProperties.SetName(backlinks, Strings.Get(R.Backlinks_Tooltip) + ": " + notes.Count.ToString(CultureInfo.CurrentCulture));
            var menu = new MenuFlyout();
            foreach (var note in notes)
            {
                var item = new MenuItem { Header = NoteTexts.Backlink(note) };
                var path = note.RelativePath;
                item.Click += async (_, _) => await _vm.OpenNoteAsync(path);
                menu.Items.Add(item);
            }

            backlinks.Flyout = menu;
        });

        var gutter = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, Metrics.TimeGutterGap, 26) };
        var bottom = Ui.VStack(2, continues, modified);
        Grid.SetRow(bottom, 2);
        gutter.Children.Add(Ui.VStack(4, created, backlinks));
        gutter.Children.Add(bottom);

        _editor.ShowLineNumbers = _host.Settings.Current.EditorLineNumbers;
        _editor.PreviewCacheDirectory = _host.Paths.PreviewCacheDirectory;
        _editor.OpenImageTip = Strings.Get(R.Editor_OpenImage);
        _editor.PlaceholderText = Strings.Get(R.Editor_Placeholder);
        UpdateEditorTheme();
        Palette.Changed += (_, _) => UpdateEditorTheme();

        return Ui.Columns((Ui.Px(Metrics.TimeGutter), gutter), (Ui.Px(1), new DottedLine()), (Ui.Star, _editor));
    }

    /// <summary>Under the day heading in full view: the note's times (the gutter is hidden) and the way back.</summary>
    private Border FullViewBar()
    {
        var title = Ui.HStack(6, Icons.Create(Icons.Expand, 12, Palette.InkSecondary), Typo.Caps(Strings.Get(R.FullView_Title).ToUpper(CultureInfo.CurrentCulture), 12, Palette.InkSecondary));
        title.VerticalAlignment = VerticalAlignment.Center;
        _fullViewTimes.VerticalAlignment = VerticalAlignment.Center;
        _fullViewTimes.TextTrimming = TextTrimming.CharacterEllipsis;
        var close = Ui.IconButton(Icons.Create(Icons.Close, 14, Palette.InkTertiary), Strings.Get(OperatingSystem.IsMacOS() ? R.FullView_ExitMac : R.FullView_Exit), () => _vm.IsFullView = false);
        var row = Ui.Columns((Ui.Auto, title), (Ui.Star, _fullViewTimes), (Ui.Auto, close));
        row.ColumnSpacing = 16;
        return new Border
        {
            Child = row,
            Background = Palette.SubtleFill,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 0, 0, 0),
            Margin = new Thickness(0, 0, 0, 10),
            IsVisible = false,
        };
    }

    /// <summary>
    /// What the window shows under the top bar: everything; the note alone (full view, F11 – the date row, tag
    /// rail, calendar, history and time gutter make room for it); or the month with the history and no note (the
    /// calendar expanded, M6-08, as on Android). Full view wins over the month.
    /// </summary>
    private void WireLayout(Grid main, Border bar, Grid editorPane, Control editorCell, Control noteBar, Control splitter, Control[] hiddenInFullView)
    {
        var gutterWidth = editorPane.ColumnDefinitions[0].Width;
        var lineWidth = editorPane.ColumnDefinitions[1].Width;
        var editorRow = main.RowDefinitions[Grid.GetRow(editorCell)];
        var editorHeight = editorRow.Height;
        var editorMinHeight = editorRow.MinHeight;
        var historyHeight = _historyRow.Height;
        var margin = main.Margin;
        var (wasFull, wasMonth) = (false, false);
        void Apply()
        {
            var full = _vm.IsFullView;
            var month = _vm.IsCalendarExpanded && !full;
            if (full == wasFull && month == wasMonth)
            {
                return; // also the first calls, before any change
            }

            if (!wasFull && !wasMonth)
            {
                historyHeight = _historyRow.Height; // the splitter may have moved it
            }

            (wasFull, wasMonth) = (full, month);
            bar.IsVisible = full;
            foreach (var control in hiddenInFullView)
            {
                control.IsVisible = !full;
            }

            foreach (var control in editorPane.Children.Take(2))
            {
                control.IsVisible = !full; // created/edited times and the dotted line
            }

            splitter.IsVisible = !full && !month;
            noteBar.IsVisible = !month;
            editorCell.IsVisible = !month;
            editorRow.MinHeight = month ? 0 : editorMinHeight;
            editorRow.Height = month ? new GridLength(0) : editorHeight;
            _historyRow.Height = full ? new GridLength(0) : month ? new GridLength(1, GridUnitType.Star) : historyHeight;
            editorPane.ColumnDefinitions[0].Width = full ? new GridLength(0) : gutterWidth;
            editorPane.ColumnDefinitions[1].Width = full ? new GridLength(0) : lineWidth;
            // The editor keeps a gap to the dotted line of the time gutter (its left padding); without the gutter
            // that gap would be an indent under the full view bar – the line numbers start where the bar does.
            editorPane.Margin = new Thickness(full ? -18 : 0, 0, 0, 0);
            // The left margin is wider than the right one because the tag rail stands on the right; without the
            // rail both sides get the same.
            main.Margin = full ? new Thickness(margin.Right, margin.Top, margin.Right, margin.Bottom) : margin;
            if (!month)
            {
                _editor.FocusEditor();
            }
            else
            {
                // After the month is drawn: otherwise the focus falls to whatever comes next, the first tag tab.
                Dispatcher.UIThread.Post(_calendar.TakeFocus, DispatcherPriority.Background);
            }
        }

        Ui.Bind(_vm, nameof(MainViewModel.IsFullView), Apply);
        Ui.Bind(_vm, nameof(MainViewModel.IsCalendarExpanded), Apply);
    }

    /// <summary>Ctrl + / Ctrl − / Ctrl 0 in the editor: the font size setting, saved once the zooming stops.</summary>
    internal void ZoomEditor(int step)
    {
        if (_vm.ZoomEditor(step == 0 ? null : step))
        {
            UpdateEditorTheme();
        }
    }

    private void UpdateEditorTheme()
    {
        ApplyEditorTheme(_editor, _host);
        foreach (var window in _externalWindows.Values)
        {
            ApplyEditorTheme(window.Editor, _host);
        }

        _quickNote?.ApplyEditorTheme();
    }

    /// <summary>Markdown colours for the current light/dark palette; fonts and size from the settings.</summary>
    internal static void ApplyEditorTheme(MarkdownTextEditor editor, AppHost host)
    {
        var s = host.Settings.Current;
        Func<string, Color> tagColor = tag => Palette.ToColor(host.Tags.GetColor(tag));
        var theme = Palette.IsDark ? EditorTheme.Dark(tagColor) : EditorTheme.Light(tagColor);
        // Settings hold Windows font names; unknown ones fall back to the platform's fonts.
        var mono = new FontFamily(s.EditorMonoFontFamily + ", " + theme.MonoFont);
        editor.MarkdownTheme = theme with
        {
            FontSize = s.EditorFontSize,
            Font = s.EditorMonospace ? mono : new FontFamily(s.EditorFontFamily + ", " + FontFamily.DefaultFontFamilyName),
            MonoFont = mono,
        };
        editor.ShowLineNumbers = s.EditorLineNumbers;
        editor.MaxLineLength = s.EditorMaxLineLength;
    }

    private void WireEditor()
    {
        var editorVm = _vm.Editor;
        _editor.Mode = editorVm.Mode;
        editorVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NoteEditorViewModel.Mode))
            {
                _editor.Mode = editorVm.Mode;
            }
        };
        _editor.ModeToggleRequested += (_, _) => editorVm.Mode = editorVm.Mode == EditorMode.Raw ? EditorMode.Formatted : EditorMode.Raw;
        _editor.ImagePasteHandler = async (data, extension) =>
        {
            var day = editorVm.Day;
            return await Task.Run(() => _host.Engine.SaveAssetAsync(day, DateTime.Now, extension, data, CancellationToken.None));
        };
        editorVm.LoadRequested += (_, request) =>
        {
            // Image links resolve against the note's folder (assets/ of its day).
            _editor.NotesRoot = _host.Engine.Root;
            _editor.NoteDirectory = editorVm.Path is { } notePath
                ? Path.GetDirectoryName(_host.Engine.GetFullPath(notePath))
                : _host.Engine.GetDayDirectory(editorVm.Day);
            _editor.Load(request.Text, request.CaretToEnd, editorVm.IsReadOnly);
            _editor.FocusEditor();
            if (request.CaretOffset is { } offset)
            {
                _editor.Select(offset, 0);
            }
        };
        // A task ticked off in the list: an edit like typing, the selection left where it is.
        editorVm.EditRequested += (_, edit) =>
        {
            var (start, length) = _editor.SelectedRange;
            _editor.ApplyEdit(edit with { SelectionStart = start, SelectionLength = length });
        };
        _editor.TextEdited += (_, text) => editorVm.OnTextEdited(text);
        _editor.NoteLinkRequested += (_, link) => _ = _vm.OpenWikiLinkAsync(link);
        _editor.FontZoomRequested += (_, step) => ZoomEditor(step);
        _editor.EditorLostFocus += (_, _) => _ = editorVm.FlushAsync();
    }

    private void WireLifecycle()
    {
        _tick.Tick += (_, _) => _ = _vm.TickAsync();
        _tick.Start();
        Deactivated += (_, _) => _ = _vm.Editor.FlushAsync();
        Closing += async (_, e) =>
        {
            if (_closing)
            {
                return;
            }

            // Save first, then close for real (the window must not vanish with unsaved text).
            e.Cancel = true;
            if (_tray is not null && !_exitRequested)
            {
                // Close to the tray: save like on exit, keep running hidden.
                await _vm.Editor.FlushAsync();
                SavePlacement();
                await _host.SaveSettingsAsync();
                Hide();
                return;
            }

            if (!await CloseExternalWindowsAsync())
            {
                _exitRequested = false;
                return;
            }

            _closing = true;
            _tick.Stop();
            try
            {
                if (_quickNote is not null)
                {
                    await _quickNote.CloseForExitAsync();
                }

                await _vm.Editor.FlushAsync(leaving: true);
                SavePlacement();
                _host.Settings.Current.HistoryPaneHeight = _historyRow.ActualHeight;
                await _host.SaveSettingsAsync();
                _tray?.Dispose();
                await _host.DisposeAsync();
            }
            finally
            {
                Close();
            }
        };
    }

    /// <summary>
    /// [✎ WOLFMARK] [⌕ search box  Ctrl+K] [⚙] – the title row of the Windows window, as three columns: the search
    /// box is centred and at most 640 px wide, and shrinks on narrow windows instead of covering the gear.
    /// </summary>
    private Control TopBar()
    {
        var logo = Ui.HStack(8,
            Icons.Create(Icons.Pencil, 16, Palette.Accent),
            new TextBlock { Text = Strings.Get(R.AppTitle).ToUpperInvariant(), FontFamily = Typo.CondensedBold, FontSize = 19, Foreground = Palette.InkSecondary, VerticalAlignment = VerticalAlignment.Center });
        logo.VerticalAlignment = VerticalAlignment.Center;
        logo.Margin = new Thickness(20, 0, 0, 0);

        var placeholder = Typo.Body(Strings.Get(R.Search_Placeholder), 14, Palette.InkTertiary);
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        var shortcut = Typo.Body(Strings.Keys("Ctrl+K"), 12, Palette.InkTertiary);
        shortcut.VerticalAlignment = VerticalAlignment.Center;
        var inside = Ui.Columns((Ui.Auto, Icons.Create(Icons.Search, 16, Palette.InkTertiary)), (Ui.Star, placeholder), (Ui.Auto, shortcut));
        inside.ColumnSpacing = 10;
        var search = new Button
        {
            Content = inside,
            MaxWidth = 640,
            Height = 36,
            Padding = new Thickness(16, 0),
            Background = Palette.WindowTint,
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(18),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24, 0),
        };
        placeholder.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(search, Strings.Get(R.Toolbar_Search));
        search.Click += (_, _) => _vm.OpenSearch();

        var settings = Ui.IconButton(Icons.Create(Icons.Settings, 20, Palette.InkSecondary), Strings.Get(R.Toolbar_Settings), () => _ = OpenSettingsAsync());
        settings.Margin = new Thickness(0, 0, 16, 0);
        settings.VerticalAlignment = VerticalAlignment.Center;

        var bar = Ui.Columns((Ui.Auto, logo), (Ui.Star, search), (Ui.Auto, settings));
        bar.Height = 56;
        return bar;
    }

    /// <summary>Bottom-right corner of an imported Claude Code note: read-only, and a way to continue in an own note.</summary>
    private Control ClaudeBadge()
    {
        var editor = _vm.Editor;
        var copy = Ui.FlatButton(Typo.Caps(Strings.Get(R.ReadOnly_CopyButton), 11, Palette.Accent), Palette.Transparent);
        copy.Padding = new Thickness(6, 2);
        copy.Click += (_, _) => _ = editor.CopyToOwnNoteAsync();
        var label = Ui.HStack(6, Icons.Create(Icons.Lock, 11, Palette.InkTertiary), Typo.Caps(Strings.Get(R.ReadOnly_ClaudeTitle), 11, Palette.InkTertiary));
        label.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(label, Strings.Get(R.ReadOnly_ClaudeMessage));
        var badge = new Border
        {
            Child = Ui.HStack(8, label, copy),
            Background = Palette.WindowTint,
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 2, 2, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 8, 13),
        };
        Ui.Bind(editor, nameof(NoteEditorViewModel.IsClaudeCode), () => badge.IsVisible = editor.IsClaudeCode);
        return badge;
    }

    private void WireOverlays() =>
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsSearchOpen))
            {
                _search.IsVisible = _vm.IsSearchOpen;
                if (_vm.IsSearchOpen)
                {
                    _search.FocusInput();
                }
                else
                {
                    _editor.FocusEditor();
                }
            }
        };

    /// <summary>Window shortcuts (the editor's own come first, e.g. Ctrl+K with a selection makes a link).</summary>
    private void WireShortcuts() =>
        KeyDown += async (_, e) =>
        {
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            Func<Task>? action = (e.Key, ctrl, shift, alt) switch
            {
                (Key.N, true, false, false) => _vm.NewNoteAsync,
                (Key.K, true, false, false) or (Key.F, true, true, false) => () =>
                {
                    _vm.OpenSearch();
                    return Task.CompletedTask;
                },
                (Key.C, true, true, false) => async () =>
                {
                    await _vm.ToggleCalendarAsync();
                    // Once the month (or the week band) is laid out: arrows and Enter work right away.
                    Dispatcher.UIThread.Post(_calendar.FocusSelectedDay, DispatcherPriority.Background);
                },
                (Key.OemComma, true, false, false) => OpenSettingsAsync,
                // Find and replace in the open note; Ctrl+Cmd+F on macOS is the full view (below).
                (Key.F, true, false, false) when !IsMacFullViewChord(e) => () => OpenFind(replace: false),
                (Key.H, true, false, false) => () => OpenFind(replace: true),
                (Key.F3, false, _, false) when _vm.Find.IsOpen => () =>
                {
                    _findBar.Go(backwards: shift);
                    return Task.CompletedTask;
                },
                (Key.Left, true, false, true) => () => _vm.MoveCalendarAsync(-1),
                (Key.Right, true, false, true) => () => _vm.MoveCalendarAsync(1),
                (Key.Escape, false, false, false) when _vm.IsSearchOpen => () =>
                {
                    _vm.IsSearchOpen = false;
                    return Task.CompletedTask;
                },
                (Key.Escape, false, false, false) when _vm.IsFullView => () =>
                {
                    _vm.IsFullView = false;
                    return Task.CompletedTask;
                },
                (Key.F11, false, false, false) or (Key.F, true, false, false) when e.Key == Key.F11 || IsMacFullViewChord(e) => () =>
                {
                    _vm.IsFullView = !_vm.IsFullView;
                    return Task.CompletedTask;
                },
                _ => null,
            };
            if (action is not null)
            {
                e.Handled = true;
                await action();
            }
        };

    /// <summary>Ctrl+Cmd+F – the macOS full screen chord (F11 is the system's Show Desktop there).</summary>
    private static bool IsMacFullViewChord(KeyEventArgs e) =>
        OperatingSystem.IsMacOS() && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Meta);

    private bool _settingsOpen;

    private async Task OpenSettingsAsync()
    {
        if (_settingsOpen)
        {
            return;
        }

        _settingsOpen = true;
        try
        {
            await new SettingsWindow(_vm, ApplyAppearance, ApplyIcon).ShowDialog(this);
        }
        catch (Exception ex)
        {
            // Fire-and-forget callers (the shortcut, the tray): without this the window just silently does not appear.
            LogSettingsFailed(_host.Loggers.CreateLogger(nameof(MainWindow)), ex);
            _vm.ErrorNotice = Strings.Format(R.Error_Settings, ex.Message);
        }
        finally
        {
            _settingsOpen = false;
        }
    }

    [Microsoft.Extensions.Logging.LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Opening settings failed")]
    private static partial void LogSettingsFailed(Microsoft.Extensions.Logging.ILogger logger, Exception ex);

    private Task OpenFind(bool replace)
    {
        _findBar.Open(replace);
        return Task.CompletedTask;
    }

    /// <summary>After the settings: theme, editor fonts, line numbers, tray.</summary>
    internal void ApplyAppearance()
    {
        var s = _host.Settings.Current;
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = s.Theme switch
            {
                AppTheme.Light => Avalonia.Styling.ThemeVariant.Light,
                AppTheme.Dark => Avalonia.Styling.ThemeVariant.Dark,
                _ => Avalonia.Styling.ThemeVariant.Default,
            };
        }

        UpdateEditorTheme();
        ApplyTray();
    }

    /// <summary>A tray icon exists while "close to tray" is on (not in demo mode).</summary>
    private void ApplyTray()
    {
        if (!_host.Settings.Current.CloseToTray || LaunchOptions.Current.IsDemo)
        {
            _tray?.Dispose();
            _tray = null;
            return;
        }

        if (_tray is not null)
        {
            return;
        }

        NativeMenuItem Item(string text, Action action)
        {
            var item = new NativeMenuItem(text);
            item.Click += (_, _) => action();
            return item;
        }

        _tray = new TrayIcon
        {
            Icon = Icon,
            ToolTipText = Strings.Get(R.AppTitle),
            Menu =
            [
                Item(Strings.Get(R.Tray_Show), BringToFront),
                Item(Strings.Get(R.Tray_Settings), () =>
                {
                    BringToFront();
                    _ = OpenSettingsAsync();
                }),
                Item(Strings.Get(R.Tray_Exit), Exit),
            ],
        };
        _tray.Clicked += (_, _) => BringToFront();
    }

    private void RestorePlacement()
    {
        if (_host.Settings.Current.Window is { Width: > 300, Height: > 300 } w)
        {
            Width = w.Width;
            Height = w.Height;
            Position = new PixelPoint(w.X, w.Y);
            // The monitor it was on may be gone or smaller now: back onto a screen, as the Windows app does.
            if ((Screens.ScreenFromPoint(Position) ?? Screens.Primary) is { } screen)
            {
                var area = screen.WorkingArea;
                var scale = screen.Scaling > 0 ? screen.Scaling : 1;
                var fit = NoteTexts.FitWindow(w.X, w.Y, (int)(w.Width * scale), (int)(w.Height * scale), area.X, area.Y, area.Width, area.Height);
                Width = fit.Width / scale;
                Height = fit.Height / scale;
                Position = new PixelPoint(fit.X, fit.Y);
            }

            WindowStartupLocation = WindowStartupLocation.Manual;
            if (w.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        else
        {
            Width = 1180;
            Height = 900;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void SavePlacement()
    {
        var maximized = WindowState == WindowState.Maximized;
        var previous = _host.Settings.Current.Window;
        _host.Settings.Current.Window = maximized && previous is not null
            ? new WindowPlacement { X = previous.X, Y = previous.Y, Width = previous.Width, Height = previous.Height, IsMaximized = true }
            : new WindowPlacement
            {
                X = Position.X,
                Y = Position.Y,
                Width = (int)Width,
                Height = (int)Height,
                IsMaximized = maximized,
            };
    }
}
