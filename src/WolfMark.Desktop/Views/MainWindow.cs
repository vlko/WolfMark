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
using WolfMark.Core.Settings;
using WolfMark.Desktop.Editor;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// Main window, laid out like the Windows one (src/WolfMark.App/Views/MainWindow.cs): day heading, week band,
/// note bar, editor with its time gutter, history and status bar on one flat paper surface.
/// </summary>
internal sealed class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppHost _host;
    private readonly MarkdownTextEditor _editor = new();
    private readonly SearchPalette _search;
    private TrayIcon? _tray;
    private bool _exitRequested;
    private readonly RowDefinition _historyRow;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly TextBlock _fullViewTimes = Typo.Caps(string.Empty, 12, Palette.InkTertiary);
    private readonly DispatcherTimer _zoomSave = new() { Interval = TimeSpan.FromSeconds(1) };
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
        main.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // day heading
        main.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // full view bar
        main.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // calendar
        main.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // note bar
        main.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star) { MinHeight = 120 });
        main.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // splitter
        main.RowDefinitions.Add(_historyRow);

        var heading = DayHeading();
        heading.Margin = new Thickness(0, 0, 0, 14);
        var calendar = new CalendarPanel(vm) { Margin = new Thickness(0, 0, 0, 12) };
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
        var fullViewBar = FullViewBar();
        var history = new HistoryView(vm);
        Add(main, heading, 0);
        Add(main, fullViewBar, 1);
        Add(main, calendar, 2);
        Add(main, noteBar, 3);
        Add(main, editorCell, 4);
        Add(main, splitter, 5);
        Add(main, history, 6);

        var rail = new TagRail(vm) { Margin = new Thickness(0, 8, 0, 0) };
        var content = Ui.Columns((Ui.Star, main), (Ui.Auto, rail));
        WireFullView(fullViewBar, editorPane, [calendar, splitter, history, rail]);
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
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://WolfMark.Desktop/Assets/WolfMark-256.png")));

        WireEditor();
        WireOverlays();
        WireShortcuts();
        WireLifecycle();
        ApplyTray();
    }

    public async Task InitializeAsync()
    {
        await _vm.InitializeAsync();
        _editor.FocusEditor();
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
            var d = _vm.SelectedDay;
            var c = CultureInfo.CurrentCulture;
            setNumber(d.Day.ToString(c));
            month.Text = (c.DateTimeFormat.GetMonthName(d.Month) + " " + d.Year).ToUpper(c);
            weekday.Text = c.DateTimeFormat.GetDayName(d.DayOfWeek).ToUpper(c);
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
            var c = CultureInfo.CurrentCulture;
            var created = Strings.Format(R.FullView_Created, editor.CreatedAt.ToString("t", c));
            if (editor.ModifiedAt is { } m)
            {
                var when = DateOnly.FromDateTime(m) == DateOnly.FromDateTime(editor.CreatedAt) ? m.ToString("t", c) : m.ToString("d", c) + " " + m.ToString("t", c);
                modified.Text = Strings.Format(R.Editor_Modified, when).ToUpper(c);
                _fullViewTimes.Text = (created + " · " + Strings.Format(R.Editor_Modified, when)).ToUpper(c);
            }
            else
            {
                modified.Text = string.Empty;
                _fullViewTimes.Text = created.ToUpper(c);
            }
        }

        Ui.Bind(_vm.Editor, [nameof(NoteEditorViewModel.CreatedAt), nameof(NoteEditorViewModel.ModifiedAt)], UpdateGutter);
        var gutter = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, Metrics.TimeGutterGap, 26) };
        var bottom = Ui.VStack(2, continues, modified);
        Grid.SetRow(bottom, 2);
        gutter.Children.Add(created);
        gutter.Children.Add(bottom);

        _editor.ShowLineNumbers = _host.Settings.Current.EditorLineNumbers;
        _editor.PreviewCacheDirectory = _host.Paths.PreviewCacheDirectory;
        _editor.OpenImageTip = Strings.Get(R.Editor_OpenImage);
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
        var close = Ui.IconButton(Icons.Create(Icons.Close, 14, Palette.InkTertiary), Strings.Get(R.FullView_Exit), () => _vm.IsFullView = false);
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

    /// <summary>Full view (F11): the tag rail, calendar, history and time gutter make room for the note.</summary>
    private void WireFullView(Border bar, Grid editorPane, Control[] hidden)
    {
        var gutterWidth = editorPane.ColumnDefinitions[0].Width;
        var lineWidth = editorPane.ColumnDefinitions[1].Width;
        var historyHeight = _historyRow.Height;
        Ui.Bind(_vm, nameof(MainViewModel.IsFullView), () =>
        {
            var full = _vm.IsFullView;
            if (full == bar.IsVisible)
            {
                return; // the first call, before any change
            }

            bar.IsVisible = full;
            foreach (var control in hidden)
            {
                control.IsVisible = !full;
            }

            foreach (var control in editorPane.Children.Take(2))
            {
                control.IsVisible = !full; // created/edited times and the dotted line
            }

            if (full)
            {
                historyHeight = _historyRow.Height; // the splitter may have moved it
            }

            _historyRow.Height = full ? new GridLength(0) : historyHeight;
            editorPane.ColumnDefinitions[0].Width = full ? new GridLength(0) : gutterWidth;
            editorPane.ColumnDefinitions[1].Width = full ? new GridLength(0) : lineWidth;
            _editor.FocusEditor();
        });
    }

    /// <summary>Ctrl + / Ctrl − / Ctrl 0 in the editor: the font size setting, saved once the zooming stops.</summary>
    private void ZoomEditor(int step)
    {
        var s = _host.Settings.Current;
        var size = step == 0 ? LocalSettings.EditorFontSizeDefault : LocalSettings.ZoomFontSize(s.EditorFontSize, step);
        if (size == s.EditorFontSize)
        {
            return;
        }

        s.EditorFontSize = size;
        UpdateEditorTheme();
        _zoomSave.Stop();
        _zoomSave.Start();
    }

    /// <summary>Markdown colours for the current light/dark palette; fonts and size from the settings.</summary>
    private void UpdateEditorTheme()
    {
        var s = _host.Settings.Current;
        Func<string, Color> tagColor = tag => Palette.ToColor(_host.Tags.GetColor(tag));
        var theme = Palette.IsDark ? EditorTheme.Dark(tagColor) : EditorTheme.Light(tagColor);
        // Settings hold Windows font names; unknown ones fall back to the platform's fonts.
        var mono = new FontFamily(s.EditorMonoFontFamily + ", " + theme.MonoFont);
        _editor.MarkdownTheme = theme with
        {
            FontSize = s.EditorFontSize,
            Font = s.EditorMonospace ? mono : new FontFamily(s.EditorFontFamily + ", " + FontFamily.DefaultFontFamilyName),
            MonoFont = mono,
        };
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
        };
        _editor.TextEdited += (_, text) => editorVm.OnTextEdited(text);
        _editor.FontZoomRequested += (_, step) => ZoomEditor(step);
        _zoomSave.Tick += (_, _) =>
        {
            _zoomSave.Stop();
            _ = _host.SaveSettingsAsync();
        };
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

            _closing = true;
            _tick.Stop();
            try
            {
                await _vm.Editor.FlushAsync();
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
        var shortcut = Typo.Body("Ctrl+K", 12, Palette.InkTertiary);
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
            Margin = new Thickness(0, 0, 16, 26),
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
                (Key.C, true, true, false) => _vm.ToggleCalendarAsync,
                (Key.OemComma, true, false, false) => OpenSettingsAsync,
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
                (Key.F11, false, false, false) => () =>
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
            await new SettingsWindow(_vm, ApplyAppearance).ShowDialog(this);
        }
        finally
        {
            _settingsOpen = false;
        }
    }

    /// <summary>After the settings: theme, editor fonts, line numbers, tray.</summary>
    private void ApplyAppearance()
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

        _editor.ShowLineNumbers = s.EditorLineNumbers;
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
                Item(Strings.Get(R.Tray_Exit), () =>
                {
                    _exitRequested = true;
                    Close();
                }),
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
