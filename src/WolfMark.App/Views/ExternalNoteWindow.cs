using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
/// A Markdown file from outside the notes tree, in its own window: like the full view, but the folder and the file
/// name instead of the day, Save instead of autosave, no tag. The status bar shows how the file is stored; the info
/// button copies it into today's notes.
/// </summary>
internal sealed partial class ExternalNoteWindow : Window
{
    private readonly MainWindow _owner;
    private readonly MainViewModel _vm;
    private readonly ExternalDocumentViewModel _doc;
    private readonly AppHost _host;
    private readonly MarkdownEditor _editor = new();
    private readonly Grid _root = new();
    private readonly Grid _titleBar;
    private bool _closing;

    public ExternalNoteWindow(MainWindow owner, MainViewModel vm, ExternalDocumentViewModel doc)
    {
        _owner = owner;
        _vm = vm;
        _doc = doc;
        _host = vm.Host;

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        var icon = AppIcons.IcoPath(_host.Settings.Current.AppIcon);
        if (File.Exists(icon))
        {
            AppWindow.SetIcon(icon);
        }

        _titleBar = BuildTitleBar();
        var editorCell = new Grid();
        editorCell.Children.Add(_editor);
        editorCell.Children.Add(BuildBadge());
        var main = Ui.Ui.Rows(
            (Ui.Ui.Auto, BuildHeading()),
            (Ui.Ui.Auto, BuildToolbar()),
            (Ui.Ui.Star, editorCell));
        main.Padding = new Thickness(24, 4, 8, 0);
        main.RowSpacing = 10;

        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // notices
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // status bar
        _root.Background = Palette.WindowTint;
        Add(_titleBar, 0);
        Add(main, 1);
        var notices = BuildNotices();
        notices.Margin = new Thickness(24, 0, 24, 6);
        Add(notices, 2);
        Add(BuildStatusBar(), 3);
        Content = _root;
        SetTitleBar(_titleBar);

        WireEditor();
        WireAccelerators();
        WireLifecycle();
        UpdateTitle();
        _doc.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExternalDocumentViewModel.IsDirty))
            {
                UpdateTitle();
            }
        };
        Place();
    }

    public string FullPath => _doc.FullPath;

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

    public async Task<bool> LoadAsync() => await _doc.LoadAsync();

    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        Windows.Win32.PInvoke.SetForegroundWindow(new Windows.Win32.Foundation.HWND(Win32Interop.GetWindowFromWindowId(AppWindow.Id)));
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

    private void Add(FrameworkElement child, int row)
    {
        Grid.SetRow(child, row);
        _root.Children.Add(child);
    }

    private Grid BuildTitleBar()
    {
        var appName = Typo.Caps(Strings.Get(R.AppTitle), 16, Palette.Ink);
        appName.FontFamily = Typo.CondensedBold;
        var grid = Ui.Ui.Columns(
            (Ui.Ui.Px(20), null),
            (Ui.Ui.Auto, Ui.Ui.HStack(10, Typo.Icon(Glyphs.Edit, 14, Palette.Accent), appName)),
            (Ui.Ui.Star, null),
            (Ui.Ui.Px(150), null)); // caption buttons
        grid.ColumnSpacing = 16;
        grid.Background = Palette.Transparent;
        return grid;
    }

    /// <summary>The folder on the first line, the file name in large letters on the second – where the day is in a note.</summary>
    private StackPanel BuildHeading()
    {
        var folder = Ui.Ui.HStack(6, Typo.Icon(Glyphs.Folder, 12, Palette.InkTertiary), Typo.Body(_doc.DirectoryName, 13, Palette.InkTertiary));
        ((TextBlock)folder.Children[1]).TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(folder, _doc.FullPath);
        var name = new TextBlock
        {
            Text = _doc.FileName,
            FontFamily = Typo.CondensedBold,
            FontSize = 32,
            CharacterSpacing = 20,
            Foreground = Palette.Ink,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        return Ui.Ui.VStack(2, folder, name);
    }

    /// <summary>[formatting] ……… [RAW|FORMÁT][Save]: the formatting of the main window, then the file's own actions.</summary>
    private Grid BuildToolbar()
    {
        var format = NoteHeaderView.BuildFormatToggles(_editor);

        var raw = NoteHeaderView.SegmentButton(Strings.Get(R.Mode_Raw));
        var formatted = NoteHeaderView.SegmentButton(Strings.Get(R.Mode_Formatted));
        raw.Click += (_, _) => _doc.Mode = EditorMode.Raw;
        formatted.Click += (_, _) => _doc.Mode = EditorMode.Formatted;
        var segments = Ui.Ui.HStack(2, raw, formatted);
        ToolTipService.SetToolTip(segments, Strings.Get(R.Mode_Tooltip));
        segments.Bind(_doc, nameof(ExternalDocumentViewModel.Mode), () =>
        {
            raw.IsChecked = _doc.Mode == EditorMode.Raw;
            formatted.IsChecked = _doc.Mode == EditorMode.Formatted;
            _editor.Mode = _doc.Mode == EditorMode.Raw ? MarkdownEditorMode.Raw : MarkdownEditorMode.Formatted;
        });

        var save = Ui.Ui.TagButton(Strings.Get(R.Common_Save), () => _ = SaveAsync(), Palette.Accent, new SolidColorBrush(Colors.White), Glyphs.Save);
        ToolTipService.SetToolTip(save, Strings.Get(R.ExtFile_Save));
        save.Bind(_doc, nameof(ExternalDocumentViewModel.IsDirty), () =>
        {
            save.IsEnabled = _doc.IsDirty;
            save.Opacity = _doc.IsDirty ? 1 : 0.4;
        });

        var actions = Ui.Ui.HStack(12, segments, save);
        actions.VerticalAlignment = VerticalAlignment.Center;
        var toolbar = Ui.Ui.Columns((Ui.Ui.Star, format), (Ui.Ui.Auto, actions));
        toolbar.ColumnSpacing = 12;
        return toolbar;
    }

    /// <summary>
    /// Bottom-right corner of the editor, like an imported Claude Code note's: the file is external and not saved on
    /// its own (why in the tooltip), and the way to make it a note – a copy in today's notes.
    /// </summary>
    private Border BuildBadge()
    {
        var copy = new Button
        {
            Content = Typo.Caps(Strings.Get(R.ExtFile_CopyButton), 11, Palette.Accent),
            Padding = new Thickness(6, 2, 6, 2),
            CornerRadius = Radius.Control,
        };
        copy.StyleButton(Palette.Transparent, Palette.Accent);
        copy.Click += (_, _) => _ = CopyToNotesAsync();
        var label = Ui.Ui.HStack(6, Typo.Icon(Glyphs.Document, 11, Palette.InkTertiary), Typo.Caps(Strings.Get(R.ExtFile_Badge), 11, Palette.InkTertiary));
        label.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(label, Strings.Get(R.ExtFile_CopyMessage));
        return new Border
        {
            Child = Ui.Ui.HStack(8, label, copy),
            Background = Palette.Card,
            BorderBrush = Palette.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = Radius.Tag,
            Padding = new Thickness(10, 2, 2, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 8, 13),
        };
    }

    /// <summary>Changed on disk (reload / keep mine), deleted, and failures.</summary>
    private StackPanel BuildNotices()
    {
        var changed = Notice(Strings.Get(R.External_Title), Strings.Get(R.External_Message), InfoBarSeverity.Warning);
        var reload = Ui.Ui.TextButton(Strings.Get(R.External_Reload), () => _ = _doc.ReloadAsync());
        var keep = Ui.Ui.TextButton(Strings.Get(R.External_Keep), () => _ = _doc.KeepMineAsync());
        changed.ActionButton = reload;
        changed.Content = keep;
        changed.Bind(_doc, nameof(ExternalDocumentViewModel.ChangedOnDisk), () => changed.IsOpen = _doc.ChangedOnDisk);

        var deleted = Notice(Strings.Get(R.ExtFile_DeletedTitle), Strings.Get(R.ExtFile_DeletedMessage), InfoBarSeverity.Warning);
        deleted.Bind(_doc, nameof(ExternalDocumentViewModel.IsDeletedOnDisk), () => deleted.IsOpen = _doc.IsDeletedOnDisk);

        var error = Notice(Strings.Get(R.Error_Title), string.Empty, InfoBarSeverity.Error);
        error.Bind(_doc, nameof(ExternalDocumentViewModel.ErrorNotice), () =>
        {
            error.IsOpen = _doc.ErrorNotice is not null;
            error.Message = _doc.ErrorNotice ?? string.Empty;
        });
        error.Closed += (_, _) => _doc.ErrorNotice = null;

        var bars = new[] { error, changed, deleted };
        var panel = Ui.Ui.VStack(0, bars);
        void Update()
        {
            var any = false;
            foreach (var bar in bars)
            {
                bar.Margin = bar.IsOpen ? new Thickness(0, 4, 0, 0) : new Thickness(0);
                any |= bar.IsOpen;
            }

            panel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var bar in bars)
        {
            bar.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (_, _) => Update());
        }

        Update();
        return panel;

        static InfoBar Notice(string title, string message, InfoBarSeverity severity) => new()
        {
            Title = title,
            Message = message,
            Severity = severity,
            IsClosable = true,
            IsOpen = false,
        };
    }

    /// <summary>Created · modified · words · encoding and line ends; the full path on the right.</summary>
    private Border BuildStatusBar()
    {
        var text = Typo.Caps(string.Empty, 11.5, Palette.InkTertiary);
        text.Bind(_doc, nameof(ExternalDocumentViewModel.StatusText), () => text.Text = _doc.StatusText.ToUpper(CultureInfo.CurrentCulture));
        var state = Ui.Ui.HStack(5, Typo.Icon(Glyphs.Document, 11, Palette.InkTertiary), text);
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
        EditorSetup.ConfigureTexts(_editor);
        _editor.AlignLeft = true;
        _editor.PreviewCacheDirectory = _host.Paths.PreviewCacheDirectory;
        // Relative images and links resolve next to the file; nothing is pasted into a folder that is not ours.
        _editor.NotesRoot = _doc.DirectoryName;
        _editor.NoteDirectory = _doc.DirectoryName;
        _editor.ImagePasteHandler = null;
        _editor.IsSpellCheckEnabled = _host.Settings.Current.SpellCheckEnabled;
        _editor.Mode = _doc.Mode == EditorMode.Raw ? MarkdownEditorMode.Raw : MarkdownEditorMode.Formatted;
        _editor.TextEdited += (_, text) => _doc.OnTextEdited(text);
        _editor.ModeChanged += (_, mode) => _doc.Mode = mode == MarkdownEditorMode.Raw ? EditorMode.Raw : EditorMode.Formatted;
        _editor.FontZoomRequested += (_, zoom) => _owner.ZoomEditor(zoom.Reset ? null : zoom.Steps);
        _doc.LoadRequested += (_, request) =>
        {
            _editor.Load(request.Text, request.CaretToEnd, readOnly: false);
            _editor.FocusEditor();
        };
    }

    private void WireAccelerators()
    {
        Accelerator(VirtualKey.S, VirtualKeyModifiers.Control, () => _ = SaveAsync());
        Accelerator(VirtualKey.E, VirtualKeyModifiers.Control, () =>
            _doc.Mode = _doc.Mode == EditorMode.Raw ? EditorMode.Formatted : EditorMode.Raw);
        Accelerator(VirtualKey.W, VirtualKeyModifiers.Control, () => _ = TryCloseAsync());

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
            if (e.WindowActivationState != WindowActivationState.Deactivated && SystemBackdrop is null)
            {
                SystemBackdrop = new MicaBackdrop();
            }
        };

        AppWindow.Closing += async (_, e) =>
        {
            if (_closing)
            {
                return;
            }

            e.Cancel = true;
            await TryCloseAsync();
        };
    }

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
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = Strings.Format(R.ExtFile_CloseTitle, _doc.FileName),
            PrimaryButtonText = Strings.Get(R.Common_Save),
            SecondaryButtonText = Strings.Get(R.ExtFile_DontSave),
            CloseButtonText = Strings.Get(R.Common_Cancel),
            DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = _root.ActualTheme,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => await _doc.SaveAsync(),
            ContentDialogResult.Secondary => true,
            _ => false,
        };
    }

    private void UpdateTitle() => Title = (_doc.IsDirty ? "• " : string.Empty) + _doc.FileName + " – " + Strings.Get(R.AppTitle);

    /// <summary>Over the main window, a little offset, at most 1000 × 900.</summary>
    private void Place()
    {
        var area = DisplayArea.GetFromWindowId(_owner.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min(1000, area.Width);
        var height = Math.Min(900, area.Height);
        var x = Math.Clamp(_owner.AppWindow.Position.X + 48, area.X, Math.Max(area.X, area.X + area.Width - width));
        var y = Math.Clamp(_owner.AppWindow.Position.Y + 48, area.Y, Math.Max(area.Y, area.Y + area.Height - height));
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
