using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;
using WolfMark.Core.Tags;
using WolfMark.Index.Queries;
using WolfMark.Storage;
using WolfMark.Storage.Settings;

namespace WolfMark.App.Views;

/// <summary>Settings (SPEC §4.6, M8-04). Visual settings apply immediately, the rest after restart.</summary>
internal static class SettingsDialog
{
    public static async Task ShowAsync(XamlRoot root, MainViewModel vm, Microsoft.UI.WindowId windowId, Action applyAppearance, Action<string?> previewIcon)
    {
        var host = vm.Host;
        var s = host.Settings.Current;
        var restartNeeded = false;

        // Notes folder
        var rootText = Typo.Body(host.Root, 13, Palette.InkSecondary);
        rootText.TextWrapping = TextWrapping.Wrap;
        var browse = Ui.Ui.TextButton(Strings.Get(R.Settings_Browse));
        var restartBar = new InfoBar
        {
            Severity = InfoBarSeverity.Informational,
            Message = Strings.Get(R.Settings_RestartRequired),
            IsClosable = false,
            IsOpen = false,
        };
        var restartButton = Ui.Ui.TextButton(Strings.Get(R.Settings_Restart));
        restartButton.Click += async (_, _) =>
        {
            await vm.Editor.FlushAsync();
            await host.SaveSettingsAsync();
            AppInstance.Restart(string.Empty);
        };
        restartBar.ActionButton = restartButton;

        // Move-or-switch choice is inline: WinUI allows only one open ContentDialog.
        string? pickedRoot = null;
        var moveButton = Ui.Ui.TextButton(Strings.Get(R.Settings_Move));
        var switchButton = Ui.Ui.TextButton(Strings.Get(R.Settings_Switch));
        var cancelMove = Ui.Ui.TextButton(Strings.Get(R.Common_Cancel));
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

            s.RootDirectory = host.Settings.ToStoredRoot(newRoot);
            await host.SaveSettingsAsync();
            rootText.Text = newRoot;
            restartNeeded = true;
            restartBar.IsOpen = true;
        }

        moveButton.Click += async (_, _) => await ApplyRootAsync(move: true);
        switchButton.Click += async (_, _) => await ApplyRootAsync(move: false);
        cancelMove.Click += (_, _) => movePanel.Visibility = Visibility.Collapsed;

        // Portable mode (ADR 0009): copy everything next to the app, or back; then a restart.
        var portableData = host.PortableDataDirectory;
        var isPortable = portableData is not null;
        var portableStatus = Typo.Body(isPortable ? Strings.Format(R.Settings_PortableOn, portableData!) : Strings.Get(R.Settings_PortableOff), 13, Palette.InkSecondary);
        portableStatus.TextWrapping = TextWrapping.Wrap;
        portableStatus.IsTextSelectionEnabled = true;
        var portableButton = Ui.Ui.TextButton(Strings.Get(isPortable ? R.Settings_PortableDisable : R.Settings_PortableEnable));
        var availability = isPortable || LaunchOptions.Current.IsDemo
            ? PortableUnavailableReason.None
            : await Task.Run(() => PortableMode.CheckAvailability(PortableMode.AppDirectory));
        portableButton.IsEnabled = !LaunchOptions.Current.IsDemo && availability == PortableUnavailableReason.None;
        var portableReason = Typo.Body(
            availability switch
            {
                PortableUnavailableReason.NotWritable => Strings.Format(R.Settings_PortableNotWritable, PortableMode.AppDirectory),
                PortableUnavailableReason.AppBundle => Strings.Get(R.Settings_PortableAppBundle),
                _ => string.Empty,
            },
            12.5,
            Palette.InkTertiary);
        portableReason.TextWrapping = TextWrapping.Wrap;
        portableReason.Visibility = availability == PortableUnavailableReason.None ? Visibility.Collapsed : Visibility.Visible;

