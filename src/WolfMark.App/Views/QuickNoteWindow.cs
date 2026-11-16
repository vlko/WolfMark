using Microsoft.UI;
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
/// Quick note (M8-01): a small window over whatever the user is doing, with a new note of today – opened by the
/// global shortcut or <c>--quick-note</c>. It saves as you type like the main window (an empty note never becomes a
/// file); Esc or Ctrl+Enter hides it. The main window stays as it was; "continue in the main window" opens the note
/// there in full view. The window is kept hidden between uses, so the next shortcut shows it at once.
/// </summary>
internal sealed partial class QuickNoteWindow : Window
{
    private const int WidthDip = 640, HeightDip = 380;

    private readonly MainWindow _owner;
    private readonly MainViewModel _vm;
    private readonly AppHost _host;
    private readonly NoteEditorViewModel _note;
    private readonly MarkdownEditor _editor = new();
    private readonly Grid _root = new();
    private bool _closing;

    public QuickNoteWindow(MainWindow owner, MainViewModel vm, TagBrushes brushes)
    {
        _owner = owner;
        _vm = vm;
        _host = vm.Host;
        _note = new NoteEditorViewModel(_host);
        Title = Strings.Get(R.QuickNote_Title) + " – " + Strings.Get(R.AppTitle);

        ExtendsContentIntoTitleBar = true;
        var icon = AppIcons.IcoPath(_host.Settings.Current.AppIcon);
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var titleBar = BuildTitleBar();
        var main = Ui.Ui.Rows(
            (Ui.Ui.Auto, BuildToolbar(brushes)),
            (Ui.Ui.Star, _editor),
            (Ui.Ui.Auto, BuildHint()));
        main.Padding = new Thickness(20, 0, 8, 8);
        main.RowSpacing = 8;

        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Background = Palette.WindowTint;
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(main, 1);
        _root.Children.Add(titleBar);
        _root.Children.Add(main);
        Content = _root;
        SetTitleBar(titleBar);

        WireEditor();
        WireAccelerators();
        WireLifecycle();
    }

    /// <summary>
    /// Shows the window with a new note. When it is already open with text, it only comes to the front – a second
    /// press of the shortcut must not throw away what is being written.
    /// </summary>
    public async Task ShowNewAsync()
    {
        if (AppWindow.IsVisible && _note.Text.Length > 0)
        {
            BringToFront();
            return;
        }

        _note.Mode = _host.Settings.Current.DefaultEditorMode;
        await _note.NewAsync(null);
        if (!AppWindow.IsVisible)
        {
            Place();
            AppWindow.Show();
        }

        BringToFront();
        _editor.FocusEditor();
    }

    /// <summary>Saves and closes for good – the app is ending.</summary>
    public async Task CloseForExitAsync()
    {
        if (_closing)
        {
            return;
        }

        await _note.FlushAsync();
        _closing = true;
        Close();
    }

    /// <summary>Same theme as the main window (also after a change in the settings).</summary>
    public void ApplyTheme(ElementTheme theme)
    {
        _root.RequestedTheme = theme;
        ApplyEditorTheme();
        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = Palette.Ink.Color;
        tb.ButtonHoverBackgroundColor = Palette.SubtleFill.Color;
        tb.ButtonHoverForegroundColor = Palette.Ink.Color;
        tb.ButtonInactiveForegroundColor = Palette.InkTertiary.Color;
    }

    public void ApplyEditorTheme() => EditorSetup.ApplyTheme(_editor, _host);

    private void BringToFront()
    {
        Activate();
        Windows.Win32.PInvoke.SetForegroundWindow(new Windows.Win32.Foundation.HWND(Win32Interop.GetWindowFromWindowId(AppWindow.Id)));
    }

    private Grid BuildTitleBar()
    {
        var title = Typo.Caps(Strings.Get(R.QuickNote_Title), 13, Palette.InkSecondary);
        var grid = Ui.Ui.Columns(
            (Ui.Ui.Px(20), null),
            (Ui.Ui.Auto, Ui.Ui.HStack(8, Typo.Icon(Glyphs.Edit, 12, Palette.Accent), title)),
            (Ui.Ui.Star, null),
            (Ui.Ui.Px(140), null)); // caption buttons (minimize and maximize disabled)
        grid.Background = Palette.Transparent;
        return grid;
    }

    /// <summary>[tag ▾] ……… [continue in the main window ⤢].</summary>
    private Grid BuildToolbar(TagBrushes brushes)
    {
        var tag = TagPicker.CreateButton(_note, _host, brushes);
        var open = Ui.Ui.IconButton(Glyphs.Expand, Strings.Get(R.QuickNote_OpenInMain), () => _ = OpenInMainAsync(), 13);
        open.VerticalAlignment = VerticalAlignment.Center;
        return Ui.Ui.Columns((Ui.Ui.Auto, tag), (Ui.Ui.Star, null), (Ui.Ui.Auto, open));
    }

