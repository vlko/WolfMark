using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;
using WolfMark.Desktop.Infrastructure;
using WolfMark.Desktop.Theming;
using WolfMark.Storage;
using WolfMark.Storage.Trash;

namespace WolfMark.Desktop.Views;

/// <summary>
/// Settings that apply off Windows (the twin of src/WolfMark.App/Views/SettingsDialog.cs, without the Windows-only
/// parts: start with Windows, window rendering, app icon; the .md association is done the macOS/Linux way).
/// Changes that need a restart offer one.
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly Action _applyAppearance;
    private bool _restartNeeded;

    public SettingsWindow(MainViewModel vm, Action applyAppearance)
    {
        _vm = vm;
        _applyAppearance = applyAppearance;
        var host = vm.Host;
        var s = host.Settings.Current;
        Title = Strings.Get(R.Settings_Title);
        Width = 820;
        Height = 640;
        Background = Palette.WindowTint;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Notes folder: switch to another one, or move the notes there.
        var rootText = Typo.Body(host.Root, 13, Palette.InkSecondary);
        var restart = Ui.FlatButton(Strings.Get(R.Settings_Restart));
        var restartText = Typo.Body(Strings.Get(R.Settings_RestartRequired), 12.5, Palette.InkSecondary);
        var restartRow = Ui.HStack(10, restartText, restart);
        restartRow.IsVisible = false;
        restart.Click += async (_, _) =>
        {
            await SaveAsync();
            Program.RestartRequested = true;
            Close();
            (Owner as Window)?.Close();
        };
        string? picked = null;
        var moveText = Typo.Body(string.Empty, 12.5, Palette.InkSecondary);
        moveText.TextWrapping = TextWrapping.Wrap;
        var move = Ui.FlatButton(Strings.Get(R.Settings_Move));
        var switchTo = Ui.FlatButton(Strings.Get(R.Settings_Switch));
        var cancelMove = Ui.FlatButton(Strings.Get(R.Common_Cancel), Palette.Transparent);
        var movePanel = Ui.VStack(6, moveText, Ui.HStack(6, move, switchTo, cancelMove));
        movePanel.IsVisible = false;
        var browse = Ui.FlatButton(Strings.Get(R.Settings_Browse));
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path && !string.Equals(path, host.Root, StringComparison.Ordinal))
            {
                picked = path;
                moveText.Text = Strings.Get(R.Settings_MoveMessage) + Environment.NewLine + path;
                movePanel.IsVisible = true;
            }
        };
        async Task ApplyRootAsync(bool moveNotes)
        {
            if (picked is not { } newRoot)
            {
                return;
            }

            movePanel.IsVisible = false;
            if (moveNotes)
            {
                browse.IsEnabled = false;
                await vm.Editor.FlushAsync();
                var bin = new FolderRecycleBin(host.Root);
                await Task.Run(() => NotesMover.MoveAsync(host.Root, newRoot, bin, null, CancellationToken.None));
                browse.IsEnabled = true;
            }

            s.RootDirectory = newRoot;
            await host.SaveSettingsAsync();
            rootText.Text = newRoot;
            _restartNeeded = true;
            restartRow.IsVisible = true;
        }

        move.Click += async (_, _) => await ApplyRootAsync(moveNotes: true);
        switchTo.Click += async (_, _) => await ApplyRootAsync(moveNotes: false);
        cancelMove.Click += (_, _) => movePanel.IsVisible = false;

        var theme = Combo([Strings.Get(R.Settings_ThemeSystem), Strings.Get(R.Settings_ThemeLight), Strings.Get(R.Settings_ThemeDark)], (int)s.Theme);
        var languages = new[] { (string?)null, "sk-SK", "en-US" };
        var language = Combo([Strings.Get(R.Settings_LanguageSystem), "Slovenčina", "English"], Math.Max(0, Array.IndexOf(languages, s.Language)));
        var closeToTray = new ToggleSwitch { IsChecked = s.CloseToTray, Content = Strings.Get(R.Settings_CloseToTray) };

        // .md files: the system's default handler is the truth – read off the UI thread (xdg-mime runs a process).
        bool? associationWas = null;
        var fileAssociation = new ToggleSwitch { Content = Strings.Get(R.Settings_FileAssociation), IsEnabled = false };
        var associationHint = Typo.Body(Strings.Get(R.Settings_FileAssociationHintDesktop), 12.5, Palette.InkTertiary);
        associationHint.TextWrapping = TextWrapping.Wrap;
        if (DefaultHandler.IsSupported)
        {
            _ = Task.Run(() => DefaultHandler.IsEnabled).ContinueWith(
                t =>
                {
                    associationWas = t.IsCompletedSuccessfully && t.Result;
                    fileAssociation.IsChecked = associationWas;
                    fileAssociation.IsEnabled = true;
                },
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        var lineNumbers = new ToggleSwitch { IsChecked = s.EditorLineNumbers, Content = Strings.Get(R.Settings_LineNumbers) };
        var monospace = new ToggleSwitch { IsChecked = s.EditorMonospace, Content = Strings.Get(R.Settings_Monospace) };
        var font = new TextBox { Text = s.EditorFontFamily, MinWidth = 260 };
        var monoFont = new ComboBox { MinWidth = 260 };
        var monoFonts = FontManager.Current.SystemFonts.Select(f => f.Name).Where(IsMonospaced).Order(StringComparer.CurrentCultureIgnoreCase).ToList();
        if (!monoFonts.Contains(s.EditorMonoFontFamily, StringComparer.OrdinalIgnoreCase))
        {
            monoFonts.Insert(0, s.EditorMonoFontFamily);
        }

        monoFont.ItemsSource = monoFonts;
        monoFont.SelectedItem = monoFonts.First(f => string.Equals(f, s.EditorMonoFontFamily, StringComparison.OrdinalIgnoreCase));
        var fontSize = new NumericUpDown { Value = (decimal)s.EditorFontSize, Minimum = 10, Maximum = 28, Increment = 1, MinWidth = 140 };
        var mode = Combo([Strings.Get(R.Mode_Raw), Strings.Get(R.Mode_Formatted)], (int)s.DefaultEditorMode);

        var cc = s.ClaudeCode;
        var ccEnabled = new ToggleSwitch { IsChecked = cc.Enabled, Content = Strings.Get(R.Settings_ClaudeEnabled) };
        var ccPath = new TextBox { Text = cc.ClaudeDirectory ?? string.Empty, PlaceholderText = host.ClaudeDirectory, MinWidth = 320 };
        var ccRedact = new CheckBox { IsChecked = cc.RedactSecrets, Content = Strings.Get(R.Settings_ClaudeRedact) };
        var ccSyncNow = Ui.FlatButton(Strings.Get(R.Settings_ClaudeSyncNow));
        ccSyncNow.Click += (_, _) => host.TriggerClaudeSync();
        var rebuild = Ui.FlatButton(Strings.Get(R.Settings_IndexRebuild));
        rebuild.Click += async (_, _) =>
        {
            rebuild.IsEnabled = false;
            await host.RebuildIndexAsync();
            rebuild.IsEnabled = true;
        };

        var save = Ui.FlatButton(Strings.Get(R.Common_Save));
        save.Background = Palette.TodayFill;
        save.Foreground = Palette.TodayInk;
        var cancel = Ui.FlatButton(Strings.Get(R.Common_Cancel));
        cancel.Click += (_, _) => Close();
        save.Click += async (_, _) =>
        {
            var newLanguage = languages[Math.Max(0, language.SelectedIndex)];
            _restartNeeded |= !string.Equals(newLanguage, s.Language, StringComparison.Ordinal)
                              || cc.Enabled != (ccEnabled.IsChecked == true)
                              || cc.RedactSecrets != (ccRedact.IsChecked == true)
                              || !string.Equals(cc.ClaudeDirectory, string.IsNullOrWhiteSpace(ccPath.Text) ? null : ccPath.Text.Trim(), StringComparison.Ordinal);
            s.Theme = (AppTheme)Math.Max(0, theme.SelectedIndex);
            s.Language = newLanguage;
            s.CloseToTray = closeToTray.IsChecked == true;
            s.EditorLineNumbers = lineNumbers.IsChecked == true;
            s.EditorMonospace = monospace.IsChecked == true;
            s.EditorFontFamily = string.IsNullOrWhiteSpace(font.Text) ? s.EditorFontFamily : font.Text.Trim();
            s.EditorMonoFontFamily = monoFont.SelectedItem as string ?? s.EditorMonoFontFamily;
            s.EditorFontSize = (double)(fontSize.Value ?? (decimal)s.EditorFontSize);
            s.DefaultEditorMode = (EditorMode)Math.Max(0, mode.SelectedIndex);
            cc.Enabled = ccEnabled.IsChecked == true;
            cc.ClaudeDirectory = string.IsNullOrWhiteSpace(ccPath.Text) ? null : ccPath.Text.Trim();
            cc.RedactSecrets = ccRedact.IsChecked == true;
            if (associationWas is { } was && (fileAssociation.IsChecked == true) != was)
            {
                var on = fileAssociation.IsChecked == true;
                try
                {
                    await Task.Run(() => DefaultHandler.Set(on));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _vm.ErrorNotice = Strings.Format(R.Settings_FileAssociationFailed, ex.Message);
                }
            }

            await SaveAsync();
            _applyAppearance();
            if (_restartNeeded)
            {
                restartRow.IsVisible = true;
                return;
            }

            Close();
        };

        var pages = new (string Icon, string Title, StackPanel Page)[]
        {
            (Icons.Settings, Strings.Get(R.Settings_General), Ui.VStack(
                10,
                Section(Strings.Get(R.Settings_Notes)),
                rootText,
                Ui.HStack(8, browse),
                movePanel,
                Field(Strings.Get(R.Settings_Language), language),
                closeToTray,
                Section(Strings.Get(R.Settings_Files)),
                fileAssociation,
                associationHint)),
            (Icons.Appearance, Strings.Get(R.Settings_Appearance), Ui.VStack(
                10,
                Field(Strings.Get(R.Settings_Theme), theme))),
            (Icons.Pencil, Strings.Get(R.Settings_Editor), Ui.VStack(
                10,
                lineNumbers,
                monospace,
                Field(Strings.Get(R.Settings_MonoFont), monoFont),
                Field(Strings.Get(R.Settings_FontFamily), font),
                Field(Strings.Get(R.Settings_FontSize), fontSize),
                Field(Strings.Get(R.Settings_DefaultMode), mode))),
            (Icons.Code, Strings.Get(R.Settings_ClaudeCode), Ui.VStack(
                10,
                ccEnabled,
                Field(Strings.Get(R.Settings_ClaudePath), ccPath),
                ccRedact,
                ccSyncNow)),
            (Icons.Sync, Strings.Get(R.Settings_Index), Ui.VStack(
                10,
                rebuild)),
        };
        var buttons = Ui.HStack(8, restartRow, cancel, save);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(24, 10);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(Categories(pages));
        Content = root;
    }

    private Task SaveAsync() => _vm.Host.SaveSettingsAsync();

    /// <summary>The category last shown – the window opens on it again while the app runs.</summary>
    private static int _lastCategory;

    /// <summary>Categories in a column on the left (as the Windows app), the chosen one's page on the right.</summary>
    private static Grid Categories((string Icon, string Title, StackPanel Page)[] pages)
    {
        var host = new ScrollViewer { Padding = new Thickness(20, 16, 24, 16) };
        var items = new List<(Button Button, Border Bar)>();
        void Show(int index)
        {
            _lastCategory = index;
            host.Content = pages[index].Page;
            host.Offset = default;
            for (var k = 0; k < items.Count; k++)
            {
                items[k].Button.Background = k == index ? Palette.SubtleFill : Palette.Transparent;
                items[k].Bar.Background = k == index ? Palette.Accent : Palette.Transparent;
            }
        }

        var nav = new StackPanel { Spacing = 2, Margin = new Thickness(12, 16, 8, 16) };
        for (var k = 0; k < pages.Length; k++)
        {
            var index = k;
            var label = Ui.HStack(10, Icons.Create(pages[k].Icon, 16, Palette.InkSecondary), Typo.Body(pages[k].Title, 14));
            var button = Ui.FlatButton(label, Palette.Transparent);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Padding = new Thickness(10, 7);
            button.Click += (_, _) => Show(index);
            var bar = new Border { Width = 3, Margin = new Thickness(0, 6) };
            items.Add((button, bar));
            nav.Children.Add(Ui.Columns((Ui.Px(3), bar), (Ui.Star, button)));
        }

        var layout = Ui.Columns((Ui.Px(210), nav), (Ui.Px(1), new Border { Width = 1, Background = Palette.Divider }), (Ui.Star, host));
        Show(Math.Clamp(_lastCategory, 0, pages.Length - 1));
        return layout;
    }

    private static ComboBox Combo(string[] items, int selected) => new() { ItemsSource = items, SelectedIndex = selected, MinWidth = 220 };

    private static TextBlock Section(string title)
    {
        var text = Typo.Caps(title.ToUpperInvariant(), 14, Palette.Ink);
        text.Margin = new Thickness(0, 12, 0, 0);
        return text;
    }

    private static Control Field(string label, Control control)
    {
        var grid = Ui.Columns((Ui.Px(200), Typo.Body(label, 13.5, Palette.InkSecondary)), (Ui.Star, control));
        ((Control)grid.Children[0]).VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        return grid;
    }

    /// <summary>The font's own fixed-pitch flag (as the Windows picker uses GDI's).</summary>
    private static bool IsMonospaced(string family)
    {
        try
        {
            var typeface = new Typeface(family);
            if (!FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphs))
            {
                return false;
            }

            return glyphs.Metrics.IsFixedPitch;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