        var portableWarning = Typo.Body(string.Empty, 13);
        portableWarning.TextWrapping = TextWrapping.Wrap;
        var portableCopy = Ui.Ui.TextButton(Strings.Get(isPortable ? R.Settings_PortableCopyBack : R.Settings_PortableCopy));
        var portableMove = Ui.Ui.TextButton(Strings.Get(R.Settings_PortableMove));
        portableMove.Visibility = isPortable ? Visibility.Collapsed : Visibility.Visible;
        var portableCancel = Ui.Ui.TextButton(Strings.Get(R.Common_Cancel));
        var portableProgress = Typo.Body(string.Empty, 12.5, Palette.InkSecondary);
        portableProgress.TextWrapping = TextWrapping.Wrap;
        var portablePanel = Ui.Ui.VStack(8, portableWarning, Ui.Ui.HStack(8, portableCopy, portableMove, portableCancel), portableProgress);
        portablePanel.Visibility = Visibility.Collapsed;

        portableButton.Click += async (_, _) =>
        {
            portableWarning.Text = isPortable
                ? Strings.Format(R.Settings_PortableDisableWarning, await Task.Run(() => PortableMigrator.StandardNotesRoot(LocalPaths.Standard)), portableData!)
                : Strings.Format(R.Settings_PortableEnableWarning, PortableMode.DefaultDataDirectory(PortableMode.AppDirectory));
            portableProgress.Text = string.Empty;
            portablePanel.Visibility = Visibility.Visible;
        };
        portableCancel.Click += (_, _) => portablePanel.Visibility = Visibility.Collapsed;

        async Task SwitchPortableAsync(bool moveNotes)
        {
            portableButton.IsEnabled = portableCopy.IsEnabled = portableMove.IsEnabled = portableCancel.IsEnabled = browse.IsEnabled = false;
            await vm.Editor.FlushAsync();
            var progress = new Progress<int>(n => portableProgress.Text = Strings.Format(R.Settings_PortableProgress, n));
            try
            {
                var result = isPortable
                    ? await host.DisablePortableAsync(progress)
                    : await host.EnablePortableAsync(moveNotes, progress);
                if (!isPortable)
                {
                    await Task.Run(SystemIntegration.Remove);
                }

                if (result.CleanupError is { } cleanup)
                {
                    portableProgress.Text = Strings.Format(R.Settings_PortableCleanupFailed, cleanup.Message);
                    restartNeeded = true;
                    restartBar.IsOpen = true;
                    return;
                }

                AppInstance.Restart(string.Empty);
            }
            catch (Exception ex)
            {
                // Watcher and Claude Code sync are stopped: only a restart brings them back.
                portableProgress.Text = Strings.Format(R.Settings_PortableFailed, ex.Message);
                portableCancel.IsEnabled = true;
                restartNeeded = true;
                restartBar.IsOpen = true;
            }
        }

        portableCopy.Click += async (_, _) => await SwitchPortableAsync(moveNotes: false);
        portableMove.Click += async (_, _) => await SwitchPortableAsync(moveNotes: true);

        // Appearance
        var theme = Combo([Strings.Get(R.Settings_ThemeSystem), Strings.Get(R.Settings_ThemeLight), Strings.Get(R.Settings_ThemeDark)], (int)s.Theme);
        var languages = new[] { (string?)null, "sk-SK", "en-US" };
        var language = Combo([Strings.Get(R.Settings_LanguageSystem), "Slovenčina", "English"], Array.IndexOf(languages, s.Language) is var li and >= 0 ? li : 0);

        // App icon: thumbnails, click = live preview of the window/taskbar icon; OK keeps it, Cancel reverts.
        var chosenIcon = s.AppIcon ?? AppIcons.Default;
        var iconFrames = new List<(string Id, Border Frame)>();
        var iconRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        void MarkIcon()
        {
            // The selection is a frame around the button: the button's own resources must not change
            // after styling (ResourceDictionary insert throws 0x800F0902).
            foreach (var (id, frame) in iconFrames)
            {
                frame.BorderBrush = id == chosenIcon ? Palette.Accent : Palette.Transparent;
            }
        }

