using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WolfMark.App.Infrastructure;
using WolfMark.App.ViewModels;
using WolfMark.Core.Tags;
using WolfMark.Desktop.Theming;

namespace WolfMark.Desktop.Views;

/// <summary>
/// Vertical tag tabs at the right edge: the active tag filters history, dims the calendar and tags new notes;
/// the active tab sticks out towards the content.
/// </summary>
internal sealed class TagRail : UserControl
{
    private const double TabWidth = 30;

    private readonly MainViewModel _vm;
    private readonly StackPanel _items = new() { Spacing = 4 };

    public TagRail(MainViewModel vm)
    {
        _vm = vm;
        Content = new ScrollViewer
        {
            Content = _items,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden,
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
        Palette.Changed += (_, _) => Render();
        Render();
    }

    private void Render()
    {
        _items.Children.Clear();
        var active = _vm.ActiveTag;
        _items.Children.Add(Tab(null, active is null, () => _ = _vm.SetActiveTagAsync(null)));
        foreach (var t in _vm.RailTags)
        {
            var tag = t.Tag;
            var isActive = active is not null && TagName.Comparer.Equals(active, tag);
            _items.Children.Add(Tab(tag, isActive, () => _ = _vm.SetActiveTagAsync(isActive ? null : tag)));
        }
    }

    /// <summary>A tab with its label turned 90° (a layout transform, so the tab gets the label's length).</summary>
    private Button Tab(string? tag, bool isActive, Action onClick)
    {
        var label = (tag ?? Strings.Get(R.Rail_All)).ToUpper(CultureInfo.CurrentCulture);
        var color = tag is null ? Palette.InkTertiary.Color : Palette.ToColor(_vm.Host.Tags.GetColor(tag));
        var ink = tag is not null && _vm.Host.Tags.GetColor(tag).PrefersDarkForeground ? Color.FromRgb(0x1F, 0x1F, 0x1E) : Colors.White;
        var text = new TextBlock
        {
            Text = label,
            FontFamily = Typo.CondensedMedium,
            FontSize = 14,
            LetterSpacing = 0.4,
            Foreground = new SolidColorBrush(ink),
            TextWrapping = TextWrapping.NoWrap,
        };
        // Turned by a render transform inside a canvas sized for the turned text (as on Windows).
        text.Measure(Size.Infinity);
        var length = Math.Max(64, Math.Ceiling(text.DesiredSize.Width) + 24);
        var thickness = Math.Ceiling(text.DesiredSize.Height);
        text.Width = Math.Ceiling(text.DesiredSize.Width);
        text.RenderTransformOrigin = RelativePoint.Center;
        text.RenderTransform = new RotateTransform(-90);
        Canvas.SetLeft(text, (TabWidth - text.Width) / 2);
        Canvas.SetTop(text, (length - thickness) / 2);
        var turned = new Canvas { Width = TabWidth, Height = length };
        turned.Children.Add(text);
        var button = new Button
        {
            Content = turned,
            Width = TabWidth,
            Height = length,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, isActive ? 8 : 2, 0),
        };
        ToolTip.SetTip(button, label);
        button.Click += (_, _) => onClick();
        return button;
    }
}
