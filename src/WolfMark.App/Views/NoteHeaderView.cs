using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;
using WolfMark.Core.Tags;
using WolfMark.Editor;

namespace WolfMark.App.Views;

/// <summary>
/// Builds the day heading ("15 · OKTÓBER 2026 · STREDA") and the note panel (tag, save state, formatting,
/// Raw/Formatted, +, info bars) – SPEC §4.5, M4-09. The window places them separately: heading on top,
/// calendar between, note panel right above the editor.
/// </summary>
internal sealed partial class NoteHeaderView
{
    private readonly MainViewModel _vm;
    private readonly TagBrushes _brushes;
    private readonly MarkdownEditor _editor;

    public NoteHeaderView(MainViewModel vm, TagBrushes brushes, MarkdownEditor editor)
    {
        _vm = vm;
        _brushes = brushes;
        _editor = editor;
        Heading = BuildDayHeading();
        NotePanel = Ui.Ui.VStack(6, BuildToolbar(), BuildInfoBars());
    }

    public FrameworkElement Heading { get; }

    /// <summary>Tag selection, formatting and the note's info bars.</summary>
    public FrameworkElement NotePanel { get; }

    // ---------------------------------------------------------------- day heading

    /// <summary>"12" lead number in riso layers + "AUGUST 2026" thin caps + "WEDNESDAY" bold condensed.</summary>
    private Grid BuildDayHeading()
    {
        var number = Typo.RisoNumber(out var setNumber, 86);
        var month = new TextBlock { FontFamily = Typo.Lead, FontSize = 23, CharacterSpacing = 60, Foreground = Palette.InkSecondary };
        var weekday = new TextBlock { FontFamily = Typo.CondensedBold, FontSize = 32, CharacterSpacing = 20, Foreground = Palette.Ink, Margin = new Thickness(0, -6, 0, 0) };
        var dateText = Ui.Ui.VStack(0, month, weekday);
        dateText.VerticalAlignment = VerticalAlignment.Center;
        dateText.Margin = new Thickness(0, 6, 0, 0);

        number.Bind(_vm, nameof(MainViewModel.SelectedDay), () =>
        {
            var d = _vm.SelectedDay;
            var c = CultureInfo.CurrentCulture;
            setNumber(d.Day.ToString(c));
            month.Text = (c.DateTimeFormat.GetMonthName(d.Month) + " " + d.Year).ToUpper(c);
            weekday.Text = c.DateTimeFormat.GetDayName(d.DayOfWeek).ToUpper(c);
        });

        var grid = Ui.Ui.Columns((Ui.Ui.Auto, number), (Ui.Ui.Star, dateText));
        grid.ColumnSpacing = 14;
        return grid;
    }

    // ---------------------------------------------------------------- toolbar

    /// <summary>
    /// [tag ▾][save state] ……… [B I ☐ ‹› 🔗][RAW|FORMÁT][+ NOVÁ]. Fixed widths on the left so nothing
    /// shifts when the tag name or state text changes; the rest is anchored to the right edge.
    /// </summary>
    private ToolbarPanel BuildToolbar()
    {
        var editor = _vm.Editor;

        var tagSquare = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
        var tagText = Typo.Caps(string.Empty, 14, Palette.Ink);
        tagText.TextTrimming = TextTrimming.CharacterEllipsis;
        var tagContent = Ui.Ui.Columns((Ui.Ui.Auto, tagSquare), (Ui.Ui.Star, tagText), (Ui.Ui.Auto, Typo.Icon(Glyphs.ChevronDown, 10, Palette.InkTertiary)));
        tagContent.ColumnSpacing = 8;
        var tagButton = new Button
        {
            Content = tagContent,
            Width = 180,
            Height = Metrics.ButtonHeight,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = Radius.Control,
        };
        tagButton.StyleButton(Palette.SubtleFill, Palette.Ink);
        ToolTipService.SetToolTip(tagButton, Strings.Get(R.Menu_ChangeTag));
        var flyout = new Flyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft, Content = new ProgressRing { IsActive = true, Width = 24, Height = 24 } };
        flyout.Opening += async (_, _) => flyout.Content = await BuildTagPickerAsync(flyout);
        tagButton.Flyout = flyout;