        foreach (var (id, nameKey) in AppIcons.All)
        {
            var image = new Microsoft.UI.Xaml.Controls.Image { Width = 40, Height = 40 };
            var png = AppIcons.PngPath(id);
            if (File.Exists(png) && Uri.TryCreate(png, UriKind.Absolute, out var uri))
            {
                // A missing/broken thumbnail must not take the whole dialog down.
                image.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
            }

            var button = new Button { Content = image, Padding = new Thickness(3), CornerRadius = Radius.Control };
            button.StyleButton(Palette.Transparent, Palette.Ink);
            var name = Strings.Get(nameKey);
            ToolTipService.SetToolTip(button, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            var iconId = id;
            button.Click += (_, _) =>
            {
                chosenIcon = iconId;
                MarkIcon();
                previewIcon(iconId);
            };
            var frame = new Border { Child = button, BorderThickness = new Thickness(2), CornerRadius = Radius.Tag };
            iconFrames.Add((id, frame));
            iconRow.Children.Add(frame);
        }

        MarkIcon();

        // Editor
        var font = new TextBox { Text = s.EditorFontFamily, MinWidth = 260, IsEnabled = !s.EditorMonospace };
        var monospace = new ToggleSwitch { IsOn = s.EditorMonospace, Header = Strings.Get(R.Settings_Monospace) };
        monospace.Toggled += (_, _) => font.IsEnabled = !monospace.IsOn;
        var monoFont = new MonoFontPicker(s.EditorMonoFontFamily);
        var lineNumbers = new ToggleSwitch { IsOn = s.EditorLineNumbers, Header = Strings.Get(R.Settings_LineNumbers) };
        var colorEmoji = new ToggleSwitch { IsOn = s.EditorColorEmoji, Header = Strings.Get(R.Settings_ColorEmoji) };
        var fontSize = new NumberBox { Value = s.EditorFontSize, Minimum = LocalSettings.EditorFontSizeMin, Maximum = LocalSettings.EditorFontSizeMax, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, SmallChange = 1 };
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
        var ccSyncNow = Ui.Ui.TextButton(Strings.Get(R.Settings_ClaudeSyncNow));
        ccSyncNow.IsEnabled = host.ClaudeSync is not null;
        ccSyncNow.Click += (_, _) => host.TriggerClaudeSync();

        // Index
        var semantic = Typo.Body(Strings.Get(host.EmbeddingProvider is not null && host.Db.VectorsAvailable ? R.Settings_IndexSemanticOn : R.Settings_IndexSemanticOff), 12.5, Palette.InkSecondary);
        var rebuild = Ui.Ui.TextButton(Strings.Get(R.Settings_IndexRebuild));
        rebuild.Click += async (_, _) =>
        {
            rebuild.IsEnabled = false;
            await host.RebuildIndexAsync();
            rebuild.IsEnabled = true;
        };
        var testNotice = Ui.Ui.TextButton(Strings.Get(R.Settings_TestNotice), () => vm.IsTestNoticeOpen = true);
        // Windows: start at sign-in (registry Run entry is the truth) and close to the notification area.
        var startupWas = await Task.Run(() => StartupRegistration.IsEnabled);
        // Portable mode writes nothing to this computer's system settings: both integrations are off there.
        var startWithWindows = new ToggleSwitch { IsOn = startupWas, Header = Strings.Get(R.Settings_StartWithWindows), IsEnabled = !LaunchOptions.Current.IsDemo && !isPortable };
        var closeToTray = new ToggleSwitch { IsOn = s.CloseToTray, Header = Strings.Get(R.Settings_CloseToTray) };
        // .md files: the association of this exe (registry is the truth); Windows confirms the default itself.
        var associationWas = await Task.Run(() => FileAssociation.IsEnabled);
        var fileAssociation = new ToggleSwitch { IsOn = associationWas, Header = Strings.Get(R.Settings_FileAssociation), IsEnabled = !isPortable };
        var defaultApps = Ui.Ui.TextButton(Strings.Get(R.Settings_FileAssociationDefault), () => _ = FileAssociation.OpenDefaultAppsAsync());
        defaultApps.IsEnabled = !isPortable;
        TextBlock PortableUnavailable()
        {
            var t = Typo.Body(Strings.Get(R.Settings_PortableUnavailable), 12.5, Palette.InkTertiary);
            t.TextWrapping = TextWrapping.Wrap;
            t.Visibility = isPortable ? Visibility.Visible : Visibility.Collapsed;
            return t;
        }

        var associationHint = Typo.Body(Strings.Get(R.Settings_FileAssociationHint), 12.5, Palette.InkTertiary);
        associationHint.TextWrapping = TextWrapping.Wrap;
        associationHint.Visibility = isPortable ? Visibility.Collapsed : Visibility.Visible;


        var paths = Typo.Body($"{host.Paths.IndexDatabase}\n{host.Paths.LogsDirectory}", 11.5, Palette.InkTertiary);
        paths.TextWrapping = TextWrapping.Wrap;
        paths.IsTextSelectionEnabled = true;

        // Version and environment, selectable and copyable for bug reports (the index size is read off the UI thread).
        var scale = root.RasterizationScale;
        var noteCount = vm.NoteCount;
        var diagnostics = await Task.Run(() => DiagnosticInfo.Build(host, noteCount, scale));
        var diagnosticText = Typo.Body(diagnostics, 11.5, Palette.InkTertiary);
        diagnosticText.TextWrapping = TextWrapping.Wrap;
        diagnosticText.IsTextSelectionEnabled = true;
        var copyDiagnostics = Ui.Ui.TextButton(Strings.Get(R.Settings_CopyDiagnostics), () =>
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(diagnostics);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        });

