using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WolfMark.App;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;
using WolfMark.Desktop.Editor;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// A Markdown file from outside the notes tree, in its own window (as src/WolfMark.App/Views/ExternalNoteWindow.cs):
/// like the full view, but the folder and the file name instead of the day, Save instead of autosave, no tag. The
/// status bar shows how the file is stored; the info button copies it into today's notes.
/// </summary>
internal sealed class ExternalNoteWindow : Window
{
    private readonly MainWindow _owner;
    private readonly MainViewModel _vm;
    private readonly ExternalDocumentViewModel _doc;
    private readonly AppHost _host;
    private bool _closing;

    public ExternalNoteWindow(MainWindow owner, MainViewModel vm, ExternalDocumentViewModel doc)
    {
        _owner = owner;
        _vm = vm;
        _doc = doc;
        _host = vm.Host;
        Background = Palette.WindowTint;
        Palette.Changed += (_, _) => Background = Palette.WindowTint;
        Icon = owner.Icon;
        Width = 1000;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = owner.Position + new PixelPoint(48, 48);

        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(24, 16, 8, 0), RowSpacing = 10 };
        var heading = Heading();
        var toolbar = Toolbar();
        Grid.SetRow(toolbar, 1);
        var editorCell = new Grid();
        editorCell.Children.Add(Editor);
        editorCell.Children.Add(Badge());
        Grid.SetRow(editorCell, 2);
        main.Children.Add(heading);
        main.Children.Add(toolbar);
        main.Children.Add(editorCell);

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto") };
        var notices = Notices();
        var status = StatusBar();
        Grid.SetRow(notices, 1);
        Grid.SetRow(status, 2);
        root.Children.Add(main);
        root.Children.Add(notices);
        root.Children.Add(status);
        Content = root;

