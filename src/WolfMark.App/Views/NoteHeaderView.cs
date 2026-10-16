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
    private readonly Action<EditorCommand> _execute;

    public NoteHeaderView(MainViewModel vm, TagBrushes brushes, Action<EditorCommand> execute)
    {
        _vm = vm;
        _brushes = brushes;
        _execute = execute;
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
    private Grid BuildToolbar()
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
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 5, 10, 5),
            CornerRadius = Radius.Control,
            Background = Palette.SubtleFill,
            BorderThickness = new Thickness(0),
        };
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

        var format = Ui.Ui.HStack(
            0,
            Ui.Ui.IconButton(Glyphs.Bold, "Bold (Ctrl+B)", () => _execute(EditorCommand.Bold), 14),
            Ui.Ui.IconButton(Glyphs.Italic, "Italic (Ctrl+I)", () => _execute(EditorCommand.Italic), 14),
            Ui.Ui.IconButton(Glyphs.CheckboxComposite, "Task (Ctrl+Shift+L)", () => _execute(EditorCommand.Task), 14),
            Ui.Ui.IconButton(Glyphs.Code, "Code", () => _execute(EditorCommand.Code), 14),
            Ui.Ui.IconButton(Glyphs.Link, "Link (Ctrl+K)", () => _execute(EditorCommand.Link), 14));
        format.Bind(editor, nameof(NoteEditorViewModel.IsReadOnly), () => format.Opacity = editor.IsReadOnly ? 0.3 : 1);
        format.Bind(editor, nameof(NoteEditorViewModel.IsReadOnly), () => format.IsHitTestVisible = !editor.IsReadOnly);

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

        var grid = Ui.Ui.Columns(
            (Ui.Ui.Px(180), tagButton),
            (Ui.Ui.Px(170), Ui.Ui.HStack(6, stateIcon, stateText)),
            (Ui.Ui.Star, null),
            (Ui.Ui.Auto, format),
            (Ui.Ui.Auto, segments),
            (Ui.Ui.Auto, add));
        grid.ColumnSpacing = 12;
        return grid;
    }

    private static ToggleButton SegmentButton(string text) => new()
    {
        Content = Typo.Caps(text, 13, Palette.Ink),
        Padding = new Thickness(10, 3, 10, 3),
        MinHeight = 30,
        CornerRadius = Radius.Control,
        BorderThickness = new Thickness(0),
    };

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
                    Background = new SolidColorBrush(Palette.ToColor(c)),
                    BorderThickness = new Thickness(host.Tags.GetColor(current) == c ? 3 : 0),
                    BorderBrush = Palette.Ink,
                };
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
            Background = Palette.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 6, 8, 6),
        };
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
        var keep = new Button { Content = Strings.Get(R.External_Keep) };
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
        var discard = new Button { Content = Strings.Get(R.Conflict_Discard) };
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
            var button = new Button { Content = action };
            button.Click += (_, _) => onAction();
            bar.ActionButton = button;
        }

        return bar;
    }
}