        var iconPicker = new ScrollViewer
        {
            Content = iconRow,
            HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
        };
        ContentDialog? dialogRef = null;
        var showNotesClose = false; // Tags → "Show notes" closes the dialog like OK
        var pages = new (string Glyph, string Title, StackPanel Page)[]
        {
            (Glyphs.Settings, Strings.Get(R.Settings_General), Ui.Ui.VStack(
                10,
                Section(Strings.Get(R.Settings_Notes)),
                Ui.Ui.HStack(12, browse, rootText),
                movePanel,
                Field(Strings.Get(R.Settings_Language), language),
                Section(Strings.Get(R.Settings_System)),
                startWithWindows,
                PortableUnavailable(),
                closeToTray,
                Section(Strings.Get(R.Settings_Files)),
                fileAssociation,
                associationHint,
                PortableUnavailable(),
                defaultApps,
                Section(Strings.Get(R.Settings_Portable)),
                portableStatus,
                portableButton,
                portableReason,
                portablePanel)),
            (Glyphs.Appearance, Strings.Get(R.Settings_Appearance), Ui.Ui.VStack(
                10,
                Field(Strings.Get(R.Settings_Theme), theme),
                Field(Strings.Get(R.Settings_AppIcon), iconPicker))),
            (Glyphs.Edit, Strings.Get(R.Settings_Editor), Ui.Ui.VStack(
                10,
                lineNumbers,
                colorEmoji,
                monospace,
                Field(Strings.Get(R.Settings_MonoFont), monoFont.Element),
                Field(Strings.Get(R.Settings_FontFamily), font),
                Field(Strings.Get(R.Settings_FontSize), fontSize),
                Field(Strings.Get(R.Settings_DefaultMode), mode))),
            (Glyphs.Tag, Strings.Get(R.Settings_Tags), TagsPage(vm, () => { showNotesClose = true; dialogRef?.Hide(); })),
            (Glyphs.Code, Strings.Get(R.Settings_ClaudeCode), Ui.Ui.VStack(
                10,
                ccEnabled,
                ccPath,
                ccExcluded,
                Ui.Ui.HStack(16, ccRedact, ccWatch),
                Ui.Ui.HStack(12, ccInterval, ccSyncNow))),
            (Glyphs.Sync, Strings.Get(R.Settings_IndexDiagnostics), Ui.Ui.VStack(
                10,
                Section(Strings.Get(R.Settings_Index)),
                semantic,
                rebuild,
                paths,
                Section(Strings.Get(R.Settings_Diagnostics)),
                diagnosticText,
                copyDiagnostics,
                testNotice)),
        };

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = $"{Strings.Get(R.Settings_Title)} · {DiagnosticInfo.ShortVersion}{(isPortable ? " · " + Strings.Get(R.Settings_PortableTitle) : string.Empty)}",
            Content = Categories(pages, restartBar),
            PrimaryButtonText = "OK",
            CloseButtonText = Strings.Get(R.Common_Cancel),
            DefaultButton = ContentDialogButton.Primary,
        };
        // The default dialog is at most 548 px wide – too narrow for the category list beside the page.
        dialog.Resources["ContentDialogMaxWidth"] = 900.0;
        dialog.Resources["ContentDialogMaxHeight"] = 900.0;
        dialogRef = dialog;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary && !showNotesClose)
        {
            previewIcon(s.AppIcon); // revert the preview
            if (restartNeeded)
            {
                await ShowRestartAsync(root, vm);
            }

            return;
        }

        s.Theme = (AppTheme)theme.SelectedIndex;
        s.AppIcon = chosenIcon == AppIcons.Default ? null : chosenIcon;
        var newLanguage = languages[Math.Max(0, language.SelectedIndex)];
        restartNeeded |= !string.Equals(newLanguage, s.Language, StringComparison.Ordinal);
        s.Language = newLanguage;
        s.EditorFontFamily = string.IsNullOrWhiteSpace(font.Text) ? s.EditorFontFamily : font.Text.Trim();
        s.EditorMonospace = monospace.IsOn;
        s.EditorMonoFontFamily = monoFont.Selected;
        s.EditorLineNumbers = lineNumbers.IsOn;
        s.EditorColorEmoji = colorEmoji.IsOn;
        s.EditorFontSize = double.IsNaN(fontSize.Value) ? s.EditorFontSize : fontSize.Value;
        s.DefaultEditorMode = (EditorMode)mode.SelectedIndex;
        s.CloseToTray = closeToTray.IsOn;
        if (!LaunchOptions.Current.IsDemo && !isPortable && (startWithWindows.IsOn != startupWas || startupWas))
        {
            // Re-written also when unchanged: keeps the path current if the exe moved.
            var on = startWithWindows.IsOn;
            try
            {
                await Task.Run(() => StartupRegistration.Set(on));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                vm.ErrorNotice = Strings.Format(R.Settings_StartupFailed, ex.Message);
            }
        }

        if (!isPortable && fileAssociation.IsOn != associationWas)
        {
            var on = fileAssociation.IsOn;
            try
            {
                await Task.Run(() => FileAssociation.Set(on));
                if (on)
                {
                    await FileAssociation.OpenDefaultAppsAsync();
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                vm.ErrorNotice = Strings.Format(R.Settings_FileAssociationFailed, ex.Message);
            }
        }

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

    /// <summary>The category last shown – the dialog opens on it again while the app runs.</summary>
    private static int _lastCategory;

    /// <summary>
    /// Categories in a column on the left (as Windows Settings), the chosen one's page on the right. A fixed size,
    /// so the dialog does not jump when switching; the restart notice stays above every page.
    /// </summary>
    private static Grid Categories((string Glyph, string Title, StackPanel Page)[] pages, InfoBar restartBar)
    {
        var host = new ScrollViewer { Padding = new Thickness(20, 0, 16, 0) };
        var items = new List<(Border Fill, Microsoft.UI.Xaml.Shapes.Rectangle Bar)>();
        void Show(int index)
        {
            _lastCategory = index;
            host.Content = pages[index].Page;
            host.ChangeView(null, 0, null, disableAnimation: true);
            for (var k = 0; k < items.Count; k++)
            {
                // The fill is on a frame around the button: a button's resources must not change after styling.
                items[k].Fill.Background = k == index ? Palette.SubtleFill : Palette.Transparent;
                items[k].Bar.Fill = k == index ? Palette.Accent : Palette.Transparent;
            }
        }

        var nav = new StackPanel { Spacing = 2 };
        for (var k = 0; k < pages.Length; k++)
        {
            var index = k;
            var label = Ui.Ui.HStack(10, Typo.Icon(pages[k].Glyph, 14, Palette.InkSecondary), Typo.Body(pages[k].Title, 14));
            var button = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 7, 10, 7),
                CornerRadius = Radius.None,
            };
            button.StyleButton(Palette.Transparent, Palette.Ink);
            button.Click += (_, _) => Show(index);
            var bar = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 3, Margin = new Thickness(0, 6, 0, 6) };
            var fill = new Border { Child = Ui.Ui.Columns((Ui.Ui.Px(3), bar), (Ui.Ui.Star, button)), CornerRadius = Radius.Control };
            items.Add((fill, bar));
            nav.Children.Add(fill);
        }

        var divider = new Border { Width = 1, Background = Palette.Divider };
        var page = Ui.Ui.Rows((Ui.Ui.Auto, restartBar), (Ui.Ui.Star, host));
        restartBar.Margin = new Thickness(20, 0, 16, 8);
        var layout = Ui.Ui.Columns((Ui.Ui.Px(200), nav), (Ui.Ui.Px(1), divider), (Ui.Ui.Star, page));
        layout.Width = 780;
        layout.Height = 540;
        Show(Math.Clamp(_lastCategory, 0, pages.Length - 1));
        return layout;
    }

    /// <summary>
    /// Tags (M8-05): the list of tags, a field to add one, and per tag a flyout (the arrow) with its colour, calendar
    /// labels, "show notes", rename (or merge) and removal. Everything applies at once, not on OK – like moving the
    /// notes folder. The checks, texts and the work are in <see cref="TagManagerModel"/>.
    /// </summary>
    /// <param name="showNotes">Closes Settings as OK, so the main window shows the filtered history.</param>
    private static StackPanel TagsPage(MainViewModel vm, Action showNotes)
    {
        var model = new TagManagerModel(vm);
        var intro = Typo.Body(Strings.Get(R.Settings_TagsIntro), 13, Palette.InkSecondary);
        intro.TextWrapping = TextWrapping.Wrap;
        var mergeNote = Typo.Body(Strings.Get(R.Settings_TagsMergeNote), 12.5, Palette.InkTertiary);
        mergeNote.TextWrapping = TextWrapping.Wrap;
        mergeNote.Margin = new Thickness(0, 8, 0, 0);
        var status = Typo.Body(string.Empty, 12.5, Palette.InkSecondary);
        status.TextWrapping = TextWrapping.Wrap;
        var red = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed);

        // New tag
        var newName = new TextBox { PlaceholderText = Strings.Get(R.Settings_TagsNewPlaceholder), MinWidth = 220 };
        var add = Ui.Ui.TextButton(Strings.Get(R.Settings_TagsAdd));
        add.IsEnabled = false;
        var newHint = Typo.Body(string.Empty, 12.5, red);
        var newTag = string.Empty;
        newName.TextChanged += (_, _) =>
        {
            var problem = model.CheckNew(newName.Text, out newTag);
            newHint.Text = problem ?? string.Empty;
            add.IsEnabled = problem is null;
        };

        var list = new StackPanel { Spacing = 2 };
        var busy = false;

        async Task RunAsync(Func<IProgress<int>, Task<string>> work)
        {
            busy = true;
            list.IsHitTestVisible = false;
            list.Opacity = 0.6;
            try
            {
                status.Text = await work(new Progress<int>(n => status.Text = Strings.Format(R.Settings_TagsProgress, n)));
            }
            finally
            {
                busy = false;
                list.IsHitTestVisible = true;
                list.Opacity = 1;
                Render();
            }
        }

        async Task AddAsync()
        {
            if (busy || !add.IsEnabled)
            {
                return;
            }

            var tag = newTag;
            newName.Text = string.Empty;
            await RunAsync(_ => model.AddAsync(tag));
        }

        add.Click += async (_, _) => await AddAsync();
        newName.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await AddAsync();
            }
        };

        void Render()
        {
            list.Children.Clear();
            foreach (var t in model.Tags)
            {
                list.Children.Add(TagRow(model, t, RunAsync, showNotes));
            }

            if (model.Tags.Count == 0)
            {
                list.Children.Add(Typo.Body(Strings.Get(R.Settings_TagsNone), 13, Palette.InkTertiary));
            }
        }

        _ = LoadAsync();

        async Task LoadAsync()
        {
            await model.LoadAsync();
            Render();
        }

        return Ui.Ui.VStack(
            10,
            intro,
            Ui.Ui.HStack(8, newName, add),
            newHint,
            list,
            status,
            mergeNote);
    }

    /// <summary>One tag: colour square, name, count and the arrow that opens its flyout.</summary>
    private static Grid TagRow(TagManagerModel model, TagUsage t, Func<Func<IProgress<int>, Task<string>>, Task> run, Action showNotes)
    {
        var square = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = 12,
            Height = 12,
            Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Palette.ToColor(model.ColorOf(t))),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var flyout = new Flyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        var arrow = Ui.Ui.IconButton(Glyphs.ChevronDown, Strings.Get(R.Settings_TagsMenu), () => { }, 12);
        arrow.Flyout = flyout;
        flyout.Opening += (_, _) => flyout.Content = TagFlyout(model, t, flyout, square, run, showNotes);
        var row = Ui.Ui.Columns(
            (Ui.Ui.Auto, square),
            (Ui.Ui.Star, Typo.Body("#" + t.Tag, 13.5)),
            (Ui.Ui.Auto, Typo.Body(model.CountText(t), 12.5, Palette.InkTertiary)),
            (Ui.Ui.Px(44), arrow));
        ((FrameworkElement)row.Children[2]).VerticalAlignment = VerticalAlignment.Center;
        ((FrameworkElement)row.Children[2]).Margin = new Thickness(0, 0, 8, 0);
        arrow.HorizontalAlignment = HorizontalAlignment.Right;
        row.Padding = new Thickness(4, 2, 0, 2);
        return row;
    }

    /// <summary>The tag's flyout: colour, calendar labels, show notes, rename, remove (the last two confirm inside).</summary>
    private static StackPanel TagFlyout(TagManagerModel model, TagUsage t, Flyout flyout, Microsoft.UI.Xaml.Shapes.Rectangle square, Func<Func<IProgress<int>, Task<string>>, Task> run, Action showNotes)
    {
        var red = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed);
        var panel = new StackPanel { Spacing = 8, Width = 280 };
        panel.Children.Add(Typo.Caps(Strings.Get(R.Tag_Color) + " · #" + t.Tag));

        var current = model.ColorOf(t);
        var swatches = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 7, ItemWidth = 36, ItemHeight = 36 };
        foreach (var color in TagPalette.Colors)
        {
            var c = color;
            var swatch = new Button { Width = 30, Height = 30, Padding = new Thickness(0), CornerRadius = Radius.Control };
            swatch.StyleButton(new Microsoft.UI.Xaml.Media.SolidColorBrush(Palette.ToColor(c)), new Microsoft.UI.Xaml.Media.SolidColorBrush(Palette.ForegroundOn(c)), onColor: true);
            if (c == current)
            {
                swatch.BorderThickness = new Thickness(3);
                swatch.BorderBrush = Palette.Ink;
                swatch.Resources["ButtonBorderBrush"] = Palette.Ink;
            }

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, c.ToHex());
            swatch.Click += async (_, _) =>
            {
                flyout.Hide();
                square.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Palette.ToColor(c));
                await model.SetColorAsync(t, c);
            };
            swatches.Children.Add(swatch);
        }

        panel.Children.Add(swatches);
        var labels = new CheckBox { Content = Strings.Get(R.Tag_ShowLabels), IsChecked = model.ShowsLabels(t) };
        labels.Click += async (_, _) =>
        {
            var show = labels.IsChecked == true; // read on the UI thread; the file write goes to the pool
            await model.SetLabelsAsync(t, show);
        };
        panel.Children.Add(labels);
        panel.Children.Add(new Border { Height = 1, Background = Palette.Divider });

        // The actions; rename and remove open their confirmation below instead of a dialog (WinUI allows one at a time).
        var actions = new StackPanel { Spacing = 4 };
        var confirm = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        Button Action(string text, Action onClick)
        {
            var b = Ui.Ui.TextButton(text, onClick);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            return b;
        }

        actions.Children.Add(Action(Strings.Get(R.Settings_TagsShowNotes), () =>
        {
            flyout.Hide();
            _ = model.ShowNotesAsync(t);
            showNotes();
        }));
        if (!TagManagerModel.IsReserved(t))
        {
            actions.Children.Add(Action(Strings.Get(R.Settings_TagsRenameAction), () =>
            {
                var name = new TextBox { Text = t.Tag };
                var hint = Typo.Body(string.Empty, 12.5, red);
                hint.TextWrapping = TextWrapping.Wrap;
                var warning = Typo.Body(string.Empty, 12.5, Palette.InkSecondary);
                warning.TextWrapping = TextWrapping.Wrap;
                var ok = Ui.Ui.TextButton(Strings.Get(R.Settings_TagsConfirmRename));
                ok.IsEnabled = false;
                var target = string.Empty;
                name.TextChanged += (_, _) =>
                {
                    var problem = TagManagerModel.CheckRename(t, name.Text, out target);
                    hint.Text = problem ?? string.Empty;
                    warning.Text = problem is null ? model.RenameWarning(t, target) : string.Empty;
                    ok.Content = problem is null ? model.RenameConfirmText(t, target) : Strings.Get(R.Settings_TagsConfirmRename);
                    ok.IsEnabled = problem is null;
                };
                ok.Click += async (_, _) =>
                {
                    flyout.Hide();
                    var to = target;
                    await run(progress => model.RenameAsync(t, to, progress));
                };
                ShowConfirm(name, hint, warning, ok);
                name.Loaded += (_, _) =>
                {
                    name.Focus(FocusState.Programmatic);
                    name.SelectAll();
                };
            }));
            actions.Children.Add(Action(Strings.Get(R.Settings_TagsDelete), () =>
            {
                var warning = Typo.Body(TagManagerModel.DeleteWarning(t), 12.5, Palette.InkSecondary);
                warning.TextWrapping = TextWrapping.Wrap;
                var ok = Ui.Ui.TextButton(Strings.Get(R.Settings_TagsConfirmDelete));
                ok.Click += async (_, _) =>
                {
                    flyout.Hide();
                    await run(progress => model.DeleteAsync(t, progress));
                };
                ShowConfirm(warning, ok);
            }));
        }

        void ShowConfirm(params UIElement[] parts)
        {
            confirm.Children.Clear();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var part in parts)
            {
                if (part is Button b)
                {
                    buttons.Children.Add(b);
                }
                else
                {
                    confirm.Children.Add(part);
                }
            }

            buttons.Children.Add(Ui.Ui.TextButton(Strings.Get(R.Common_Cancel), () =>
            {
                confirm.Visibility = Visibility.Collapsed;
                actions.Visibility = Visibility.Visible;
            }));
            confirm.Children.Add(buttons);
            actions.Visibility = Visibility.Collapsed;
            confirm.Visibility = Visibility.Visible;
        }

        panel.Children.Add(actions);
        panel.Children.Add(confirm);
        return panel;
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