    private TextBlock BuildHint()
    {
        var hint = Typo.Body(Strings.Get(R.QuickNote_Hint), 12, Palette.InkTertiary);
        hint.Margin = new Thickness(0, 0, 12, 0);
        return hint;
    }

    private void WireEditor()
    {
        EditorSetup.ConfigureTexts(_editor);
        _editor.AlignLeft = true;
        _editor.PreviewCacheDirectory = _host.Paths.PreviewCacheDirectory;
        _editor.IsSpellCheckEnabled = _host.Settings.Current.SpellCheckEnabled;
        _editor.Mode = _note.Mode == EditorMode.Raw ? MarkdownEditorMode.Raw : MarkdownEditorMode.Formatted;
        _editor.TextEdited += (_, text) => _note.OnTextEdited(text);
        _editor.EditorLostFocus += (_, _) => _ = _note.FlushAsync();
        _editor.ModeChanged += (_, mode) => _note.Mode = mode == MarkdownEditorMode.Raw ? EditorMode.Raw : EditorMode.Formatted;
        _editor.FontZoomRequested += (_, zoom) => _owner.ZoomEditor(zoom.Reset ? null : zoom.Steps);
        _editor.ImagePasteHandler = async (data, extension) =>
        {
            var day = _note.Day;
            return await Task.Run(() => _host.Engine.SaveAssetAsync(day, DateTime.Now, extension, data, CancellationToken.None));
        };
        _note.LoadRequested += (_, request) =>
        {
            _editor.NotesRoot = _host.Engine.Root;
            _editor.NoteDirectory = _host.Engine.GetDayDirectory(_note.Day);
            _editor.Load(request.Text, request.CaretToEnd, readOnly: false);
            _editor.FocusEditor();
        };
        _note.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NoteEditorViewModel.Mode))
            {
                _editor.Mode = _note.Mode == EditorMode.Raw ? MarkdownEditorMode.Raw : MarkdownEditorMode.Formatted;
            }
        };
    }

    private void WireAccelerators()
    {
        Accelerator(VirtualKey.Escape, VirtualKeyModifiers.None, () => _ = HideAsync());
        Accelerator(VirtualKey.Enter, VirtualKeyModifiers.Control, () => _ = HideAsync());
        Accelerator(VirtualKey.E, VirtualKeyModifiers.Control, () =>
            _note.Mode = _note.Mode == EditorMode.Raw ? EditorMode.Formatted : EditorMode.Raw);

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
                _ = _note.FlushAsync();
            }
            else if (SystemBackdrop is null)
            {
                SystemBackdrop = new MicaBackdrop();
            }
        };

        // The close button hides like Esc; the window is closed for good only with the app.
        AppWindow.Closing += async (_, e) =>
        {
            if (_closing)
            {
                return;
            }

            e.Cancel = true;
            await HideAsync();
        };
    }

    private async Task HideAsync()
    {
        await _note.FlushAsync();
        AppWindow.Hide();
    }

    /// <summary>The note goes to the main window in full view (a note not written yet: a new one there).</summary>
    private async Task OpenInMainAsync()
    {
        await HideAsync();
        if (_note.IsPersisted && _note.Path is { } path)
        {
            await _vm.OpenNoteAsync(path.RelativePath);
        }
        else
        {
            await _vm.NewNoteAsync();
        }

        _vm.IsFullView = true;
        _owner.BringToFront();
    }

    /// <summary>In the middle of the screen with the mouse pointer, in the upper third – where the eyes are.</summary>
    private void Place()
    {
        Windows.Win32.PInvoke.GetCursorPos(out var cursor);
        var display = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        var area = display.WorkArea;
        // The scale of the monitor it opens on (the window may still be on another one).
        var monitor = Windows.Win32.PInvoke.MonitorFromPoint(cursor, Windows.Win32.Graphics.Gdi.MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var scale = Windows.Win32.PInvoke.GetDpiForMonitor(monitor, Windows.Win32.UI.HiDpi.MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpi, out _).Succeeded && dpi > 0
            ? dpi / 96.0
            : 1;

        var width = Math.Min((int)(WidthDip * scale), area.Width);
        var height = Math.Min((int)(HeightDip * scale), area.Height);
        var x = area.X + ((area.Width - width) / 2);
        var y = area.Y + Math.Max(0, (area.Height - height) / 3);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