        WireEditor();
        WireShortcuts();
        Closing += async (_, e) =>
        {
            if (_closing)
            {
                return;
            }

            e.Cancel = true;
            await TryCloseAsync();
        };
        UpdateTitle();
        Ui.Bind(_doc, nameof(ExternalDocumentViewModel.IsDirty), UpdateTitle);
    }

    public MarkdownTextEditor Editor { get; } = new();

    public Task<bool> LoadAsync() => _doc.LoadAsync();

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Show();
        Activate();
    }

    /// <summary>Closes the window; with unsaved changes asks first. False when the user cancelled.</summary>
    public async Task<bool> TryCloseAsync()
    {
        if (_closing)
        {
            return true;
        }

        if (!await ConfirmDiscardAsync())
        {
            return false;
        }

        _closing = true;
        _doc.Dispose();
        Close();
        return true;
    }

    /// <summary>The folder on the first line, the file name in large letters on the second – where the day is in a note.</summary>
    private StackPanel Heading()
    {
        var folderText = Typo.Body(_doc.DirectoryName, 13, Palette.InkTertiary);
        folderText.TextTrimming = TextTrimming.CharacterEllipsis;
        folderText.VerticalAlignment = VerticalAlignment.Center;
        var folder = Ui.HStack(6, Icons.Create(Icons.Folder, 13, Palette.InkTertiary), folderText);
        ToolTip.SetTip(folder, _doc.FullPath);
        var name = new TextBlock
        {
            Text = _doc.FileName,
            FontFamily = Typo.CondensedBold,
            FontSize = 32,
            LetterSpacing = 0.6,
            Foreground = Palette.Ink,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        return Ui.VStack(2, folder, name);
    }

    /// <summary>[formatting] ……… [RAW|FORMATTED][SAVE]: the formatting of the main window, then the file's own actions.</summary>
    private Grid Toolbar()
    {
        var format = FormatBar.Create(Editor);
        var segments = FormatBar.ModeSegments(_doc, nameof(ExternalDocumentViewModel.Mode), () => _doc.Mode, m => _doc.Mode = m);

        var saveLabel = Typo.Caps(Strings.Get(R.Common_Save).ToUpper(CultureInfo.CurrentCulture), 14, Brushes.White);
        var save = Ui.FlatButton(Ui.HStack(6, Icons.Create(Icons.Save, 14, Brushes.White), saveLabel), Palette.Accent);
        save.Height = Ui.ButtonHeight;
        save.VerticalContentAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(save, Strings.Get(OperatingSystem.IsMacOS() ? R.ExtFile_SaveMac : R.ExtFile_Save));
        save.Click += async (_, _) => await SaveAsync();
        Ui.Bind(_doc, nameof(ExternalDocumentViewModel.IsDirty), () =>
        {
            save.IsEnabled = _doc.IsDirty;
            save.Opacity = _doc.IsDirty ? 1 : 0.4;
        });

        var actions = Ui.HStack(12, segments, save);
        actions.VerticalAlignment = VerticalAlignment.Center;
        var toolbar = Ui.Columns((Ui.Star, format), (Ui.Auto, actions));
        toolbar.ColumnSpacing = 12;
        return toolbar;
    }

    /// <summary>
    /// Bottom-right corner of the editor, like an imported Claude Code note's: the file is external and not saved on
    /// its own (why in the tooltip), and the way to make it a note – a copy in today's notes.
    /// </summary>
    private Border Badge()
    {
        var copy = Ui.FlatButton(Typo.Caps(Strings.Get(R.ExtFile_CopyButton), 11, Palette.Accent), Palette.Transparent);
        copy.Padding = new Thickness(6, 2);
        copy.Click += (_, _) => _ = CopyToNotesAsync();
        var label = Ui.HStack(6, Icons.Create(Icons.Document, 11, Palette.InkTertiary), Typo.Caps(Strings.Get(R.ExtFile_Badge), 11, Palette.InkTertiary));
        label.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(label, Strings.Get(R.ExtFile_CopyMessage));
        return new Border
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
    }

    /// <summary>Changed on disk (reload / keep mine), deleted, and failures.</summary>
    private StackPanel Notices()
    {
        var changed = Views.Notices.Notice(Strings.Get(R.External_Title), Strings.Get(R.External_Message), warning: true,
            (Strings.Get(R.External_Reload), () => _ = _doc.ReloadAsync()),
            (Strings.Get(R.External_Keep), () => _ = _doc.KeepMineAsync()));
        Views.Notices.Bind(changed, _doc, nameof(ExternalDocumentViewModel.ChangedOnDisk), () => _doc.ChangedOnDisk);

        var deleted = Views.Notices.Notice(Strings.Get(R.ExtFile_DeletedTitle), Strings.Get(R.ExtFile_DeletedMessage), warning: true);
        Views.Notices.Bind(deleted, _doc, nameof(ExternalDocumentViewModel.IsDeletedOnDisk), () => _doc.IsDeletedOnDisk);

        var error = Views.Notices.Notice(Strings.Get(R.Error_Title), string.Empty, warning: true);
        error.Closed = () => _doc.ErrorNotice = null;
        Views.Notices.Bind(error, _doc, nameof(ExternalDocumentViewModel.ErrorNotice), () =>
        {
            error.Message.Text = _doc.ErrorNotice ?? string.Empty;
            return _doc.ErrorNotice is not null;
        });

        var panel = Ui.VStack(4, error.Root, changed.Root, deleted.Root);
        panel.Margin = new Thickness(24, 0, 24, 6);
        return panel;
    }

    /// <summary>Created · modified · words · encoding and line ends.</summary>
    private Border StatusBar()
    {
        var text = Typo.Caps(string.Empty, 11.5, Palette.InkTertiary);
        text.VerticalAlignment = VerticalAlignment.Center;
        Ui.Bind(_doc, nameof(ExternalDocumentViewModel.StatusText), () => text.Text = _doc.StatusText.ToUpper(CultureInfo.CurrentCulture));
        var state = Ui.HStack(5, Icons.Create(Icons.Document, 11, Palette.InkTertiary), text);
        state.VerticalAlignment = VerticalAlignment.Center;
        return new Border
        {
            Child = state,
            BorderBrush = Palette.Divider,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 2, 14, 2),
            MinHeight = 28,
        };
    }

    private void WireEditor()
    {
        MainWindow.ApplyEditorTheme(Editor, _host);
        Palette.Changed += (_, _) => MainWindow.ApplyEditorTheme(Editor, _host);
        Editor.PreviewCacheDirectory = _host.Paths.PreviewCacheDirectory;
        Editor.OpenImageTip = Strings.Get(R.Editor_OpenImage);
        // Relative images and links resolve next to the file; nothing is pasted into a folder that is not ours.
        Editor.NotesRoot = _doc.DirectoryName;
        Editor.NoteDirectory = _doc.DirectoryName;
        Editor.ImagePasteHandler = null;
        Editor.Mode = _doc.Mode;
        Ui.Bind(_doc, nameof(ExternalDocumentViewModel.Mode), () => Editor.Mode = _doc.Mode);
        Editor.ModeToggleRequested += (_, _) => _doc.Mode = _doc.Mode == EditorMode.Raw ? EditorMode.Formatted : EditorMode.Raw;
        Editor.TextEdited += (_, text) => _doc.OnTextEdited(text);
        Editor.FontZoomRequested += (_, step) => _owner.ZoomEditor(step);
        _doc.LoadRequested += (_, request) =>
        {
            Editor.Load(request.Text, request.CaretToEnd, readOnly: false);
            Editor.FocusEditor();
        };
    }

    /// <summary>Ctrl+S (⌘S on macOS) saves, Ctrl+W (⌘W) closes.</summary>
    private void WireShortcuts() =>
        KeyDown += async (_, e) =>
        {
            var command = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (!command || e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                return;
            }

            if (e.Key == Key.S)
            {
                e.Handled = true;
                await SaveAsync();
            }
            else if (e.Key == Key.W)
            {
                e.Handled = true;
                await TryCloseAsync();
            }
        };

    private async Task SaveAsync()
    {
        if (_doc.IsDirty)
        {
            await _doc.SaveAsync();
        }
    }

    /// <summary>The copy opens in the main window; this window closes (asking first about unsaved changes).</summary>
    private async Task CopyToNotesAsync()
    {
        try
        {
            var path = await _doc.CopyToNotesAsync();
            _vm.IsFullView = false;
            await _vm.OpenNoteAsync(path.RelativePath);
            _owner.BringToFront();
            await TryCloseAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _doc.ErrorNotice = ex.Message;
        }
    }

    /// <summary>Save / Don't save / Cancel when the text differs from the file. True when the window may close.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_doc.IsDirty)
        {
            return true;
        }

        BringToFront();
        var answer = await new SaveQuestion(Strings.Format(R.ExtFile_CloseTitle, _doc.FileName)).ShowDialog<int>(this);
        return answer switch
        {
            1 => await _doc.SaveAsync(),
            2 => true,
            _ => false,
        };
    }

    private void UpdateTitle() => Title = (_doc.IsDirty ? "• " : string.Empty) + _doc.FileName + " – " + Strings.Get(R.AppTitle);

    /// <summary>"Save changes to x.md?" – Save (1) / Don't save (2) / Cancel (0, also Esc or closing it).</summary>
    private sealed class SaveQuestion : Window
    {
        public SaveQuestion(string question)
        {
            Title = Strings.Get(R.AppTitle);
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Palette.WindowTint;
            var text = Typo.Body(question, 15, Palette.Ink);
            text.TextWrapping = TextWrapping.Wrap;
            text.MaxWidth = 380;
            var saveLabel = Typo.Caps(Strings.Get(R.Common_Save).ToUpper(CultureInfo.CurrentCulture), 13, Brushes.White);
            var save = Ui.FlatButton(saveLabel, Palette.Accent);
            save.Click += (_, _) => Close(1);
            var discard = Ui.FlatButton(Strings.Get(R.ExtFile_DontSave));
            discard.Click += (_, _) => Close(2);
            var cancel = Ui.FlatButton(Strings.Get(R.Common_Cancel), Palette.Transparent);
            cancel.Click += (_, _) => Close(0);
            var buttons = Ui.HStack(8, save, discard, cancel);
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            Content = new Border { Padding = new Thickness(20), Child = Ui.VStack(18, text, buttons) };
            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close(0);
                }
            };
            Opened += (_, _) => save.Focus();
        }
    }
}
