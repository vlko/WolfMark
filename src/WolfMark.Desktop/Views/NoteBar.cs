using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Settings;
using WolfMark.Core.Tags;
using WolfMark.Desktop.Editor;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// [■ tag ▾][✓ saved] ……… [H1 H2 H3 | B I S {} 🔗 ☑][RAW][FORMATTED][+ NEW]: the note's toolbar (the Windows
/// NoteHeaderView toolbar). Every button is <see cref="Ui.ButtonHeight"/> high; the formats at the caret show
/// as pressed. The tag picker lists the used tags, takes a new one and recolours the current one.
/// </summary>
internal sealed class NoteBar : UserControl
{
    public NoteBar(MainViewModel vm, MarkdownTextEditor editor)
    {
        var note = vm.Editor;

        var tagButton = TagPicker.CreateButton(note, vm.Host);

        var stateIcon = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var stateText = Typo.Caps(string.Empty, 12.5, Palette.InkTertiary);
        stateText.VerticalAlignment = VerticalAlignment.Center;
        var state = Ui.HStack(6, stateIcon, stateText);
        state.VerticalAlignment = VerticalAlignment.Center;
        Ui.Bind(note, nameof(NoteEditorViewModel.SaveState), () =>
        {
            var icon = note.SaveState switch
            {
                SaveState.Saved => Icons.Check,
                SaveState.Pending => Icons.Pencil,
                SaveState.ReadOnly => Icons.Lock,
                SaveState.Saving or SaveState.Error => null,
                _ => Icons.Plus,
            };
            stateIcon.Content = icon is null ? null : Icons.Create(icon, 12, Palette.InkTertiary);
            stateText.Text = NoteTexts.SaveState(note.SaveState);
            stateText.Foreground = note.SaveState == SaveState.Error ? Brushes.IndianRed : Palette.InkTertiary;
        });

        var format = FormatBar.Create(editor);
        Ui.Bind(note, nameof(NoteEditorViewModel.IsReadOnly), () =>
        {
            format.Opacity = note.IsReadOnly ? 0.3 : 1;
            format.IsHitTestVisible = !note.IsReadOnly;
        });
        var segments = FormatBar.ModeSegments(note, nameof(NoteEditorViewModel.Mode), () => note.Mode, m => note.Mode = m);

        var addLabel = Typo.Caps(Strings.Get(R.Toolbar_NewNoteShort).ToUpper(CultureInfo.CurrentCulture), 14, Brushes.White);
        var add = Ui.FlatButton(Ui.HStack(6, Icons.Create(Icons.Plus, 14, Brushes.White), addLabel), Palette.Accent);
        add.Height = Ui.ButtonHeight;
        add.MinWidth = 80;
        add.VerticalContentAlignment = VerticalAlignment.Center;
        add.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(add, Strings.Get(R.Toolbar_NewNote));
        add.Click += async (_, _) => await vm.NewNoteAsync();

        // Three groups that wrap as a whole on narrow windows (ToolbarPanel): first the mode group drops to a
        // second row, then the formatting group – never a hidden button.
        state.Width = 170;
        var fullView = Ui.IconButton(Icons.Create(Icons.Expand, 14, Palette.InkTertiary), Strings.Get(OperatingSystem.IsMacOS() ? R.FullView_EnterMac : R.FullView_Enter), () => vm.IsFullView = true);
        var left = Ui.HStack(12, tagButton, state);
        var mode = Ui.HStack(12, segments, fullView, add);
        mode.VerticalAlignment = VerticalAlignment.Center;

        // Full view keeps only the formatting; the full view bar under the heading has the rest.
        Ui.Bind(vm, nameof(MainViewModel.IsFullView), () => left.IsVisible = mode.IsVisible = !vm.IsFullView);
        var toolbar = new ToolbarPanel();
        toolbar.Children.Add(left);
        toolbar.Children.Add(format);
        toolbar.Children.Add(mode);
        Content = toolbar;
    }
}

