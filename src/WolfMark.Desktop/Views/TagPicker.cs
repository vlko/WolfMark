using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WolfMark.App;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Tags;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// The tag button of a note ([■ TAG ▾]) and its flyout: no tag, the used tags (filtered as you type), a new tag with
/// its colour. Used by the note bar and by the quick note window (as src/WolfMark.App/Views/TagPicker.cs).
/// </summary>
internal static class TagPicker
{
    public static Button CreateButton(NoteEditorViewModel note, AppHost host)
    {
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
        flyout.Opening += async (_, _) => flyout.Content = await PickerAsync(flyout, note, host);
        tagButton.Flyout = flyout;
        void UpdateTag()
        {
            square.Background = new SolidColorBrush(Palette.ToColor(host.Tags.GetColor(note.Tag)));
            tagText.Text = (note.Tag ?? Strings.Get(R.Tag_None)).ToUpper(CultureInfo.CurrentCulture);
            tagButton.IsEnabled = !note.IsReadOnly && !note.IsNonStandard;
        }

        Ui.Bind(note, [nameof(NoteEditorViewModel.Tag), nameof(NoteEditorViewModel.IsReadOnly), nameof(NoteEditorViewModel.IsNonStandard)], UpdateTag);
        host.Tags.Changed += (_, _) => Dispatch(UpdateTag);
        return tagButton;
    }

    private static void Dispatch(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);

    private static async Task<Control> PickerAsync(Flyout flyout, NoteEditorViewModel note, AppHost host)
    {
        await host.DatabaseReady;
        var tags = await host.GetTagListAsync(host.ShutdownToken);
        var input = new TextBox { PlaceholderText = Strings.Get(R.Tag_NewPlaceholder), MinWidth = 240 };
        var error = Typo.Body(string.Empty, 11.5, Brushes.IndianRed);
        var list = new StackPanel { Spacing = 2 };

        // A tag that does not exist yet: its colour and calendar labels are chosen here, before it is created.
        // Existing tags are edited in Settings → Tags.
        TagColor? newColor = null;
        var newLabels = new CheckBox { Content = Strings.Get(R.Tag_ShowLabels) };
        var newSwatches = new WrapPanel { MaxWidth = 7 * 32 };
        var newTitle = Typo.Caps(string.Empty);
        var newSection = Ui.VStack(8, new Border { Height = 1, Background = Palette.Divider, Margin = new Thickness(0, 4) }, newTitle, newSwatches, newLabels);
        newSection.IsVisible = false;

        bool IsNew(string tag) => !tags.Any(t => TagName.Comparer.Equals(t.Tag, tag));

        void RenderSwatches(string tag)
        {
            newSwatches.Children.Clear();
            var selected = newColor ?? TagPalette.ForTag(tag);
            foreach (var color in TagPalette.Colors)
            {
                var c = color;
                var swatch = new Button
                {
                    Width = 26,
                    Height = 26,
                    Margin = new Thickness(3),
                    Background = new SolidColorBrush(Palette.ToColor(c)),
                    BorderThickness = new Thickness(c == selected ? 3 : 0),
                    BorderBrush = Palette.Ink,
                    CornerRadius = new CornerRadius(4),
                };
                swatch.Click += (_, _) =>
                {
                    newColor = c; // kept until Enter creates the tag
                    RenderSwatches(tag);
                };
                newSwatches.Children.Add(swatch);
            }
        }

        async Task ApplyAsync(string? tag)
        {
            flyout.Hide();
            if (tag is not null && IsNew(tag))
            {
                if (newColor is { } c)
                {
                    await host.Tags.SetColorAsync(tag, c, host.ShutdownToken);
                }

                if (newLabels.IsChecked == true)
                {
                    await host.Tags.SetShowInCalendarLabelsAsync(tag, true, host.ShutdownToken);
                }
            }

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

            if (TagName.TryNormalize(filter, out var typed) && IsNew(typed))
            {
                newTitle.Text = Strings.Get(R.Tag_Color) + " · #" + typed;
                RenderSwatches(typed);
                newSection.IsVisible = true;
            }
            else
            {
                newSection.IsVisible = false;
            }
        }

        input.TextChanged += (_, _) =>
        {
            var text = (input.Text ?? string.Empty).Trim();
            error.Text = text.Length > 0 && !TagName.TryNormalize(text, out _) ? Strings.Get(R.Tag_Invalid) : string.Empty;
            newColor = null; // another name: back to its palette colour
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

        var panel = Ui.VStack(8, input, error, new ScrollViewer { Content = list, MaxHeight = 260 }, newSection);
        Dispatch(() => input.Focus());
        return panel;
    }
}
