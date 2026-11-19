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
        NotePanel = BuildToolbar();
        InfoBars = BuildInfoBars();
    }

    public FrameworkElement Heading { get; }

    /// <summary>Tag selection and formatting.</summary>
    public FrameworkElement NotePanel { get; }

    /// <summary>Closable notices (Claude Code import, external change, cleanupPeriodDays …) – shown at the bottom of the window.</summary>
    public FrameworkElement InfoBars { get; }

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
            var heading = NoteTexts.DayHeading(_vm.SelectedDay);
            setNumber(heading.Number);
            month.Text = heading.Month;
            weekday.Text = heading.Weekday;
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

        var tagButton = TagPicker.CreateButton(editor, _vm.Host, _brushes);

        var stateIcon = Typo.Icon(string.Empty, 12, Palette.InkTertiary);
        var stateText = Typo.Caps(string.Empty, 12.5, Palette.InkTertiary);
        stateText.Bind(editor, nameof(NoteEditorViewModel.SaveState), () =>
        {
            stateIcon.Glyph = editor.SaveState switch
            {
                SaveState.Saved => "\uE73E",
                SaveState.Saving => Glyphs.Sync,
                SaveState.Pending => Glyphs.Edit,
                SaveState.Error => Glyphs.Warning,
                SaveState.ReadOnly => Glyphs.Lock,
                _ => Glyphs.Add,
            };
            stateText.Text = NoteTexts.SaveState(editor.SaveState);
            stateIcon.Foreground = editor.SaveState == SaveState.Error ? new SolidColorBrush(Microsoft.UI.Colors.IndianRed) : Palette.InkTertiary;
        });

        var format = BuildFormatToggles(_editor);
        format.Bind(editor, nameof(NoteEditorViewModel.IsReadOnly), () =>
        {
            format.Opacity = editor.IsReadOnly ? 0.3 : 1;
            format.IsHitTestVisible = !editor.IsReadOnly;
        });
        var proofing = BuildProofingToggle();

        var segments = ModeSwitch(() => editor.Mode, m => editor.Mode = m, out var showMode);
        segments.Bind(editor, nameof(NoteEditorViewModel.Mode), showMode);

        var add = Ui.Ui.TagButton(Strings.Get(R.Toolbar_NewNoteShort), () => _ = _vm.NewNoteAsync(), Palette.Accent, new SolidColorBrush(Microsoft.UI.Colors.White), Glyphs.Add);
        ToolTipService.SetToolTip(add, Strings.Get(R.Toolbar_NewNote));
        var fullView = Ui.Ui.IconButton(Glyphs.Expand, Strings.Get(R.FullView_Enter), () => _vm.IsFullView = true, 13);

        // Three groups that wrap as a whole on narrow windows (ToolbarPanel): first the mode group drops to
        // a second row, then the formatting group – never a hidden button.
        var state = Ui.Ui.HStack(6, stateIcon, stateText);
        state.Width = 170;
        state.VerticalAlignment = VerticalAlignment.Center;
        var left = Ui.Ui.HStack(12, tagButton, state);
        var mode = Ui.Ui.HStack(12, proofing, segments, fullView, add);
        mode.VerticalAlignment = VerticalAlignment.Center;

        // Full view keeps only the formatting; the full view bar under the heading has the rest.
        left.Bind(_vm, nameof(MainViewModel.IsFullView), () =>
        {
            var shown = _vm.IsFullView ? Visibility.Collapsed : Visibility.Visible;
            left.Visibility = shown;
            mode.Visibility = shown;
        });

        var toolbar = new ToolbarPanel();
        toolbar.Children.Add(left);
        toolbar.Children.Add(format);
        toolbar.Children.Add(mode);
        return toolbar;
    }

    /// <summary>
    /// Format toggles: pressed when the format is active at the caret (instead of revealing the markdown
    /// delimiters inline); pressing an active one removes the format. Also in the external file window.
    /// </summary>
    internal static StackPanel BuildFormatToggles(MarkdownEditor editor)
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
                editor.Execute(command);
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
                button.IsChecked = editor.ActiveFormats.Contains(command);
            }
        }

        editor.ActiveFormatsChanged += (_, _) => Sync();
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
            squiggle.Opacity = on ? 1 : 0.25;
            abc.Opacity = on ? 1 : 0.45;
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

    /// <summary>
    /// [RAW|FORMÁT]: one switch of two joined segments on a shared track; the mode that is on is a block in the
    /// strong ink (ADR 0004) – two separate buttons a shade apart did not say which one was on.
    /// </summary>
    /// <param name="show">Shows the current mode; bind it to the mode property.</param>
    internal static Border ModeSwitch(Func<EditorMode> get, Action<EditorMode> set, out Action show)
    {
        var raw = Segment(Strings.Get(R.Mode_Raw));
        var formatted = Segment(Strings.Get(R.Mode_Formatted));
        void Show()
        {
            raw.IsChecked = get() == EditorMode.Raw;
            formatted.IsChecked = get() == EditorMode.Formatted;
        }

        // A ToggleButton flips itself on a click: a click on the segment that is on would switch it off and leave
        // both off, so the state is always put back from the mode.
        raw.Click += (_, _) =>
        {
            set(EditorMode.Raw);
            Show();
        };
        formatted.Click += (_, _) =>
        {
            set(EditorMode.Formatted);
            Show();
        };
        show = Show;

        var track = new Border
        {
            Background = Palette.SubtleFill,
            CornerRadius = Radius.Control,
            VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.Ui.HStack(0, raw, formatted),
        };
        ToolTipService.SetToolTip(track, Strings.Get(R.Mode_Tooltip));
        return track;

        static ToggleButton Segment(string text) => new ToggleButton
        {
            Content = Typo.Caps(text, 13, Palette.Ink),
            // Fixed width: RAW and FORMÁT are the same size in either state, nothing shifts when switching.
            Width = 92,
            Height = Metrics.ButtonHeight,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            CornerRadius = Radius.Control,
        }.StyleButton(Palette.Transparent, Palette.InkSecondary, checkedFill: Palette.TodayFill, checkedInk: Palette.TodayInk);
    }

    // ---------------------------------------------------------------- info bars

    private StackPanel BuildInfoBars()
    {
        var editor = _vm.Editor;

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

        var test = Info(Strings.Get(R.TestNotice_Title), Strings.Get(R.TestNotice_Message), InfoBarSeverity.Informational,
            Strings.Get(R.TestNotice_Action), () => _vm.IsTestNoticeOpen = false);
        test.Bind(_vm, nameof(MainViewModel.IsTestNoticeOpen), () => test.IsOpen = _vm.IsTestNoticeOpen);
        test.Closed += (_, _) => _vm.IsTestNoticeOpen = false;

        // A share from the phone (M12): which one, and a way to it. Further shares update the same notice.
        var share = Info(Strings.Get(R.Share_NoticeTitle), string.Empty, InfoBarSeverity.Informational,
            Strings.Get(R.Share_NoticeOpen), () => _ = _vm.OpenSharedNoteAsync());
        share.Bind(_vm, nameof(MainViewModel.ShareNotice), () =>
        {
            share.IsOpen = _vm.ShareNotice is not null;
            share.Message = _vm.ShareNotice?.Message ?? string.Empty;
        });
        share.Closed += (_, _) => _vm.ShareNotice = null;

        var error = Info(Strings.Get(R.Error_Title), string.Empty, InfoBarSeverity.Error, null, null);
        error.Bind(_vm, nameof(MainViewModel.ErrorNotice), () =>
        {
            error.IsOpen = _vm.ErrorNotice is not null;
            error.Message = _vm.ErrorNotice ?? string.Empty;
        });
        error.Closed += (_, _) => _vm.ErrorNotice = null;

        // The area exists only while a notice is open: closed InfoBars take no space, but spacing and
        // margins would leave an empty strip. Each open bar gets its own gap instead.
        var bars = new[] { error, share, external, deleted, conflict, nonStandard, cleanup, test };
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