        void UpdateTag()
        {
            tagSquare.Fill = _brushes.BrushOf(editor.Tag);
            tagText.Text = (editor.Tag ?? Strings.Get(R.Tag_None)).ToUpper(CultureInfo.CurrentCulture);
            tagButton.IsEnabled = !editor.IsReadOnly && !editor.IsNonStandard;
        }

        tagButton.Bind(editor, [nameof(NoteEditorViewModel.Tag), nameof(NoteEditorViewModel.IsReadOnly), nameof(NoteEditorViewModel.IsNonStandard)], UpdateTag);
        _brushes.Changed += (_, _) => UpdateTag();

        var stateIcon = Typo.Icon(string.Empty, 12, Palette.InkTertiary);
        var stateText = Typo.Caps(string.Empty, 12.5, Palette.InkTertiary);
        stateText.Bind(editor, nameof(NoteEditorViewModel.SaveState), () =>
        {
            (stateIcon.Glyph, stateText.Text) = editor.SaveState switch
            {
                SaveState.Saved => ("\uE73E", Strings.Get(R.Save_Saved)),
                SaveState.Saving => (Glyphs.Sync, Strings.Get(R.Save_Saving)),
                SaveState.Pending => (Glyphs.Edit, Strings.Get(R.Save_Pending)),
                SaveState.Error => (Glyphs.Warning, Strings.Get(R.Save_Error)),
                SaveState.ReadOnly => (Glyphs.Lock, Strings.Get(R.Save_ReadOnly)),
                _ => (Glyphs.Add, Strings.Get(R.Save_New)),
            };
            stateText.Text = stateText.Text.ToUpper(CultureInfo.CurrentCulture);
            stateIcon.Foreground = editor.SaveState == SaveState.Error ? new SolidColorBrush(Microsoft.UI.Colors.IndianRed) : Palette.InkTertiary;
        });

        var format = BuildFormatToggles();
        format.Bind(editor, nameof(NoteEditorViewModel.IsReadOnly), () =>
        {
            format.Opacity = editor.IsReadOnly ? 0.3 : 1;
            format.IsHitTestVisible = !editor.IsReadOnly;
        });
        var proofing = BuildProofingToggle();

        var raw = SegmentButton(Strings.Get(R.Mode_Raw));
        var formatted = SegmentButton(Strings.Get(R.Mode_Formatted));
        raw.Click += (_, _) => editor.Mode = EditorMode.Raw;
        formatted.Click += (_, _) => editor.Mode = EditorMode.Formatted;
        var segments = Ui.Ui.HStack(2, raw, formatted);
        ToolTipService.SetToolTip(segments, Strings.Get(R.Mode_Tooltip));
        segments.Bind(editor, nameof(NoteEditorViewModel.Mode), () =>
        {
            raw.IsChecked = editor.Mode == EditorMode.Raw;
            formatted.IsChecked = editor.Mode == EditorMode.Formatted;
        });

        var add = Ui.Ui.TagButton(Strings.Get(R.Toolbar_NewNoteShort), () => _ = _vm.NewNoteAsync(), Palette.Accent, new SolidColorBrush(Microsoft.UI.Colors.White), Glyphs.Add);
        ToolTipService.SetToolTip(add, Strings.Get(R.Toolbar_NewNote));

        // Three groups that wrap as a whole on narrow windows (ToolbarPanel): first the mode group drops to
        // a second row, then the formatting group – never a hidden button.
        var state = Ui.Ui.HStack(6, stateIcon, stateText);
        state.Width = 170;
        state.VerticalAlignment = VerticalAlignment.Center;
        var left = Ui.Ui.HStack(12, tagButton, state);
        var mode = Ui.Ui.HStack(12, proofing, segments, add);
        mode.VerticalAlignment = VerticalAlignment.Center;

