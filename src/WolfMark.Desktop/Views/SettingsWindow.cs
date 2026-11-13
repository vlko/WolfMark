using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;
using WolfMark.Core.Tags;
using WolfMark.Index.Queries;
using WolfMark.Desktop.Infrastructure;
using WolfMark.Desktop.Theming;
using WolfMark.Storage;
using WolfMark.Storage.Settings;
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
        var portableData = host.PortableDataDirectory;
        var isPortable = portableData is not null;
        Title = isPortable ? $"{Strings.Get(R.Settings_Title)} · {Strings.Get(R.Settings_PortableTitle)}" : Strings.Get(R.Settings_Title);
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

            s.RootDirectory = host.Settings.ToStoredRoot(newRoot);
            await host.SaveSettingsAsync();
            rootText.Text = newRoot;
            _restartNeeded = true;
            restartRow.IsVisible = true;
        }

        move.Click += async (_, _) => await ApplyRootAsync(moveNotes: true);
        switchTo.Click += async (_, _) => await ApplyRootAsync(moveNotes: false);
        cancelMove.Click += (_, _) => movePanel.IsVisible = false;

        // Portable mode (ADR 0009): copy everything next to the app, or back; then a restart.
        var portableStatus = Typo.Body(isPortable ? Strings.Format(R.Settings_PortableOn, portableData!) : Strings.Get(R.Settings_PortableOff), 13, Palette.InkSecondary);
        portableStatus.TextWrapping = TextWrapping.Wrap;
        var portableButton = Ui.FlatButton(Strings.Get(isPortable ? R.Settings_PortableDisable : R.Settings_PortableEnable));
        portableButton.IsEnabled = false;
        var portableReason = Typo.Body(string.Empty, 12.5, Palette.InkTertiary);
        portableReason.TextWrapping = TextWrapping.Wrap;
        portableReason.IsVisible = false;
        if (!LaunchOptions.Current.IsDemo)
        {
            _ = Task.Run(() => isPortable ? PortableUnavailableReason.None : PortableMode.CheckAvailability(PortableMode.AppDirectory)).ContinueWith(
                t =>
                {
                    var reason = t.IsCompletedSuccessfully ? t.Result : PortableUnavailableReason.NotWritable;
                    portableButton.IsEnabled = reason == PortableUnavailableReason.None;
                    portableReason.Text = reason switch
                    {
                        PortableUnavailableReason.NotWritable => Strings.Format(R.Settings_PortableNotWritable, PortableMode.AppDirectory),
                        PortableUnavailableReason.AppBundle => Strings.Get(R.Settings_PortableAppBundle),
                        _ => string.Empty,
                    };
                    portableReason.IsVisible = reason != PortableUnavailableReason.None;
                },
                TaskScheduler.FromCurrentSynchronizationContext());
        }

        var portableWarning = Typo.Body(string.Empty, 12.5, Palette.InkSecondary);
        portableWarning.TextWrapping = TextWrapping.Wrap;
        var portableCopy = Ui.FlatButton(Strings.Get(isPortable ? R.Settings_PortableCopyBack : R.Settings_PortableCopy));
        var portableMove = Ui.FlatButton(Strings.Get(R.Settings_PortableMove));
        portableMove.IsVisible = !isPortable;
        var portableCancel = Ui.FlatButton(Strings.Get(R.Common_Cancel), Palette.Transparent);
        var portableProgress = Typo.Body(string.Empty, 12.5, Palette.InkSecondary);
        portableProgress.TextWrapping = TextWrapping.Wrap;
        var portablePanel = Ui.VStack(6, portableWarning, Ui.HStack(6, portableCopy, portableMove, portableCancel), portableProgress);
        portablePanel.IsVisible = false;
        portableButton.Click += async (_, _) =>
        {
            portableWarning.Text = isPortable
                ? Strings.Format(R.Settings_PortableDisableWarning, await Task.Run(() => PortableMigrator.StandardNotesRoot(LocalPaths.Standard)), portableData!)
                : Strings.Format(R.Settings_PortableEnableWarning, PortableMode.DefaultDataDirectory(PortableMode.AppDirectory));
            portableProgress.Text = string.Empty;
            portablePanel.IsVisible = true;
        };
        portableCancel.Click += (_, _) => portablePanel.IsVisible = false;

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
                if (!isPortable && DefaultHandler.IsSupported)
                {
                    await Task.Run(() =>
                    {
                        try
                        {
                            if (DefaultHandler.IsEnabled)
                            {
                                DefaultHandler.Set(false);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // best effort – the mode switch already happened
                        }
                    });
                }

                if (result.CleanupError is { } cleanup)
                {
                    portableProgress.Text = Strings.Format(R.Settings_PortableCleanupFailed, cleanup.Message);
                    _restartNeeded = true;
                    restartRow.IsVisible = true;
                    return;
                }

                Program.RestartRequested = true;
                Close();
                (Owner as Window)?.Close();
            }
            catch (Exception ex)
            {
                // Watcher and Claude Code sync are stopped: only a restart brings them back.
                portableProgress.Text = Strings.Format(R.Settings_PortableFailed, ex.Message);
                portableCancel.IsEnabled = true;
                _restartNeeded = true;
                restartRow.IsVisible = true;
            }
        }

        portableCopy.Click += async (_, _) => await SwitchPortableAsync(moveNotes: false);
        portableMove.Click += async (_, _) => await SwitchPortableAsync(moveNotes: true);

        var theme = Combo([Strings.Get(R.Settings_ThemeSystem), Strings.Get(R.Settings_ThemeLight), Strings.Get(R.Settings_ThemeDark)], (int)s.Theme);
        var languages = new[] { (string?)null, "sk-SK", "en-US" };
        var language = Combo([Strings.Get(R.Settings_LanguageSystem), "Slovenčina", "English"], Math.Max(0, Array.IndexOf(languages, s.Language)));
        var closeToTray = new ToggleSwitch { IsChecked = s.CloseToTray, Content = Strings.Get(R.Settings_CloseToTray) };

        // .md files: the system's default handler is the truth – read off the UI thread (xdg-mime runs a process).
        bool? associationWas = null;
        var fileAssociation = new ToggleSwitch { Content = Strings.Get(R.Settings_FileAssociation), IsEnabled = false };
        var associationHint = Typo.Body(Strings.Get(R.Settings_FileAssociationHintDesktop), 12.5, Palette.InkTertiary);
        associationHint.TextWrapping = TextWrapping.Wrap;
        // Portable mode writes nothing to this computer's system settings.
        var associationPortable = Typo.Body(Strings.Get(R.Settings_PortableUnavailable), 12.5, Palette.InkTertiary);
        associationPortable.TextWrapping = TextWrapping.Wrap;
        associationPortable.IsVisible = isPortable;
        associationHint.IsVisible = !isPortable;
        if (DefaultHandler.IsSupported && !isPortable)
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
                associationHint,
                associationPortable,
                Section(Strings.Get(R.Settings_Portable)),
                portableStatus,
                Ui.HStack(8, portableButton),
                portableReason,
                portablePanel)),
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
            (Icons.Tag, Strings.Get(R.Settings_Tags), TagsPage(vm, () => save.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)))),
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

    /// <summary>
    /// Tags (M8-05), as src/WolfMark.App/Views/SettingsDialog.cs: the list of tags, a field to add one, and per tag a
    /// flyout (the arrow) with its colour, calendar labels, "show notes", rename (or merge) and removal. Everything
    /// applies at once. The checks, texts and the work are in <see cref="TagManagerModel"/>.
    /// </summary>
    /// <param name="showNotes">Closes Settings as Save, so the main window shows the filtered history.</param>
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

        var newName = new TextBox { PlaceholderText = Strings.Get(R.Settings_TagsNewPlaceholder), MinWidth = 220 };
        var add = Ui.FlatButton(Strings.Get(R.Settings_TagsAdd));
        add.IsEnabled = false;
        var newHint = Typo.Body(string.Empty, 12.5, Brushes.IndianRed);
        var newTag = string.Empty;
        newName.TextChanged += (_, _) =>
        {
            var problem = model.CheckNew(newName.Text ?? string.Empty, out newTag);
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
            if (e.Key == Avalonia.Input.Key.Enter)
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

        return Ui.VStack(10, intro, Ui.HStack(8, newName, add), newHint, list, status, mergeNote);
    }

    /// <summary>One tag: colour square, name, count and the arrow that opens its flyout.</summary>
    private static Grid TagRow(TagManagerModel model, TagUsage t, Func<Func<IProgress<int>, Task<string>>, Task> run, Action showNotes)
    {
        var square = new Border
        {
            Width = 12,
            Height = 12,
            Background = new SolidColorBrush(Palette.ToColor(model.ColorOf(t))),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var arrow = Ui.IconButton(Icons.Create(Icons.ChevronDown, 16, Palette.InkSecondary), Strings.Get(R.Settings_TagsMenu), () => { });
        arrow.Flyout = flyout;
        flyout.Opening += (_, _) => flyout.Content = TagFlyout(model, t, flyout, square, run, showNotes);
        var name = Typo.Body("#" + t.Tag, 13.5);
        name.VerticalAlignment = VerticalAlignment.Center;
        var count = Typo.Body(model.CountText(t), 12.5, Palette.InkTertiary);
        count.VerticalAlignment = VerticalAlignment.Center;
        count.Margin = new Thickness(0, 0, 8, 0);
        var row = Ui.Columns((Ui.Auto, square), (Ui.Star, name), (Ui.Auto, count), (Ui.Auto, arrow));
        row.Margin = new Thickness(4, 0, 0, 0);
        return row;
    }

    /// <summary>The tag's flyout: colour, calendar labels, show notes, rename, remove (the last two confirm inside).</summary>
    private static StackPanel TagFlyout(TagManagerModel model, TagUsage t, Flyout flyout, Border square, Func<Func<IProgress<int>, Task<string>>, Task> run, Action showNotes)
    {
        var panel = new StackPanel { Spacing = 8, Width = 280 };
        panel.Children.Add(Typo.Caps(Strings.Get(R.Tag_Color) + " · #" + t.Tag));
        var current = model.ColorOf(t);
        var swatches = new WrapPanel { MaxWidth = 7 * 34 };
        foreach (var color in TagPalette.Colors)
        {
            var c = color;
            var swatch = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(3),
                Background = new SolidColorBrush(Palette.ToColor(c)),
                BorderThickness = new Thickness(c == current ? 3 : 0),
                BorderBrush = Palette.Ink,
                CornerRadius = new CornerRadius(4),
            };
            swatch.Click += async (_, _) =>
            {
                flyout.Hide();
                square.Background = new SolidColorBrush(Palette.ToColor(c));
                await model.SetColorAsync(t, c);
            };
            swatches.Children.Add(swatch);
        }

        panel.Children.Add(swatches);
        var labels = new CheckBox { Content = Strings.Get(R.Tag_ShowLabels), IsChecked = model.ShowsLabels(t) };
        labels.Click += async (_, _) => await model.SetLabelsAsync(t, labels.IsChecked == true);
        panel.Children.Add(labels);
        panel.Children.Add(new Border { Height = 1, Background = Palette.Divider });

        var actions = new StackPanel { Spacing = 4 };
        var confirm = new StackPanel { Spacing = 8, IsVisible = false };
        Button Action(string text, Action onClick)
        {
            var b = Ui.FlatButton(text);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Click += (_, _) => onClick();
            return b;
        }

        void ShowConfirm(Control[] texts, Button ok)
        {
            confirm.Children.Clear();
            foreach (var text in texts)
            {
                confirm.Children.Add(text);
            }

            var cancel = Ui.FlatButton(Strings.Get(R.Common_Cancel), Palette.Transparent);
            cancel.Click += (_, _) =>
            {
                confirm.IsVisible = false;
                actions.IsVisible = true;
            };
            confirm.Children.Add(Ui.HStack(8, ok, cancel));
            actions.IsVisible = false;
            confirm.IsVisible = true;
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
                var hint = Typo.Body(string.Empty, 12.5, Brushes.IndianRed);
                hint.TextWrapping = TextWrapping.Wrap;
                var warning = Typo.Body(string.Empty, 12.5, Palette.InkSecondary);
                warning.TextWrapping = TextWrapping.Wrap;
                var ok = Ui.FlatButton(Strings.Get(R.Settings_TagsConfirmRename));
                ok.IsEnabled = false;
                var target = string.Empty;
                name.TextChanged += (_, _) =>
                {
                    var problem = TagManagerModel.CheckRename(t, name.Text ?? string.Empty, out target);
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
                ShowConfirm([name, hint, warning], ok);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    name.Focus();
                    name.SelectAll();
                });
            }));
            actions.Children.Add(Action(Strings.Get(R.Settings_TagsDelete), () =>
            {
                var warning = Typo.Body(TagManagerModel.DeleteWarning(t), 12.5, Palette.InkSecondary);
                warning.TextWrapping = TextWrapping.Wrap;
                var ok = Ui.FlatButton(Strings.Get(R.Settings_TagsConfirmDelete));
                ok.Click += async (_, _) =>
                {
                    flyout.Hide();
                    await run(progress => model.DeleteAsync(t, progress));
                };
                ShowConfirm([warning], ok);
            }));
        }

        panel.Children.Add(actions);
        panel.Children.Add(confirm);
        return panel;
    }

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
