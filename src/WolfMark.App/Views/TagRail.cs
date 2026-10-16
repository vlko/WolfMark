using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.Ui;
using WolfMark.App.ViewModels;
using WolfMark.Core.Tags;

namespace WolfMark.App.Views;

/// <summary>
/// Vertical tag tabs at the right edge (reference <c>tags_full.png</c>). Choosing a tag makes it the active
/// tag (history filter, calendar dimming, tag of new notes); the active tab sticks out towards the content.
/// The rail stays open.
/// </summary>
internal sealed partial class TagRail : UserControl
{
    private const double TabWidth = 30;

    private readonly MainViewModel _vm;
    private readonly TagBrushes _brushes;
    private readonly StackPanel _items = new() { Spacing = 4 };

    public TagRail(MainViewModel vm, TagBrushes brushes)
    {
        _vm = vm;
        _brushes = brushes;
        Content = new ScrollViewer
        {
            Content = _items,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Disabled,
            Padding = new Thickness(0, 4, 0, 12),
        };
        Width = TabWidth + 10;

        vm.RailTags.CollectionChanged += (_, _) => Render();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveTag))
            {
                Render();
            }
        };
        brushes.Changed += (_, _) => Render();
        Render();
    }

    private void Render()
    {
        _items.Children.Clear();
        var active = _vm.ActiveTag;
        _items.Children.Add(Tab(null, isActive: active is null, () => _ = _vm.SetActiveTagAsync(null)));
        foreach (var t in _vm.RailTags)
        {
            var tag = t.Tag;
            var isActive = active is not null && TagName.Comparer.Equals(active, tag);
            _items.Children.Add(Tab(tag, isActive, () => _ = _vm.SetActiveTagAsync(isActive ? null : tag)));
        }
    }

    /// <summary>A tab with its label rotated 90° (WinUI has no LayoutTransform: a Canvas hosts the rotated text).</summary>
    private Button Tab(string? tag, bool isActive, Action onClick)
    {
        var label = (tag ?? Strings.Get(R.Rail_All)).ToUpper(CultureInfo.CurrentCulture);
        var color = tag is null ? (Brush)Palette.LabelGrey : _brushes.BrushOf(tag);
        var ink = tag is null ? Palette.LabelInk : new SolidColorBrush(Palette.ForegroundOn(_brushes.ColorOf(tag)));

        var text = new TextBlock
        {
            Text = label,
            FontFamily = Typo.CondensedMedium,
            FontSize = 14,
            CharacterSpacing = 30,
            Foreground = ink,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var length = Math.Max(64, Math.Ceiling(text.DesiredSize.Width) + 24);
        // The button keeps a 2px glow border; the canvas fills the inner area and hosts the rotated text.
        var innerWidth = TabWidth - 4;
        var innerLength = length - 4;
        text.Width = innerLength;
        text.Height = innerWidth;
        text.Padding = new Thickness(0, 4, 0, 0);
        text.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        text.RenderTransform = new RotateTransform { Angle = -90 };
        Canvas.SetLeft(text, (innerWidth - innerLength) / 2);
        Canvas.SetTop(text, (innerLength - innerWidth) / 2);
        var canvas = new Canvas { Width = innerWidth, Height = innerLength };
        canvas.Children.Add(text);

        var button = new Button
        {
            Content = canvas,
            Width = TabWidth,
            Height = length,
            Padding = new Thickness(0),
            CornerRadius = Radius.Tag,
            HorizontalAlignment = HorizontalAlignment.Right,
            // The active tab sticks out towards the content, like a pulled index tab.
            Margin = new Thickness(0, 0, isActive ? 8 : 2, 0),
        };
        button.StyleButton(color, ink, onColor: tag is not null);
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Click += (_, _) => onClick();
        return button;
    }
}
