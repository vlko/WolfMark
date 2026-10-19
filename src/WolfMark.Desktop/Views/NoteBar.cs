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
    private readonly MainViewModel _vm;
    private readonly MarkdownTextEditor _editor;
    private readonly Dictionary<FormatCommand, Button> _toggles = [];

    public NoteBar(MainViewModel vm, MarkdownTextEditor editor)
    {
        _vm = vm;
        _editor = editor;
        var note = vm.Editor;

        var square = new Border { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
        var tagText = Typo.Caps(string.Empty, 14, Palette.Ink);
        tagText.TextTrimming = TextTrimming.CharacterEllipsis;
        tagText.VerticalAlignment = VerticalAlignment.Center;
        var tagContent = Ui.Columns((Ui.Auto, square), (Ui.Star, tagText), (Ui.Auto, Icons.Create(Icons.ChevronDown, 14, Palette.InkTertiary)));
        tagContent.ColumnSpacing = 8;
        var tagButton = Ui.FlatButton(tagContent);
        tagButton.Width = 180;
        tagButton.Height = Ui.ButtonHeight;
        tagButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        tagButton.VerticalContentAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(tagButton, Strings.Get(R.Menu_ChangeTag));
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Opening += async (_, _) => flyout.Content = await TagPickerAsync(flyout);
        tagButton.Flyout = flyout;
        void UpdateTag()
        {
            square.Background = new SolidColorBrush(Palette.ToColor(vm.Host.Tags.GetColor(note.Tag)));
            tagText.Text = (note.Tag ?? Strings.Get(R.Tag_None)).ToUpper(CultureInfo.CurrentCulture);
            tagButton.IsEnabled = !note.IsReadOnly && !note.IsNonStandard;
        }

        Ui.Bind(note, [nameof(NoteEditorViewModel.Tag), nameof(NoteEditorViewModel.IsReadOnly), nameof(NoteEditorViewModel.IsNonStandard)], UpdateTag);
        vm.Host.Tags.Changed += (_, _) => Dispatch(UpdateTag);

        var stateIcon = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var stateText = Typo.Caps(string.Empty, 12.5, Palette.InkTertiary);
        stateText.VerticalAlignment = VerticalAlignment.Center;
        var state = Ui.HStack(6, stateIcon, stateText);
        state.VerticalAlignment = VerticalAlignment.Center;
        Ui.Bind(note, nameof(NoteEditorViewModel.SaveState), () =>
        {
            var (icon, text) = note.SaveState switch
            {
                SaveState.Saved => (Icons.Check, Strings.Get(R.Save_Saved)),
                SaveState.Saving => ((string?)null, Strings.Get(R.Save_Saving)),
                SaveState.Pending => (Icons.Pencil, Strings.Get(R.Save_Pending)),
                SaveState.Error => (null, Strings.Get(R.Save_Error)),
                SaveState.ReadOnly => (Icons.Lock, Strings.Get(R.Save_ReadOnly)),
                _ => (Icons.Plus, Strings.Get(R.Save_New)),
            };
            stateIcon.Content = icon is null ? null : Icons.Create(icon, 12, Palette.InkTertiary);
            stateText.Text = text.ToUpper(CultureInfo.CurrentCulture);
            stateText.Foreground = note.SaveState == SaveState.Error ? Brushes.IndianRed : Palette.InkTertiary;
        });

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
        Ui.Bind(note, nameof(NoteEditorViewModel.IsReadOnly), () =>
        {
            format.Opacity = note.IsReadOnly ? 0.3 : 1;
            format.IsHitTestVisible = !note.IsReadOnly;
        });
        editor.ActiveFormatsChanged += (_, active) => ShowActive(active);
        Palette.Changed += (_, _) => ShowActive(_editor.ActiveFormats);

        var raw = Segment(Strings.Get(R.Mode_Raw));
        var formatted = Segment(Strings.Get(R.Mode_Formatted));
        raw.Click += (_, _) => note.Mode = EditorMode.Raw;
        formatted.Click += (_, _) => note.Mode = EditorMode.Formatted;
        void UpdateMode()
        {
            foreach (var (button, mode) in new[] { (raw, EditorMode.Raw), (formatted, EditorMode.Formatted) })
            {
                var on = note.Mode == mode;
                button.Background = on ? Palette.SubtleFillHover : Palette.SubtleFill;
                ((TextBlock)button.Content!).Foreground = on ? Palette.Ink : Palette.InkTertiary;
            }
        }

        Ui.Bind(note, nameof(NoteEditorViewModel.Mode), UpdateMode);
        Palette.Changed += (_, _) => UpdateMode();

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
        var left = Ui.HStack(12, tagButton, state);
        var mode = Ui.HStack(12, Ui.HStack(4, raw, formatted), add);
        mode.VerticalAlignment = VerticalAlignment.Center;
        format.VerticalAlignment = VerticalAlignment.Center;
        var toolbar = new ToolbarPanel();
        toolbar.Children.Add(left);
        toolbar.Children.Add(format);
        toolbar.Children.Add(mode);
        Content = toolbar;
    }

    private static TextBlock HeadingLabel(string text) => new() { Text = text, FontFamily = Typo.CondensedBold, FontSize = 16, Foreground = Palette.Ink };

    private Button Format(Control content, string tooltipKey, FormatCommand command)
    {
        var button = Ui.ToolButton(content, Strings.Get(tooltipKey));
        button.Click += (_, _) => _editor.Execute(command);
        _toggles[command] = button;
        return button;
    }

    /// <summary>Pressed look for the formats at the caret.</summary>
    private void ShowActive(IReadOnlySet<FormatCommand> active)
    {
        foreach (var (command, button) in _toggles)
        {
            button.Background = active.Contains(command) ? Palette.SubtleFillHover : Palette.Transparent;
        }
    }

    private static Button Segment(string text)
    {
        var button = Ui.FlatButton(Typo.Caps(text.ToUpper(CultureInfo.CurrentCulture), 14, Palette.InkTertiary));
        button.Width = 112;
        button.Height = Ui.ButtonHeight;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(button, Strings.Get(R.Mode_Tooltip));
        return button;
    }

    private static void Dispatch(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);

    private async Task<Control> TagPickerAsync(Flyout flyout)
    {
        var note = _vm.Editor;
        var host = _vm.Host;
        await host.DatabaseReady;
        var tags = await host.History.GetAllTagsAsync(host.ShutdownToken);
        var input = new TextBox { PlaceholderText = Strings.Get(R.Tag_NewPlaceholder), MinWidth = 240 };
        var error = Typo.Body(string.Empty, 11.5, Brushes.IndianRed);
        var list = new StackPanel { Spacing = 2 };

        async Task ApplyAsync(string? tag)
        {
            flyout.Hide();
            await note.ChangeTagAsync(tag);
        }

        Button Row(string? tag, string label, string? count)
        {
            var swatch = new Border
            {
                Width = 10,
                Height = 10,
                Background = tag is null ? Palette.Divider : new SolidColorBrush(Palette.ToColor(host.Tags.GetColor(tag))),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var grid = Ui.Columns((Ui.Auto, swatch), (Ui.Star, Typo.Body(label, 13.5)), (Ui.Auto, Typo.Body(count ?? string.Empty, 12, Palette.InkTertiary)));
            grid.ColumnSpacing = 8;
            var row = Ui.FlatButton(grid, Palette.Transparent);
            row.HorizontalAlignment = HorizontalAlignment.Stretch;
            row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            row.Click += (_, _) => _ = ApplyAsync(tag);
            return row;
        }

        void Render()
        {
            list.Children.Clear();
            var filter = (input.Text ?? string.Empty).Trim().TrimStart('#');
            if (filter.Length == 0)
            {
                list.Children.Add(Row(null, Strings.Get(R.Tag_None), null));
            }

            foreach (var t in tags.Where(t => filter.Length == 0 || t.Tag.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).Take(30))
            {
                list.Children.Add(Row(t.Tag, t.Tag, t.Count.ToString(CultureInfo.CurrentCulture)));
            }
        }

        input.TextChanged += (_, _) =>
        {
            var text = (input.Text ?? string.Empty).Trim();
            error.Text = text.Length > 0 && !TagName.TryNormalize(text, out _) ? Strings.Get(R.Tag_Invalid) : string.Empty;
            Render();
        };
        input.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter && TagName.TryNormalize(input.Text ?? string.Empty, out var tag))
            {
                e.Handled = true;
                await ApplyAsync(tag);
            }
        };
        Render();

        var panel = Ui.VStack(8, input, error, new ScrollViewer { Content = list, MaxHeight = 260 });
        if (note.Tag is { } current)
        {
            panel.Children.Add(new Border { Height = 1, Background = Palette.Divider, Margin = new Thickness(0, 4) });
            panel.Children.Add(Typo.Caps(Strings.Get(R.Tag_Color) + " · " + current));
            var swatches = new WrapPanel { MaxWidth = 7 * 32 };
            foreach (var color in TagPalette.Colors)
            {
                var swatch = new Button
                {
                    Width = 26,
                    Height = 26,
                    Margin = new Thickness(3),
                    Background = new SolidColorBrush(Palette.ToColor(color)),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(4),
                };
                swatch.Click += async (_, _) =>
                {
                    flyout.Hide();
                    await host.Tags.SetColorAsync(current, color, host.ShutdownToken);
                };
                swatches.Children.Add(swatch);
            }

            panel.Children.Add(swatches);
        }

        Dispatch(() => input.Focus());
        return panel;
    }
}
