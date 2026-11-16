using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using WolfMark.App;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;
using WolfMark.Desktop.Editor;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// Quick note (M8-01, as src/WolfMark.App/Views/QuickNoteWindow.cs): a small window with a new note of today, opened
/// by <c>--quick-note</c> – which the user binds to a shortcut of the system (ADR 0012). It saves as you type like
/// the main window; Esc or Ctrl+Enter (⌘↩) hides it. "Continue in the main window" opens the note there in full
/// view. Kept hidden between uses, so the next shortcut shows it at once.
/// </summary>
internal sealed class QuickNoteWindow : Window
{
    private readonly MainWindow _owner;
    private readonly MainViewModel _vm;
    private readonly AppHost _host;
    private readonly NoteEditorViewModel _note;
    private readonly MarkdownTextEditor _editor = new();
    private bool _closing;

    public QuickNoteWindow(MainWindow owner, MainViewModel vm)
    {
        _owner = owner;
        _vm = vm;
        _host = vm.Host;
        _note = new NoteEditorViewModel(_host);
        Title = Strings.Get(R.QuickNote_Title) + " – " + Strings.Get(R.AppTitle);
        Icon = owner.Icon;
        Width = 640;
        Height = 380;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.WindowTint;
        Palette.Changed += (_, _) => Background = Palette.WindowTint;

        var tag = TagPicker.CreateButton(_note, _host);
        var open = Ui.IconButton(Icons.Create(Icons.Expand, 14, Palette.InkTertiary), Strings.Get(R.QuickNote_OpenInMain), () => _ = OpenInMainAsync());
        open.VerticalAlignment = VerticalAlignment.Center;
        var toolbar = Ui.Columns((Ui.Auto, tag), (Ui.Star, null), (Ui.Auto, open));
        var hint = Typo.Body(Strings.Get(OperatingSystem.IsMacOS() ? R.QuickNote_HintMac : R.QuickNote_Hint), 12, Palette.InkTertiary);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20, 12, 8, 8), RowSpacing = 8 };
        Grid.SetRow(_editor, 1);
        Grid.SetRow(hint, 2);
        root.Children.Add(toolbar);
        root.Children.Add(_editor);
        root.Children.Add(hint);
        Content = root;

        WireEditor();
        WireShortcuts();
        Deactivated += (_, _) => _ = _note.FlushAsync();

        // Closing hides like Esc; the window is closed for good only with the app.
        Closing += async (_, e) =>
        {
            if (_closing)
            {
                return;
            }

            e.Cancel = true;
            await HideAsync();
        };
    }

    /// <summary>
    /// Shows the window with a new note. When it is already open with text, it only comes to the front – a second
    /// use of the shortcut must not throw away what is being written.
    /// </summary>
    public async Task ShowNewAsync()
    {
        if (IsVisible && _note.Text.Length > 0)
        {
            Activate();
            return;
        }

        _note.Mode = _host.Settings.Current.DefaultEditorMode;
        await _note.NewAsync(null);
        if (!IsVisible)
        {
            Place();
            Show();
        }

        Activate();
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

    public void ApplyEditorTheme() => MainWindow.ApplyEditorTheme(_editor, _host);

    private void WireEditor()
    {
        ApplyEditorTheme();
        Palette.Changed += (_, _) => ApplyEditorTheme();
        _editor.PreviewCacheDirectory = _host.Paths.PreviewCacheDirectory;
        _editor.OpenImageTip = Strings.Get(R.Editor_OpenImage);
        _editor.Mode = _note.Mode;
        Ui.Bind(_note, nameof(NoteEditorViewModel.Mode), () => _editor.Mode = _note.Mode);
        _editor.ModeToggleRequested += (_, _) => _note.Mode = _note.Mode == EditorMode.Raw ? EditorMode.Formatted : EditorMode.Raw;
        _editor.TextEdited += (_, text) => _note.OnTextEdited(text);
        _editor.EditorLostFocus += (_, _) => _ = _note.FlushAsync();
        _editor.FontZoomRequested += (_, step) => _owner.ZoomEditor(step);
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
    }

    /// <summary>Esc, and Ctrl+Enter (⌘↩ on macOS): done – the window hides, the note is saved.</summary>
    private void WireShortcuts() =>
        AddHandler(
            KeyDownEvent,
            async (_, e) =>
            {
                var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
                if ((e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) || (e.Key == Key.Enter && command))
                {
                    e.Handled = true;
                    await HideAsync();
                }
            },
            Avalonia.Interactivity.RoutingStrategies.Tunnel);

    private async Task HideAsync()
    {
        await _note.FlushAsync();
        Hide();
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

    /// <summary>In the middle of the main window's screen, in the upper third – where the eyes are.</summary>
    private void Place()
    {
        var screens = _owner.Screens;
        var screen = screens.ScreenFromWindow(_owner) ?? screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var width = (int)Math.Min(Width * scale, area.Width);
        var height = (int)Math.Min(Height * scale, area.Height);
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(area.X + ((area.Width - width) / 2), area.Y + Math.Max(0, (area.Height - height) / 3));
    }
}
