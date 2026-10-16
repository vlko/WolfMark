using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;
using WolfMark.Storage;

namespace WolfMark.App.Views;

/// <summary>Settings (SPEC §4.6, M8-04). Visual settings apply immediately, the rest after restart.</summary>
internal static class SettingsDialog
{
    public static async Task ShowAsync(XamlRoot root, MainViewModel vm, Microsoft.UI.WindowId windowId, Action applyAppearance)
    {
        var host = vm.Host;
        var s = host.Settings.Current;
        var restartNeeded = false;

        // Notes folder
        var rootText = Typo.Body(host.Root, 13, Palette.InkSecondary);
        rootText.TextWrapping = TextWrapping.Wrap;
        var browse = new Button { Content = Strings.Get(R.Settings_Browse) };
        var restartBar = new InfoBar
        {
            Severity = InfoBarSeverity.Informational,
            Message = Strings.Get(R.Settings_RestartRequired),
            IsClosable = false,
            IsOpen = false,
        };
        var restartButton = new Button { Content = Strings.Get(R.Settings_Restart) };
        restartButton.Click += async (_, _) =>
        {
            await vm.Editor.FlushAsync();
            await host.SaveSettingsAsync();
            AppInstance.Restart(string.Empty);
        };
        restartBar.ActionButton = restartButton;

        // Move-or-switch choice is inline: WinUI allows only one open ContentDialog.
        string? pickedRoot = null;
        var moveButton = new Button { Content = Strings.Get(R.Settings_Move) };
        var switchButton = new Button { Content = Strings.Get(R.Settings_Switch) };
        var cancelMove = new Button { Content = Strings.Get(R.Common_Cancel) };
        var moveText = Typo.Body(Strings.Get(R.Settings_MoveMessage), 13);
        moveText.TextWrapping = TextWrapping.Wrap;
        var movePanel = Ui.Ui.VStack(8, moveText, Ui.Ui.HStack(8, moveButton, switchButton, cancelMove));
        movePanel.Visibility = Visibility.Collapsed;

        browse.Click += async (_, _) =>
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(windowId);
            var picked = await picker.PickSingleFolderAsync();
            if (picked is null || string.Equals(picked.Path, host.Root, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            pickedRoot = picked.Path;
            moveText.Text = Strings.Get(R.Settings_MoveMessage) + Environment.NewLine + pickedRoot;
            movePanel.Visibility = Visibility.Visible;
        };

        async Task ApplyRootAsync(bool move)
        {
            if (pickedRoot is not { } newRoot)
            {
                return;
            }

            movePanel.Visibility = Visibility.Collapsed;
            if (move)
            {
                browse.IsEnabled = false;
                await vm.Editor.FlushAsync();
                var bin = new WolfMark.Storage.Trash.WindowsRecycleBin();
                await Task.Run(() => NotesMover.MoveAsync(host.Root, newRoot, bin, null, CancellationToken.None));
                browse.IsEnabled = true;
            }

            s.RootDirectory = newRoot;
            await host.SaveSettingsAsync();
            rootText.Text = newRoot;
            restartNeeded = true;
            restartBar.IsOpen = true;
        }

        moveButton.Click += async (_, _) => await ApplyRootAsync(move: true);
        switchButton.Click += async (_, _) => await ApplyRootAsync(move: false);
        cancelMove.Click += (_, _) => movePanel.Visibility = Visibility.Collapsed;

        // Appearance
        var theme = Combo([Strings.Get(R.Settings_ThemeSystem), Strings.Get(R.Settings_ThemeLight), Strings.Get(R.Settings_ThemeDark)], (int)s.Theme);
        var backdrop = Combo(["Mica", "Acrylic", Strings.Get(R.Settings_BackdropNone)], (int)s.Backdrop);
        var languages = new[] { (string?)null, "sk-SK", "en-US" };
        var language = Combo([Strings.Get(R.Settings_LanguageSystem), "Slovenčina", "English"], Array.IndexOf(languages, s.Language) is var li and >= 0 ? li : 0);

        // Editor
        var font = new TextBox { Text = s.EditorFontFamily, MinWidth = 260 };
        var fontSize = new NumberBox { Value = s.EditorFontSize, Minimum = 10, Maximum = 28, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, SmallChange = 1 };
        var lineWidth = new NumberBox { Value = s.EditorMaxLineWidth, Minimum = 400, Maximum = 2000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, SmallChange = 20 };
        var mode = Combo([Strings.Get(R.Mode_Raw), Strings.Get(R.Mode_Formatted)], (int)s.DefaultEditorMode);

        // Claude Code
        var cc = s.ClaudeCode;
        var ccEnabled = new ToggleSwitch { IsOn = cc.Enabled, Header = Strings.Get(R.Settings_ClaudeEnabled) };
        var ccPath = new TextBox { Text = cc.ClaudeDirectory ?? string.Empty, Header = Strings.Get(R.Settings_ClaudePath), PlaceholderText = host.ClaudeDirectory };
        var ccExcluded = new TextBox
        {
            Text = string.Join(Environment.NewLine, cc.ExcludedProjects),
            Header = Strings.Get(R.Settings_ClaudeExcluded),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 70,
        };
        var ccRedact = new CheckBox { Content = Strings.Get(R.Settings_ClaudeRedact), IsChecked = cc.RedactSecrets };
        var ccWatch = new CheckBox { Content = Strings.Get(R.Settings_ClaudeWatch), IsChecked = cc.WatchInRealTime };
        var ccInterval = new NumberBox { Header = Strings.Get(R.Settings_ClaudeInterval), Value = cc.IntervalMinutes, Minimum = 1, Maximum = 1440, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var ccSyncNow = new Button { Content = Strings.Get(R.Settings_ClaudeSyncNow), IsEnabled = host.ClaudeSync is not null };
        ccSyncNow.Click += (_, _) => host.TriggerClaudeSync();

        // Index
        var semantic = Typo.Body(Strings.Get(host.EmbeddingProvider is not null && host.Db.VectorsAvailable ? R.Settings_IndexSemanticOn : R.Settings_IndexSemanticOff), 12.5, Palette.InkSecondary);
        var rebuild = new Button { Content = Strings.Get(R.Settings_IndexRebuild) };
        rebuild.Click += async (_, _) =>
        {
            rebuild.IsEnabled = false;
            await host.RebuildIndexAsync();
            rebuild.IsEnabled = true;
        };
        var paths = Typo.Body($"{host.Paths.IndexDatabase}\n{host.Paths.LogsDirectory}", 11.5, Palette.InkTertiary);
        paths.TextWrapping = TextWrapping.Wrap;
        paths.IsTextSelectionEnabled = true;

        var content = Ui.Ui.VStack(
            10,
            restartBar,
            Section(Strings.Get(R.Settings_Notes)),
            Ui.Ui.HStack(12, browse, rootText),
            movePanel,
            Section(Strings.Get(R.Settings_Appearance)),
            Field(Strings.Get(R.Settings_Theme), theme),
            Field(Strings.Get(R.Settings_Backdrop), backdrop),
            Field(Strings.Get(R.Settings_Language), language),
            Section(Strings.Get(R.Settings_Editor)),
            Field(Strings.Get(R.Settings_FontFamily), font),
            Field(Strings.Get(R.Settings_FontSize), fontSize),
            Field(Strings.Get(R.Settings_LineWidth), lineWidth),
            Field(Strings.Get(R.Settings_DefaultMode), mode),
            Section(Strings.Get(R.Settings_ClaudeCode)),
            ccEnabled,
            ccPath,
            ccExcluded,
            Ui.Ui.HStack(16, ccRedact, ccWatch),
            Ui.Ui.HStack(12, ccInterval, ccSyncNow),
            Section(Strings.Get(R.Settings_Index)),
            semantic,
            rebuild,
            paths);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Strings.Get(R.Settings_Title),
            Content = new ScrollViewer { Content = content, MaxHeight = 620, Padding = new Thickness(0, 0, 16, 0) },
            PrimaryButtonText = "OK",
            CloseButtonText = Strings.Get(R.Common_Cancel),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            if (restartNeeded)
            {
                await ShowRestartAsync(root, vm);
            }

            return;
        }

        s.Theme = (AppTheme)theme.SelectedIndex;
        s.Backdrop = (BackdropKind)backdrop.SelectedIndex;
        var newLanguage = languages[Math.Max(0, language.SelectedIndex)];
        restartNeeded |= !string.Equals(newLanguage, s.Language, StringComparison.Ordinal);
        s.Language = newLanguage;
        s.EditorFontFamily = string.IsNullOrWhiteSpace(font.Text) ? s.EditorFontFamily : font.Text.Trim();
        s.EditorFontSize = double.IsNaN(fontSize.Value) ? s.EditorFontSize : fontSize.Value;
        s.EditorMaxLineWidth = double.IsNaN(lineWidth.Value) ? s.EditorMaxLineWidth : lineWidth.Value;
        s.DefaultEditorMode = (EditorMode)mode.SelectedIndex;

        var excluded = ccExcluded.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var ccDir = string.IsNullOrWhiteSpace(ccPath.Text) ? null : ccPath.Text.Trim();
        restartNeeded |= cc.Enabled != ccEnabled.IsOn
                         || cc.RedactSecrets != (ccRedact.IsChecked == true)
                         || cc.WatchInRealTime != (ccWatch.IsChecked == true)
                         || cc.IntervalMinutes != (int)ccInterval.Value
                         || !string.Equals(cc.ClaudeDirectory, ccDir, StringComparison.Ordinal)
                         || !cc.ExcludedProjects.SequenceEqual(excluded);
        cc.Enabled = ccEnabled.IsOn;
        cc.ClaudeDirectory = ccDir;
        cc.ExcludedProjects = excluded;
        cc.RedactSecrets = ccRedact.IsChecked == true;
        cc.WatchInRealTime = ccWatch.IsChecked == true;
        cc.IntervalMinutes = double.IsNaN(ccInterval.Value) ? cc.IntervalMinutes : (int)ccInterval.Value;

        if (newLanguage is not null)
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = newLanguage;
        }

        await host.SaveSettingsAsync();
        applyAppearance();

        if (restartNeeded)
        {
            await ShowRestartAsync(root, vm);
        }
    }

    private static async Task ShowRestartAsync(XamlRoot root, MainViewModel vm)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = Strings.Get(R.Settings_Title),
            Content = Strings.Get(R.Settings_RestartRequired),
            PrimaryButtonText = Strings.Get(R.Settings_Restart),
            CloseButtonText = Strings.Get(R.Common_Close),
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await vm.Editor.FlushAsync();
            await vm.Host.SaveSettingsAsync();
            AppInstance.Restart(string.Empty);
        }
    }

    private static ComboBox Combo(string[] items, int selected)
    {
        var combo = new ComboBox { MinWidth = 200 };
        foreach (var item in items)
        {
            combo.Items.Add(item);
        }

        combo.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
        return combo;
    }

    private static TextBlock Section(string title)
    {
        var t = Typo.Caps(title, 11.5, Palette.InkSecondary);
        t.Margin = new Thickness(0, 14, 0, 0);
        return t;
    }

    private static Grid Field(string label, FrameworkElement control)
    {
        var grid = Ui.Ui.Columns((Ui.Ui.Px(170), Typo.Body(label, 13.5)), (Ui.Ui.Star, control));
        control.HorizontalAlignment = HorizontalAlignment.Left;
        return grid;
    }
}
