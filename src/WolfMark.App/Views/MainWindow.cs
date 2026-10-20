using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;
using WolfMark.Editor;

namespace WolfMark.App.Views;

/// <summary>
/// Main window (SPEC §4.5, ADR 0004): flat paper surface with the title bar, day heading, calendar panel,
/// note toolbar, editor with its time gutter, history, and the tag rail on the right. Only the search
/// palette floats above.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly TagBrushes _brushes;
    private readonly AppHost _host;
    private readonly Grid _root = new();
    private readonly Grid _titleBar;
    private readonly List<FrameworkElement> _titleBarInteractive = [];
    private readonly IMarkdownEditor _editor = LaunchOptions.Current.CanvasEditor
        ? new WolfMark.Editor.Win2D.CanvasMarkdownEditor()
        : new MarkdownEditor();
    private readonly CalendarPanel _calendar;
    private readonly SearchPalette _search;
    private readonly Action<DateTime?> _setCreated;
    private readonly TextBlock _modified = Typo.Caps(string.Empty, 12, Palette.InkTertiary);
    private readonly Button _continues;
    private readonly RowDefinition _historyRow;
    private bool _closing;
    private bool _exitRequested;

    /// <summary>For <see cref="ScreenshotRunner"/>: the window content and its view model.</summary>
    internal Grid RootElement => _root;

    internal MainViewModel ViewModel => _vm;

    internal void ScrollEditorToStart() => _editor.ScrollToStart();
    private bool _settingsOpen;
    private TrayIcon? _tray;

    public MainWindow(MainViewModel vm, TagBrushes brushes)
    {
        _vm = vm;
        _brushes = brushes;
        _host = vm.Host;
        Title = Strings.Get(R.AppTitle);

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ApplyIcon(_host.Settings.Current.AppIcon);

        // Palette first, so every view is built with the right colours.
        Palette.Apply(InitialTheme());

        _titleBar = BuildTitleBar();
        _calendar = new CalendarPanel(vm, brushes);
        _search = new SearchPalette(vm, brushes) { Visibility = Visibility.Collapsed };

        _historyRow = new RowDefinition { Height = new GridLength(_host.Settings.Current.HistoryPaneHeight) };
        var header = new NoteHeaderView(vm, brushes, _editor);
        var splitter = new SplitterHandle(
            () => _historyRow.Height.Value,
            h => _historyRow.Height = new GridLength(h),
            () =>
            {
                _host.Settings.Current.HistoryPaneHeight = _historyRow.Height.Value;
                _ = _host.SaveSettingsAsync();
            });
        var history = new HistoryView(vm, brushes);

        // Editor with time gutter: created time on top, last modification on the last visible line.
        var created = Typo.LeadTime(out _setCreated, 32, Palette.InkSecondary);
        created.Margin = new Thickness(0, 4, 0, 0);
        _modified.HorizontalAlignment = HorizontalAlignment.Right;
        _modified.TextAlignment = TextAlignment.Right;
        _modified.TextWrapping = TextWrapping.Wrap;
        _continues = new Button
        {
            Content = Ui.Ui.HStack(4, Typo.Caps(Strings.Get(R.Editor_Continues), 11.5, Palette.Accent), Typo.Icon(Glyphs.ChevronDown, 10, Palette.Accent)),
            Padding = new Thickness(4, 2, 0, 2),
            CornerRadius = Radius.Control,
            HorizontalAlignment = HorizontalAlignment.Right,
            Visibility = Visibility.Collapsed,
        };
        _continues.StyleButton(Palette.Transparent, Palette.Accent);
        _continues.Click += (_, _) => _editor.ScrollToEnd();
        var gutter = Ui.Ui.Rows((Ui.Ui.Auto, created), (Ui.Ui.Star, null), (Ui.Ui.Auto, Ui.Ui.VStack(2, _continues, _modified)));
        gutter.Padding = new Thickness(0, 0, Metrics.TimeGutterGap, 26);
        _editor.AlignLeft = true;
        // Same gutter width as history rows: the two dotted lines form one line.
        var editorCell = new Grid();
        editorCell.Children.Add(_editor.View);
        editorCell.Children.Add(ClaudeBadge());
        var editorPane = Ui.Ui.Columns((Ui.Ui.Px(Metrics.TimeGutter), gutter), (Ui.Ui.Px(1), Ui.Ui.DottedLine()), (Ui.Ui.Star, editorCell));

        var main = new Grid { Padding = new Thickness(24, 4, 8, 0) };
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // day heading
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // calendar panel
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // note panel (tag, formatting)
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(_historyRow);
        header.Heading.Margin = new Thickness(0, 0, 0, 10);
        _calendar.Margin = new Thickness(0, 0, 0, 12);
        header.NotePanel.Margin = new Thickness(0, 0, 0, 10);
        Add(main, header.Heading, 0);
        Add(main, _calendar, 1);
        Add(main, header.NotePanel, 2);
        Add(main, editorPane, 3);
        Add(main, splitter, 4);
        Add(main, history, 5);

        var rail = new TagRail(vm, brushes) { Margin = new Thickness(0, 8, 0, 0) };
        var content = Ui.Ui.Columns((Ui.Ui.Star, main), (Ui.Ui.Auto, rail));

        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // closable notices
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // status bar
        _root.Background = Palette.WindowTint;
        Add(_root, _titleBar, 0);
        Add(_root, content, 1);
        header.InfoBars.Margin = new Thickness(24, 0, 24, 6);
        Add(_root, header.InfoBars, 2);
        Add(_root, new StatusBar(vm), 3);
        Grid.SetRowSpan(_search, 4);
        _root.Children.Add(_search);
        Content = _root;
        SetTitleBar(_titleBar);

        WireEditor();
        WireOverlays();
        WireAccelerators();
        WireLifecycle();
        ApplyTray();
        ApplyAppearance();
        RestorePlacement();
    }

    /// <summary>Theme and editor settings (also re-applied after the settings dialog).</summary>
    public void ApplyAppearance()
    {
        var s = _host.Settings.Current;
        _root.RequestedTheme = s.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        OnThemeChanged();
    }

    /// <summary>Window/taskbar icon (Settings → Appearance → App icon); also used for the live preview.</summary>
    public void ApplyIcon(string? id)
    {
        var path = AppIcons.IcoPath(id);
        if (File.Exists(path))
        {
            AppWindow.SetIcon(path);
            _tray?.SetIcon(path);
        }
    }

    /// <summary>Notification-area icon exists while "close to tray" is on.</summary>
    public void ApplyTray()
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

        _tray = new TrayIcon(Win32Interop.GetWindowFromWindowId(AppWindow.Id), AppIcons.IcoPath(_host.Settings.Current.AppIcon), Strings.Get(R.AppTitle))
        {
            MenuTexts = () => (Strings.Get(R.Tray_Show), Strings.Get(R.Tray_Settings), Strings.Get(R.Tray_Exit)),
        };
        _tray.ShowRequested += (_, _) => BringToFront();
        _tray.SettingsRequested += (_, _) =>
        {
            BringToFront();
            _ = OpenSettingsAsync();
        };
        _tray.ExitRequested += (_, _) => _ = ExitAsync();
    }

    /// <summary>Ends the app (tray menu "Exit"), also when the window is hidden.</summary>
    public async Task ExitAsync()
    {
        if (_closing)
        {
            return;
        }

        _exitRequested = true;
        _closing = true;
        try
        {
            await ShutdownAsync();
        }
        finally
        {
            Close();
        }
    }

    private async Task ShutdownAsync()
    {
        await _vm.Editor.FlushAsync();
        SavePlacement();
        await _host.SaveSettingsAsync();
        _tray?.Dispose();
        _tray = null;
        await _host.DisposeAsync();
    }

    public void BringToFront()
    {
        if (!AppWindow.IsVisible)
        {
            AppWindow.Show();
        }

        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        Windows.Win32.PInvoke.SetForegroundWindow(new Windows.Win32.Foundation.HWND(Win32Interop.GetWindowFromWindowId(AppWindow.Id)));
    }

    public async Task InitializeAsync()
    {
        await _vm.InitializeAsync();
        _editor.FocusEditor();
    }

    private static void Add(Grid grid, FrameworkElement child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    // ---------------------------------------------------------------- title bar

    private Grid BuildTitleBar()
    {
        var appName = Typo.Caps(Strings.Get(R.AppTitle), 16, Palette.Ink);
        appName.FontFamily = Typo.CondensedBold;
        var searchBox = new Button
        {
            Content = Ui.Ui.Columns(
                (Ui.Ui.Auto, Typo.Icon(Glyphs.Search, 13, Palette.InkTertiary)),
                (Ui.Ui.Star, Typo.Body(Strings.Get(R.Search_Placeholder), 13, Palette.InkTertiary)),
                (Ui.Ui.Auto, Typo.Body("Ctrl+K", 11.5, Palette.InkTertiary))),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MaxWidth = 520,
            Height = 32,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(16),
        };
        searchBox.StyleButton(Palette.Card, Palette.InkTertiary);
        searchBox.BorderBrush = Palette.CardStroke;
        searchBox.Resources["ButtonBorderBrush"] = Palette.CardStroke;
        ((Grid)searchBox.Content).ColumnSpacing = 10;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(searchBox, Strings.Get(R.Toolbar_Search));
        searchBox.Click += (_, _) => _vm.OpenSearch();

        var settings = Ui.Ui.IconButton(Glyphs.Settings, Strings.Get(R.Toolbar_Settings), () => _ = OpenSettingsAsync());

        _titleBarInteractive.Add(searchBox);
        _titleBarInteractive.Add(settings);

        var grid = Ui.Ui.Columns(
            (Ui.Ui.Px(20), null),
            (Ui.Ui.Auto, Ui.Ui.HStack(10, Typo.Icon(Glyphs.Edit, 14, Palette.Accent), appName)),
            (Ui.Ui.Star, searchBox),
            (Ui.Ui.Auto, settings),
            (Ui.Ui.Px(150), null)); // caption buttons
        grid.ColumnSpacing = 16;
        grid.Background = Palette.Transparent;
        grid.SizeChanged += (_, _) => UpdateTitleBarRegions();
        grid.Loaded += (_, _) => UpdateTitleBarRegions();
        return grid;
    }

    /// <summary>Interactive elements inside the drag region need passthrough rectangles.</summary>
    private void UpdateTitleBarRegions()
    {
        if (_titleBar.XamlRoot is null)
        {
            return;
        }

        var scale = _titleBar.XamlRoot.RasterizationScale;
        var rects = new List<RectInt32>();
        foreach (var element in _titleBarInteractive)
        {
            if (element.ActualWidth <= 0)
            {
                continue;
            }

            var bounds = element.TransformToVisual(null).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
            rects.Add(new RectInt32(
                (int)Math.Round(bounds.X * scale),
                (int)Math.Round(bounds.Y * scale),
                (int)Math.Round(bounds.Width * scale),
                (int)Math.Round(bounds.Height * scale)));
        }

        InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(NonClientRegionKind.Passthrough, [.. rects]);
    }

    private ElementTheme InitialTheme() => _host.Settings.Current.Theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light,
    };

    private void OnThemeChanged()
    {
        var theme = _root.ActualTheme;
        Palette.Apply(theme);
        UpdateEditorTheme();
        _brushes.Refresh(); // views that compute colours re-render

        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = Palette.Ink.Color;
        tb.ButtonHoverBackgroundColor = Palette.SubtleFill.Color;
        tb.ButtonHoverForegroundColor = Palette.Ink.Color;
        tb.ButtonInactiveForegroundColor = Palette.InkTertiary.Color;

        if (_host.Settings.Current.WindowRendering == WindowRendering.ClassBackground)
        {
            WindowBackground.SetClassBackground(Win32Interop.GetWindowFromWindowId(AppWindow.Id), Palette.WindowTint.Color);
        }
    }

    private void UpdateEditorTheme()
    {
        var theme = Palette.CurrentTheme;
        var s = _host.Settings.Current;
        Func<string, Windows.UI.Color> tagColor = tag => Palette.ToColor(_host.Tags.GetColor(tag));
        var baseTheme = theme == ElementTheme.Dark ? MarkdownTheme.Dark(tagColor) : MarkdownTheme.Light(tagColor);
        // The chosen font first; XAML falls back to the defaults if it was uninstalled.
        var monoFont = string.IsNullOrWhiteSpace(s.EditorMonoFontFamily)
            ? baseTheme.MonoFontFamily
            : s.EditorMonoFontFamily.Trim() + ", " + baseTheme.MonoFontFamily;
        _editor.Theme = new MarkdownTheme
        {
            Text = baseTheme.Text,
            Muted = baseTheme.Muted,
            Heading = baseTheme.Heading,
            Link = baseTheme.Link,
            Code = baseTheme.Code,
            CodeBackground = baseTheme.CodeBackground,
            Quote = baseTheme.Quote,
            TaskOpen = baseTheme.TaskOpen,
            TaskDone = baseTheme.TaskDone,
            TagColor = tagColor,
            FontFamily = s.EditorMonospace ? monoFont : s.EditorFontFamily,
            MonoFontFamily = monoFont,
            FontSize = s.EditorFontSize,
        };
        _editor.ShowLineNumbers = s.EditorLineNumbers;
    }

    // ---------------------------------------------------------------- wiring

    private void WireEditor()
    {
        var editorVm = _vm.Editor;
        _editor.PlaceholderText = Strings.Get(R.Editor_Placeholder);
        if (_editor is WolfMark.Editor.Win2D.CanvasMarkdownEditor canvas)
        {
            canvas.MenuTexts = new WolfMark.Editor.Win2D.CanvasMenuTexts(
                Strings.Get(R.Menu_Cut),
                Strings.Get(R.Menu_Copy),
                Strings.Get(R.Menu_Paste),
                Strings.Get(R.Menu_SelectAll),
                Strings.Get(R.Spell_AddToDictionary),
                Strings.Get(R.Spell_Ignore),
                Strings.Get(R.Spell_NoSuggestions));
        }

        _editor.CommandIcon = FormatIcons.CreateForMenu;
        _editor.CommandText = command => command switch
        {
            EditorCommand.Paragraph => Strings.Get(R.Format_Paragraph),
            EditorCommand.Heading1 => Strings.Get(R.Format_H1),
            EditorCommand.Heading2 => Strings.Get(R.Format_H2),
            EditorCommand.Heading3 => Strings.Get(R.Format_H3),
            EditorCommand.Quote => Strings.Get(R.Format_Quote),
            EditorCommand.BulletList => Strings.Get(R.Format_Bullet),
            EditorCommand.NumberedList => Strings.Get(R.Format_Numbered),
            EditorCommand.Task => Strings.Get(R.Format_Task),
            EditorCommand.Bold => Strings.Get(R.Format_Bold),
            EditorCommand.Italic => Strings.Get(R.Format_Italic),
            EditorCommand.Strikethrough => Strings.Get(R.Format_Strike),
            EditorCommand.Code => Strings.Get(R.Format_Code),
            EditorCommand.Link => Strings.Get(R.Format_Link),
            EditorCommand.CopyPlainText => Strings.Get(R.Copy_Plain),
            EditorCommand.CopyHtml => Strings.Get(R.Copy_Html),
            _ => command.ToString(),
        };
        _editor.Mode = editorVm.Mode == EditorMode.Raw ? MarkdownEditorMode.Raw : MarkdownEditorMode.Formatted;
        _editor.TextEdited += (_, text) => editorVm.OnTextEdited(text);
        _editor.EditorLostFocus += (_, _) => _ = editorVm.FlushAsync();
        _editor.ModeChanged += (_, mode) => editorVm.Mode = mode == MarkdownEditorMode.Raw ? EditorMode.Raw : EditorMode.Formatted;
        _editor.ImagePasteHandler = async (data, extension) =>
        {
            var day = editorVm.Day;
            return await Task.Run(() => _host.Engine.SaveAssetAsync(day, DateTime.Now, extension, data, CancellationToken.None));
        };
        _editor.DropImageCaption = Strings.Get(R.Editor_DropImage);
        _editor.OpenImageTip = Strings.Get(R.Editor_OpenImage);
        _editor.PreviewCacheDirectory = _host.Paths.PreviewCacheDirectory;

        editorVm.LoadRequested += (_, request) =>
        {
            // Image links resolve against the note's folder (assets/ of its day).
            _editor.NotesRoot = _host.Engine.Root;
            _editor.NoteDirectory = editorVm.Path is { } notePath
                ? System.IO.Path.GetDirectoryName(_host.Engine.GetFullPath(notePath))
                : _host.Engine.GetDayDirectory(editorVm.Day);
            // Read-only goes with the text: switching it on its own (before the load) re-proofed the old note,
            // which was then replaced while the spell checker ran – a WinUIEdit crash.
            _editor.Load(request.Text, request.CaretToEnd, editorVm.IsReadOnly);
            _editor.FocusEditor();
            _editor.ScrollToCaret();
        };
        editorVm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(NoteEditorViewModel.Mode):
                    _editor.Mode = editorVm.Mode == EditorMode.Raw ? MarkdownEditorMode.Raw : MarkdownEditorMode.Formatted;
                    break;
                case nameof(NoteEditorViewModel.CreatedAt) or nameof(NoteEditorViewModel.ModifiedAt):
                    UpdateGutter();
                    break;
            }
        };
        _editor.OverflowChanged += (_, more) => _continues.Visibility = more ? Visibility.Visible : Visibility.Collapsed;
        UpdateGutter();

        _brushes.Changed += (_, _) => UpdateEditorTheme();
        _host.Tags.Changed += (_, _) => UiThread.Post(_brushes.Refresh);
        _root.ActualThemeChanged += (_, _) => OnThemeChanged();
    }

    /// <summary>Created time on top of the gutter; "edited 14:20" on the last visible line.</summary>
    /// <summary>
    /// Bottom-right corner of an imported Claude Code note: where it came from, why it is read-only (tooltip)
    /// and a way to continue in an own note.
    /// </summary>
    private Border ClaudeBadge()
    {
        var editor = _vm.Editor;
        var copy = new Button
        {
            Content = Typo.Caps(Strings.Get(R.ReadOnly_CopyButton), 11, Palette.Accent),
            Padding = new Thickness(6, 2, 6, 2),
            CornerRadius = Radius.Control,
        };
        copy.StyleButton(Palette.Transparent, Palette.Accent);
        copy.Click += (_, _) => _ = editor.CopyToOwnNoteAsync();
        var label = Ui.Ui.HStack(6, Typo.Icon(Glyphs.Lock, 11, Palette.InkTertiary), Typo.Caps(Strings.Get(R.ReadOnly_ClaudeTitle), 11, Palette.InkTertiary));
        label.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(label, Strings.Get(R.ReadOnly_ClaudeMessage));
        var badge = new Border
        {
            Child = Ui.Ui.HStack(8, label, copy),
            Background = Palette.Card,
            BorderBrush = Palette.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = Radius.Tag,
            Padding = new Thickness(10, 2, 2, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 16, 26),
        };
        return badge.Bind(editor, nameof(NoteEditorViewModel.IsClaudeCode),
            () => badge.Visibility = editor.IsClaudeCode ? Visibility.Visible : Visibility.Collapsed);
    }

    private void UpdateGutter()
    {
        var editor = _vm.Editor;
        _setCreated(editor.CreatedAt);
        if (editor.ModifiedAt is { } modified)
        {
            var c = System.Globalization.CultureInfo.CurrentCulture;
            var when = DateOnly.FromDateTime(modified) == DateOnly.FromDateTime(editor.CreatedAt)
                ? modified.ToString("t", c)
                : modified.ToString("d", c) + " " + modified.ToString("t", c);
            _modified.Text = Strings.Format(R.Editor_Modified, when).ToUpper(c);
        }
        else
        {
            _modified.Text = string.Empty;
        }
    }

    private void WireOverlays()
    {
        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsSearchOpen):
                    _search.Visibility = _vm.IsSearchOpen ? Visibility.Visible : Visibility.Collapsed;
                    if (_vm.IsSearchOpen)
                    {
                        _search.FocusInput();
                    }
                    else
                    {
                        _editor.FocusEditor();
                    }

                    break;
            }
        };
    }

    private void WireAccelerators()
    {
        Accelerator(VirtualKey.N, VirtualKeyModifiers.Control, () => _ = _vm.NewNoteAsync());
        Accelerator(VirtualKey.K, VirtualKeyModifiers.Control, _vm.OpenSearch);
        Accelerator(VirtualKey.F, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, _vm.OpenSearch);
        Accelerator(VirtualKey.C, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, async () =>
        {
            await _vm.ToggleCalendarAsync();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, _calendar.FocusSelectedDay);
        });
        Accelerator(VirtualKey.E, VirtualKeyModifiers.Control, () =>
            _vm.Editor.Mode = _vm.Editor.Mode == EditorMode.Raw ? EditorMode.Formatted : EditorMode.Raw);
        Accelerator((VirtualKey)188, VirtualKeyModifiers.Control, () => _ = OpenSettingsAsync()); // Ctrl+,
        Accelerator(VirtualKey.Escape, VirtualKeyModifiers.None, () => _vm.IsSearchOpen = false);
        Accelerator(VirtualKey.Left, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, () => _ = _vm.MoveCalendarAsync(-1));
        Accelerator(VirtualKey.Right, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, () => _ = _vm.MoveCalendarAsync(1));

        void Accelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, e) =>
            {
                e.Handled = true;
                action();
            };
            _root.KeyboardAccelerators.Add(accelerator);
        }

        _root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }

    private void WireLifecycle()
    {
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                _ = _vm.Editor.FlushAsync();
            }
            else if (SystemBackdrop is null && _host.Settings.Current.WindowRendering == WindowRendering.MicaAfterActivation)
            {
                // Not a visible feature – the paper is opaque. Without a system backdrop, areas exposed
                // while resizing flash black. Set only once the window is shown: assigned in the
                // constructor (before activation) the resize flicker stayed.
                SystemBackdrop = new MicaBackdrop();
            }
        };

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMinutes(1);
        timer.Tick += (_, _) => _ = _vm.TickAsync();
        timer.Start();

        AppWindow.Closing += async (_, e) =>
        {
            if (_closing)
            {
                return;
            }

            e.Cancel = true;
            if (_tray is not null && !_exitRequested)
            {
                // Close to the notification area: save like on exit, keep running hidden.
                await _vm.Editor.FlushAsync();
                SavePlacement();
                await _host.SaveSettingsAsync();
                AppWindow.Hide();
                return;
            }

            _closing = true;
            try
            {
                await ShutdownAsync();
            }
            finally
            {
                Close();
            }
        };
    }

    private async Task OpenSettingsAsync()
    {
        if (_settingsOpen)
        {
            return; // tray "Settings…" while the dialog is open – only one ContentDialog at a time
        }

        _settingsOpen = true;
        try
        {
            await SettingsDialog.ShowAsync(_root.XamlRoot, _vm, AppWindow.Id, ApplyAppearance, ApplyIcon);
            ApplyTray();
        }
        catch (Exception ex)
        {
            // Fire-and-forget caller: without this the dialog just silently does not appear.
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

    private void RestorePlacement()
    {
        if (_host.Settings.Current.Window is { Width: > 300, Height: > 300 } w)
        {
            var area = DisplayArea.GetFromRect(new RectInt32(w.X, w.Y, w.Width, w.Height), DisplayAreaFallback.Primary);
            var work = area.WorkArea;
            var x = Math.Clamp(w.X, work.X, Math.Max(work.X, work.X + work.Width - w.Width));
            var y = Math.Clamp(w.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - w.Height));
            AppWindow.MoveAndResize(new RectInt32(x, y, Math.Min(w.Width, work.Width), Math.Min(w.Height, work.Height)));
            if (w.IsMaximized && AppWindow.Presenter is OverlappedPresenter p)
            {
                p.Maximize();
            }
        }
        else
        {
            var area = DisplayArea.Primary.WorkArea;
            var width = Math.Min(1180, area.Width);
            var height = Math.Min(900, area.Height);
            AppWindow.MoveAndResize(new RectInt32(area.X + ((area.Width - width) / 2), area.Y + ((area.Height - height) / 2), width, height));
        }
    }

    private void SavePlacement()
    {
        var maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        var previous = _host.Settings.Current.Window;
        _host.Settings.Current.Window = maximized && previous is not null
            ? new WindowPlacement { X = previous.X, Y = previous.Y, Width = previous.Width, Height = previous.Height, IsMaximized = true }
            : new WindowPlacement
            {
                X = AppWindow.Position.X,
                Y = AppWindow.Position.Y,
                Width = AppWindow.Size.Width,
                Height = AppWindow.Size.Height,
                IsMaximized = maximized,
            };
    }
}