/// <summary>The formatting buttons and the Raw/Formatted switch – the note bar's, and the external file window's.</summary>
internal static class FormatBar
{
    /// <summary>[H1 H2 H3 | B I S {} 🔗 ☑] for <paramref name="editor"/>; the formats at the caret show as pressed.</summary>
    public static StackPanel Create(MarkdownTextEditor editor)
    {
        var toggles = new Dictionary<FormatCommand, Button>();
        Button Format(Control content, string tooltipKey, FormatCommand command)
        {
            var button = Ui.ToolButton(content, Strings.Get(tooltipKey));
            button.Click += (_, _) => editor.Execute(command);
            toggles[command] = button;
            return button;
        }

        void ShowActive(IReadOnlySet<FormatCommand> active)
        {
            foreach (var (command, button) in toggles)
            {
                button.Background = active.Contains(command) ? Palette.SubtleFillHover : Palette.Transparent;
            }
        }

        var headings = Ui.HStack(2,
            Format(HeadingLabel("H1"), R.Format_H1, FormatCommand.Heading1),
            Format(HeadingLabel("H2"), R.Format_H2, FormatCommand.Heading2),
            Format(HeadingLabel("H3"), R.Format_H3, FormatCommand.Heading3));
        var inline = Ui.HStack(2,
            Format(Icons.Create(Icons.Bold, 18), R.Format_Bold, FormatCommand.Bold),
            Format(Icons.Create(Icons.Italic, 18), R.Format_Italic, FormatCommand.Italic),
            Format(Icons.Create(Icons.Strikethrough, 16), R.Format_Strike, FormatCommand.Strikethrough),
            Format(Icons.Create(Icons.Code, 17), R.Format_Code, FormatCommand.Code),
            Format(Icons.Create(Icons.Link, 18), R.Format_Link, FormatCommand.Link),
            Format(Icons.Create(Icons.Task, 17), R.Format_Task, FormatCommand.Task));
        var separator = new Border { Width = 1, Height = 18, Background = Palette.Divider, Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
        var format = Ui.HStack(0, headings, separator, inline);
        format.VerticalAlignment = VerticalAlignment.Center;
        editor.ActiveFormatsChanged += (_, active) => ShowActive(active);
        Palette.Changed += (_, _) => ShowActive(editor.ActiveFormats);
        return format;
    }

    /// <summary>
    /// [RAW|FORMATTED] showing and setting the mode of <paramref name="source"/>: one switch of two joined segments
    /// on a shared track; the mode that is on is a block in the strong ink (ADR 0004).
    /// </summary>
    public static Border ModeSegments(System.ComponentModel.INotifyPropertyChanged source, string property, Func<EditorMode> get, Action<EditorMode> set)
    {
        var raw = Segment(Strings.Get(R.Mode_Raw), () => set(EditorMode.Raw));
        var formatted = Segment(Strings.Get(R.Mode_Formatted), () => set(EditorMode.Formatted));
        void UpdateMode()
        {
            foreach (var (segment, mode) in new[] { (raw, EditorMode.Raw), (formatted, EditorMode.Formatted) })
            {
                var on = get() == mode;
                segment.Block.Background = on ? Palette.TodayFill : Brushes.Transparent;
                segment.Label.Foreground = on ? Palette.TodayInk : Palette.InkSecondary;
            }
        }

        Ui.Bind(source, property, UpdateMode);
        var track = new Border
        {
            Background = Palette.SubtleFill,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Ui.HStack(0, raw.Cell, formatted.Cell),
        };
        Ui.OnPaletteChanged(track, UpdateMode);
        return track;
    }

    private static TextBlock HeadingLabel(string text) => new() { Text = text, FontFamily = Typo.CondensedBold, FontSize = 16, Foreground = Palette.Ink };

    /// <summary>
    /// One segment of the mode switch: the block of the mode that is on, a transparent button over it and the label
    /// on top – the theme's hover fill of a button would otherwise replace the block.
    /// </summary>
    private static (Panel Cell, Border Block, TextBlock Label) Segment(string text, Action onClick)
    {
        var block = new Border { CornerRadius = new CornerRadius(3) };
        var label = Typo.Caps(text.ToUpper(CultureInfo.CurrentCulture), 14, Palette.InkSecondary);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.IsHitTestVisible = false;
        var button = Ui.FlatButton(string.Empty, Brushes.Transparent);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.VerticalAlignment = VerticalAlignment.Stretch;
        button[Avalonia.Automation.AutomationProperties.NameProperty] = text;
        button.Click += (_, _) => onClick();
        ToolTip.SetTip(button, Strings.Get(R.Mode_Tooltip));
        // Fixed width: RAW and FORMATTED are the same size in either state, nothing shifts when switching.
        var cell = new Panel { Width = 112, Height = Ui.ButtonHeight };
        cell.Children.Add(block);
        cell.Children.Add(button);
        cell.Children.Add(label);
        return (cell, block, label);
    }
}
