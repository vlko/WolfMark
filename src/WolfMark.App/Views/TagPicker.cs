using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;
using WolfMark.Core.Tags;

namespace WolfMark.App.Views;

/// <summary>
/// The tag button of a note ([■ TAG ▾]) and its flyout: no tag, the existing tags (filtered as you type), a new tag
/// with its colour. Used by the main window's note panel and by the quick note window.
/// </summary>
internal static class TagPicker
{
    public static Button CreateButton(NoteEditorViewModel editor, AppHost host, TagBrushes brushes)
    {
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
        flyout.Opening += async (_, _) => flyout.Content = await BuildPickerAsync(flyout, editor, host, brushes);
        tagButton.Flyout = flyout;

        void UpdateTag()
        {
            tagSquare.Fill = brushes.BrushOf(editor.Tag);
            tagText.Text = (editor.Tag ?? Strings.Get(R.Tag_None)).ToUpper(CultureInfo.CurrentCulture);
            tagButton.IsEnabled = !editor.IsReadOnly && !editor.IsNonStandard;
        }

        tagButton.Bind(editor, [nameof(NoteEditorViewModel.Tag), nameof(NoteEditorViewModel.IsReadOnly), nameof(NoteEditorViewModel.IsNonStandard)], UpdateTag);
        brushes.Changed += (_, _) => UpdateTag();

        return tagButton;
    }

    private static async Task<UIElement> BuildPickerAsync(Flyout flyout, NoteEditorViewModel editor, AppHost host, TagBrushes brushes)
    {
        await host.DatabaseReady;
        var tags = await host.GetTagListAsync(host.ShutdownToken);

        var input = new TextBox { PlaceholderText = Strings.Get(R.Tag_NewPlaceholder), MinWidth = 240 };
        var error = Typo.Body(string.Empty, 11.5, new SolidColorBrush(Microsoft.UI.Colors.IndianRed));
        var list = new StackPanel { Spacing = 2 };

        // A tag that does not exist yet: its colour and calendar labels are chosen here, before it is created.
        // Existing tags are edited in Settings → Tags.
        var newSection = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        TagColor? newColor = null;
        var newLabels = new CheckBox { Content = Strings.Get(R.Tag_ShowLabels) };
        var newSwatches = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 7, ItemWidth = 32, ItemHeight = 32 };
        var newTitle = Typo.Caps(string.Empty);
        newSection.Children.Add(new Border { Height = 1, Background = Palette.Divider, Margin = new Thickness(0, 4, 0, 4) });
        newSection.Children.Add(newTitle);
        newSection.Children.Add(newSwatches);
        newSection.Children.Add(newLabels);

        bool IsNew(string tag) => !tags.Any(t => TagName.Comparer.Equals(t.Tag, tag));

        void RenderSwatches(string tag)
        {
            newSwatches.Children.Clear();
            var selected = newColor ?? TagPalette.ForTag(tag);
            foreach (var color in TagPalette.Colors)
            {
                var c = color;
                var swatch = new Button { Width = 28, Height = 28, Padding = new Thickness(0), CornerRadius = Radius.Control };
                swatch.StyleButton(new SolidColorBrush(Palette.ToColor(c)), new SolidColorBrush(Palette.ForegroundOn(c)), onColor: true);
                if (c == selected)
                {
                    swatch.BorderThickness = new Thickness(3);
                    swatch.BorderBrush = Palette.Ink;
                    swatch.Resources["ButtonBorderBrush"] = Palette.Ink;
                }

                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, c.ToHex());
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
                // Read the choices on the UI thread; only the tags.json writes go to the pool.
                var color = newColor;
                var labels = newLabels.IsChecked == true;
                await Task.Run(async () =>
                {
                    if (color is { } c)
                    {
                        await host.Tags.SetColorAsync(tag, c, CancellationToken.None);
                    }

                    if (labels)
                    {
                        await host.Tags.SetShowInCalendarLabelsAsync(tag, true, CancellationToken.None);
                    }
                });
            }

            await editor.ChangeTagAsync(tag);
        }

        void Render()
        {
            list.Children.Clear();
            var filter = input.Text.Trim().TrimStart('#');
            if (filter.Length == 0)
            {
                list.Children.Add(TagRow(brushes, null, Strings.Get(R.Tag_None), null, () => _ = ApplyAsync(null)));
            }

            foreach (var t in tags.Where(t => filter.Length == 0 || t.Tag.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).Take(30))
            {
                var tag = t.Tag;
                list.Children.Add(TagRow(brushes, tag, tag, t.Count.ToString(CultureInfo.CurrentCulture), () => _ = ApplyAsync(tag)));
            }

            if (TagName.TryNormalize(filter, out var typed) && IsNew(typed))
            {
                newTitle.Text = Strings.Get(R.Tag_Color) + " · #" + typed;
                RenderSwatches(typed);
                newSection.Visibility = Visibility.Visible;
            }
            else
            {
                newSection.Visibility = Visibility.Collapsed;
            }
        }

        input.TextChanged += (_, _) =>
        {
            error.Text = input.Text.Trim().Length > 0 && !TagName.TryNormalize(input.Text, out _) ? Strings.Get(R.Tag_Invalid) : string.Empty;
            newColor = null; // another name: back to its palette colour
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
            new ScrollViewer { Content = list, MaxHeight = 260 },
            newSection);

        input.Loaded += (_, _) => input.Focus(FocusState.Programmatic);
        return panel;
    }

    private static Button TagRow(TagBrushes brushes, string? tag, string label, string? count, Action onClick)
    {
        var row = Ui.Ui.Columns(
            (Ui.Ui.Auto, new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, Fill = brushes.BrushOf(tag), Margin = new Thickness(0, 0, 10, 0) }),
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
}