        var toolbar = new ToolbarPanel();
        toolbar.Children.Add(left);
        toolbar.Children.Add(format);
        toolbar.Children.Add(mode);
        return toolbar;
    }

    /// <summary>
    /// Format toggles: pressed when the format is active at the caret (instead of revealing the markdown
    /// delimiters inline); pressing an active one removes the format.
    /// </summary>
    private StackPanel BuildFormatToggles()
    {
        var toggles = new List<(EditorCommand Command, ToggleButton Button)>();
        ToggleButton Toggle(EditorCommand command, UIElement content, string tooltip)
        {
            var button = new ToggleButton
            {
                Content = content,
                MinWidth = Metrics.ButtonHeight,
                Height = Metrics.ButtonHeight,
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = Radius.Control,
            };
            button.StyleButton(Palette.Transparent, Palette.Ink);
            ToolTipService.SetToolTip(button, tooltip);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
            button.Click += (_, _) =>
            {
                _editor.Execute(command);
                Sync();
            };
            toggles.Add((command, button));
            return button;
        }

        FontIcon Icon(EditorCommand command)
        {
            var icon = FormatIcons.Create(command);
            icon.Foreground = Palette.Ink;
            return icon;
        }

        var panel = Ui.Ui.HStack(
            0,
            Toggle(EditorCommand.Heading1, Icon(EditorCommand.Heading1), Strings.Get(R.Format_H1)),
            Toggle(EditorCommand.Heading2, Icon(EditorCommand.Heading2), Strings.Get(R.Format_H2)),
            Toggle(EditorCommand.Heading3, Icon(EditorCommand.Heading3), Strings.Get(R.Format_H3)),
            new Border { Width = 1, Height = 18, Background = Palette.Divider, Margin = new Thickness(6, 0, 6, 0) },
            Toggle(EditorCommand.Bold, Icon(EditorCommand.Bold), Strings.Get(R.Format_Bold)),
            Toggle(EditorCommand.Italic, Icon(EditorCommand.Italic), Strings.Get(R.Format_Italic)),
            Toggle(EditorCommand.Strikethrough, Icon(EditorCommand.Strikethrough), Strings.Get(R.Format_Strike)),
            Toggle(EditorCommand.Code, Icon(EditorCommand.Code), Strings.Get(R.Format_Code)),
            Toggle(EditorCommand.Link, Icon(EditorCommand.Link), Strings.Get(R.Format_Link)),
            Toggle(EditorCommand.Task, Icon(EditorCommand.Task), Strings.Get(R.Format_Task)));
        panel.VerticalAlignment = VerticalAlignment.Center;

        void Sync()
        {
            foreach (var (command, button) in toggles)
            {
                button.IsChecked = _editor.ActiveFormats.Contains(command);
            }
        }

        _editor.ActiveFormatsChanged += (_, _) => Sync();
        Sync();
        return panel;
    }

    /// <summary>Spell check on/off: "abc" over a red squiggle, remembered in the settings.</summary>
    private ToggleButton BuildProofingToggle()
    {
        var squiggle = new Microsoft.UI.Xaml.Shapes.Polyline
        {
            Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0xE5, 0x39, 0x35)),
            StrokeThickness = 1.4,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        for (var x = 0; x <= 20; x += 2)
        {
            squiggle.Points.Add(new Windows.Foundation.Point(x, x % 4 == 0 ? 3 : 0));
        }

        var abc = new TextBlock { Text = "abc", FontFamily = Typo.CondensedMedium, FontSize = 13, Foreground = Palette.Ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) };
        var settings = _vm.Host.Settings.Current;
        var button = new ToggleButton
        {
            Content = Ui.Ui.VStack(0, abc, squiggle),
            Width = 40,
            Height = Metrics.ButtonHeight,
            Padding = new Thickness(4, 0, 4, 0),
            CornerRadius = Radius.Control,
            IsChecked = settings.SpellCheckEnabled,
        };
        button.StyleButton(Palette.Transparent, Palette.Ink);
        var tip = Strings.Get(R.Proofing_Toggle);
        ToolTipService.SetToolTip(button, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);

        void Apply()
        {
            var on = button.IsChecked == true;
            _editor.IsSpellCheckEnabled = on;
            squiggle.Opacity = on ? 1 : 0.3;
        }

        button.Click += (_, _) =>
        {
            Apply();
            settings.SpellCheckEnabled = button.IsChecked == true;
            _ = _vm.Host.SaveSettingsAsync();
        };
        Apply();
        return button;
    }

    private static ToggleButton SegmentButton(string text) => new ToggleButton
    {
        Content = Typo.Caps(text, 13, Palette.Ink),
        // Fixed width: RAW and FORMÁT are the same size in either state, nothing shifts when switching.
        Width = 92,
        Height = Metrics.ButtonHeight,
        Padding = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        CornerRadius = Radius.Control,
    }.StyleButton(Palette.SubtleFill, Palette.InkSecondary, checkedFill: Palette.SubtleFillHover);

    private async Task<UIElement> BuildTagPickerAsync(Flyout flyout)
    {
        var editor = _vm.Editor;
        var host = _vm.Host;
        await host.DatabaseReady;
        var tags = await host.History.GetAllTagsAsync(host.ShutdownToken);

        var input = new TextBox { PlaceholderText = Strings.Get(R.Tag_NewPlaceholder), MinWidth = 240 };
        var error = Typo.Body(string.Empty, 11.5, new SolidColorBrush(Microsoft.UI.Colors.IndianRed));
        var list = new StackPanel { Spacing = 2 };

        async Task ApplyAsync(string? tag)
        {
            flyout.Hide();
            await editor.ChangeTagAsync(tag);
        }

        void Render()
        {
            list.Children.Clear();
            var filter = input.Text.Trim().TrimStart('#');
            if (filter.Length == 0)
            {
                list.Children.Add(TagRow(null, Strings.Get(R.Tag_None), null, () => _ = ApplyAsync(null)));
            }

            foreach (var t in tags.Where(t => filter.Length == 0 || t.Tag.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).Take(30))
            {
                var tag = t.Tag;
                list.Children.Add(TagRow(tag, tag, t.Count.ToString(CultureInfo.CurrentCulture), () => _ = ApplyAsync(tag)));
            }
        }

        input.TextChanged += (_, _) =>
        {
            error.Text = input.Text.Trim().Length > 0 && !TagName.TryNormalize(input.Text, out _) ? Strings.Get(R.Tag_Invalid) : string.Empty;
            Render();
        };
        input.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter && TagName.TryNormalize(input.Text, out var tag))
            {
                e.Handled = true;
                await ApplyAsync(tag);
            }
        };
        Render();

        var panel = Ui.Ui.VStack(
            8,
            input,
            error,
            new ScrollViewer { Content = list, MaxHeight = 260 });

        if (editor.Tag is { } current)
        {
            panel.Children.Add(new Border { Height = 1, Background = Palette.Divider, Margin = new Thickness(0, 4, 0, 4) });
            panel.Children.Add(Typo.Caps(Strings.Get(R.Tag_Color) + " · " + current));
            var swatches = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 7, ItemWidth = 32, ItemHeight = 32 };
            foreach (var color in TagPalette.Colors)
            {
                var c = color;
                var swatch = new Button
                {
                    Width = 28,
                    Height = 28,
                    Padding = new Thickness(0),
                    CornerRadius = Radius.Control,
                };
                swatch.StyleButton(new SolidColorBrush(Palette.ToColor(c)), new SolidColorBrush(Palette.ForegroundOn(c)), onColor: true);
                if (host.Tags.GetColor(current) == c)
                {
                    // Current colour: permanent ink frame.
                    swatch.BorderThickness = new Thickness(3);
                    swatch.BorderBrush = Palette.Ink;
                    swatch.Resources["ButtonBorderBrush"] = Palette.Ink;
                }

                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, c.ToHex());
                swatch.Click += async (_, _) =>
                {
                    flyout.Hide();
                    await Task.Run(() => host.Tags.SetColorAsync(current, c, CancellationToken.None));
                    _brushes.Refresh();
                };
                swatches.Children.Add(swatch);
            }

            panel.Children.Add(swatches);
            var labels = new CheckBox { Content = Strings.Get(R.Tag_ShowLabels), IsChecked = host.Tags.ShowsInCalendarLabels(current) };
            labels.Click += async (_, _) =>
            {
                // Read UI state on the UI thread; only the file write goes to the pool.
                var show = labels.IsChecked == true;
                await Task.Run(() => host.Tags.SetShowInCalendarLabelsAsync(current, show, CancellationToken.None));
            };
            panel.Children.Add(labels);
        }

        input.Loaded += (_, _) => input.Focus(FocusState.Programmatic);
        return panel;
    }

    private Button TagRow(string? tag, string label, string? count, Action onClick)
    {
        var row = Ui.Ui.Columns(
            (Ui.Ui.Auto, new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, Fill = _brushes.BrushOf(tag), Margin = new Thickness(0, 0, 10, 0) }),
            (Ui.Ui.Star, Typo.Body(label, 13.5)),
            (Ui.Ui.Auto, Typo.Body(count ?? string.Empty, 12, Palette.InkTertiary)));
        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 6, 8, 6),
        };
        button.StyleButton(Palette.Transparent, Palette.Ink);
        button.Click += (_, _) => onClick();
        return button;
    }

    // ---------------------------------------------------------------- info bars

    private StackPanel BuildInfoBars()
    {
        var editor = _vm.Editor;

        var claude = Info(Strings.Get(R.ReadOnly_ClaudeTitle), Strings.Get(R.ReadOnly_ClaudeMessage), InfoBarSeverity.Informational,
            Strings.Get(R.ReadOnly_CopyButton), () => _ = editor.CopyToOwnNoteAsync());
        claude.Bind(editor, nameof(NoteEditorViewModel.IsClaudeCode), () => claude.IsOpen = editor.IsClaudeCode);

        var external = Info(Strings.Get(R.External_Title), Strings.Get(R.External_Message), InfoBarSeverity.Warning,
            Strings.Get(R.External_Reload), () => _ = editor.ReloadFromDiskAsync());
        var keep = Ui.Ui.TextButton(Strings.Get(R.External_Keep));
        keep.Click += async (_, _) => await editor.KeepMineAsync();
        external.Content = keep;
        external.Bind(editor, nameof(NoteEditorViewModel.ExternalChangePending), () => external.IsOpen = editor.ExternalChangePending);

        var deleted = Info(Strings.Get(R.Deleted_Title), Strings.Get(R.Deleted_Message), InfoBarSeverity.Warning, null, null);
        deleted.Bind(editor, nameof(NoteEditorViewModel.IsDeletedExternally), () => deleted.IsOpen = editor.IsDeletedExternally);

        var conflict = Info(Strings.Get(R.Conflict_Title), string.Empty, InfoBarSeverity.Warning,
            Strings.Get(R.Conflict_OpenOriginal), () =>
            {
                if (editor.ConflictOriginal is { } original)
                {
                    _ = _vm.OpenNoteAsync(original.RelativePath);
                }
            });
        var discard = Ui.Ui.TextButton(Strings.Get(R.Conflict_Discard));
        discard.Click += async (_, _) => await editor.DeleteAsync();
        conflict.Content = discard;
        conflict.Bind(editor, nameof(NoteEditorViewModel.ConflictOriginal), () =>
        {
            conflict.IsOpen = editor.ConflictOriginal is not null;
            conflict.Message = editor.ConflictOriginal is { } o ? Strings.Format(R.Conflict_Message, o.FileName) : string.Empty;
        });

        var nonStandard = Info(Strings.Get(R.NonStandard_Title), Strings.Get(R.NonStandard_Message), InfoBarSeverity.Informational, null, null);
        nonStandard.Bind(editor, nameof(NoteEditorViewModel.IsNonStandard), () => nonStandard.IsOpen = editor.IsNonStandard);

        var cleanup = Info(Strings.Get(R.Claude_CleanupTitle), string.Empty, InfoBarSeverity.Warning,
            Strings.Get(R.Claude_CleanupFix), () => _ = _vm.FixClaudeCleanupAsync());
        cleanup.Bind(_vm, nameof(MainViewModel.CleanupWarning), () =>
        {
            cleanup.IsOpen = _vm.CleanupWarning is not null;
            cleanup.Message = _vm.CleanupWarning is { } w ? Strings.Format(R.Claude_CleanupMessage, w.Days) : string.Empty;
        });

        return Ui.Ui.VStack(4, claude, external, deleted, conflict, nonStandard, cleanup);
    }

    private static InfoBar Info(string title, string message, InfoBarSeverity severity, string? action, Action? onAction)
    {
        var bar = new InfoBar
        {
            Title = title,
            Message = message,
            Severity = severity,
            IsClosable = true,
            IsOpen = false,
        };
        if (action is not null && onAction is not null)
        {
            var button = Ui.Ui.TextButton(action);
            button.Click += (_, _) => onAction();
            bar.ActionButton = button;
        }

        return bar;
    }
}
