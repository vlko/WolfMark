using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Zapisnik.App.Infrastructure;
using Zapisnik.App.Theming;
using Zapisnik.App.Ui;
using Zapisnik.App.ViewModels;
using Zapisnik.Core.Settings;
using Zapisnik.Editor;

namespace Zapisnik.App.Views;

/// <summary>
/// Main window (SPEC §4.5): custom title bar (date · search · settings), week strip, the note card
/// (header, editor, splitter, history) and the calendar / search overlays.
/// </summary>
internal sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly TagBrushes _brushes;
    private readonly AppHost _host;
    private readonly Grid _root = new();
    private readonly Grid _titleBar;
    private readonly List<FrameworkElement> _titleBarInteractive = [];
    private readonly MarkdownEditor _editor = new();
    private readonly CalendarView _calendar;
    private readonly SearchPalette _search;
    private readonly RowDefinition _historyRow;
    private bool _closing;

    public MainWindow(MainViewModel vm, TagBrushes brushes)
    {
        _vm = vm;
        _brushes = brushes;
        _host = vm.Host;
        Title = Strings.Get(R.AppTitle);

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Zapisnik.ico");
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        _titleBar = BuildTitleBar();
        _calendar = new CalendarView(vm, brushes) { Margin = new Thickness(12, 0, 12, 12), Visibility = Visibility.Collapsed };
        _search = new SearchPalette(vm, brushes) { Visibility = Visibility.Collapsed };

        // Note card
        _historyRow = new RowDefinition { Height = new GridLength(_host.Settings.Current.HistoryPaneHeight) };
        var header = new NoteHeaderView(vm, brushes, _editor.Execute);
        var splitter = new SplitterHandle(
            () => _historyRow.Height.Value,
            h => _historyRow.Height = new GridLength(h),
            () =>
            {
                _host.Settings.Current.HistoryPaneHeight = _historyRow.Height.Value;
                _ = _host.SaveSettingsAsync();
            });
        var history = new HistoryView(vm, brushes);

        var cardGrid = new Grid();
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardGrid.RowDefinitions.Add(_historyRow);
        Add(cardGrid, header, 0);
        Add(cardGrid, _editor, 1);
        Add(cardGrid, splitter, 2);
        Add(cardGrid, history, 3);

        var card = Ui.Ui.Card(cardGrid, radius: 20);
        card.Margin = new Thickness(12, 0, 12, 12);
        card.Translation = new System.Numerics.Vector3(0, 0, 16);
        card.Shadow = new ThemeShadow();

        var content = new Grid();
        content.Children.Add(card);
        content.Children.Add(_calendar);

        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Background = Palette.WindowTint;
        Add(_root, _titleBar, 0);
        Add(_root, new WeekStripView(vm, brushes), 1);
        Add(_root, content, 2);
        Grid.SetRowSpan(_search, 3);
        _root.Children.Add(_search);
        Content = _root;
        SetTitleBar(_titleBar);

        WireEditor();
        WireOverlays();
        WireAccelerators();
        WireLifecycle();
        ApplyAppearance();
        RestorePlacement();
    }

    /// <summary>Theme, backdrop and editor settings (also re-applied after the settings dialog).</summary>
    public void ApplyAppearance()
    {
        var s = _host.Settings.Current;
        _root.RequestedTheme = s.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        SystemBackdrop = s.Backdrop switch
        {
            BackdropKind.Mica => new MicaBackdrop(),
            BackdropKind.Acrylic => new DesktopAcrylicBackdrop(),
            _ => null,
        };
        OnThemeChanged();
    }

    public void BringToFront()
    {
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
        var appName = Typo.Caps(Strings.Get(R.AppTitle), 11.5, Palette.InkSecondary);
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
            Background = Palette.Card,
            BorderBrush = Palette.CardStroke,
        };
        ((Grid)searchBox.Content).ColumnSpacing = 10;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(searchBox, Strings.Get(R.Toolbar_Search));
        searchBox.Click += (_, _) => _vm.OpenSearch();

        var settings = Ui.Ui.IconButton(Glyphs.Settings, Strings.Get(R.Toolbar_Settings), () => _ = OpenSettingsAsync());
        var status = Typo.Body(string.Empty, 11.5, Palette.InkTertiary);
        status.Bind(_vm, nameof(MainViewModel.StatusText), () => status.Text = _vm.StatusText);

        _titleBarInteractive.Add(searchBox);
        _titleBarInteractive.Add(settings);

        var grid = Ui.Ui.Columns(
            (Ui.Ui.Px(20), null),
            (Ui.Ui.Auto, Ui.Ui.HStack(10, Typo.Icon(Glyphs.Edit, 14, Palette.Accent), appName, status)),
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

    private void OnThemeChanged()
    {
        var theme = _root.ActualTheme;
        Palette.Apply(theme);
        var s = _host.Settings.Current;
        Func<string, Windows.UI.Color> tagColor = tag => Palette.ToColor(_host.Tags.GetColor(tag));
        var baseTheme = theme == ElementTheme.Dark ? MarkdownTheme.Dark(tagColor) : MarkdownTheme.Light(tagColor);
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
            FontFamily = s.EditorFontFamily,
            FontSize = s.EditorFontSize,
        };
        _editor.MaxLineWidth = s.EditorMaxLineWidth;

        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = Palette.Ink.Color;
        tb.ButtonHoverBackgroundColor = Palette.SubtleFill.Color;
        tb.ButtonHoverForegroundColor = Palette.Ink.Color;
        tb.ButtonInactiveForegroundColor = Palette.InkTertiary.Color;
    }

    // ---------------------------------------------------------------- wiring

    private void WireEditor()
    {
        var editorVm = _vm.Editor;
        _editor.PlaceholderText = Strings.Get(R.Editor_Placeholder);
        _editor.Mode = editorVm.Mode == EditorMode.Raw ? MarkdownEditorMode.Raw : MarkdownEditorMode.Formatted;
        _editor.TextEdited += (_, text) => editorVm.OnTextEdited(text);
        _editor.EditorLostFocus += (_, _) => _ = editorVm.FlushAsync();
        _editor.ModeChanged += (_, mode) => editorVm.Mode = mode == MarkdownEditorMode.Raw ? EditorMode.Raw : EditorMode.Formatted;
        _editor.ImagePasteHandler = async png =>
        {
            var day = editorVm.Day;
            return await Task.Run(() => _host.Engine.SaveAssetAsync(day, DateTime.Now, ".png", png, CancellationToken.None));
        };

        editorVm.LoadRequested += (_, request) =>
        {
            _editor.IsReadOnly = false;
            _editor.Load(request.Text, request.CaretToEnd);
            _editor.IsReadOnly = editorVm.IsReadOnly;
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
                case nameof(NoteEditorViewModel.IsReadOnly):
                    _editor.IsReadOnly = editorVm.IsReadOnly;
                    break;
            }
        };

        _brushes.Changed += (_, _) => OnThemeChanged();
        _host.Tags.Changed += (_, _) => UiThread.Post(_brushes.Refresh);
        _root.ActualThemeChanged += (_, _) => OnThemeChanged();
    }

    private void WireOverlays()
    {
        _vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsCalendarOpen):
                    _calendar.Visibility = _vm.IsCalendarOpen ? Visibility.Visible : Visibility.Collapsed;
                    if (!_vm.IsCalendarOpen)
                    {
                        _editor.FocusEditor();
                    }

                    break;
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
        _vm.CalendarLoaded += (_, _) =>
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _calendar.FocusDay(_vm.SelectedDay));

        Accelerator(VirtualKey.N, VirtualKeyModifiers.Control, () => _ = _vm.NewNoteAsync());
        Accelerator(VirtualKey.K, VirtualKeyModifiers.Control, _vm.OpenSearch);
        Accelerator(VirtualKey.F, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, _vm.OpenSearch);
        Accelerator(VirtualKey.C, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () =>
        {
            if (_vm.IsCalendarOpen)
            {
                _vm.IsCalendarOpen = false;
            }
            else
            {
                _ = _vm.OpenCalendarAsync();
            }
        });
        Accelerator(VirtualKey.E, VirtualKeyModifiers.Control, () =>
            _vm.Editor.Mode = _vm.Editor.Mode == EditorMode.Raw ? EditorMode.Formatted : EditorMode.Raw);
        Accelerator((VirtualKey)188, VirtualKeyModifiers.Control, () => _ = OpenSettingsAsync()); // Ctrl+,
        Accelerator(VirtualKey.Escape, VirtualKeyModifiers.None, () =>
        {
            _vm.IsSearchOpen = false;
            _vm.IsCalendarOpen = false;
        });
        Accelerator(VirtualKey.Left, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, () => _ = _vm.MoveWeekAsync(-1));
        Accelerator(VirtualKey.Right, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, () => _ = _vm.MoveWeekAsync(1));

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
            _closing = true;
            try
            {
                await _vm.Editor.FlushAsync();
                SavePlacement();
                await _host.SaveSettingsAsync();
                await _host.DisposeAsync();
            }
            finally
            {
                Close();
            }
        };
    }

    private async Task OpenSettingsAsync()
    {
        await SettingsDialog.ShowAsync(_root.XamlRoot, _vm, AppWindow.Id, ApplyAppearance);
    }

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
