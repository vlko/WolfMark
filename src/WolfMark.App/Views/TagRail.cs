using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using WolfMark.App.Infrastructure;
using WolfMark.App.Theming;
using WolfMark.App.ViewModels;
using WolfMark.Core.Tags;

namespace WolfMark.App.Views;

/// <summary>
/// Vertical tag tabs at the right edge (reference <c>tags_full.png</c>). Choosing a tag makes it the active
/// tag (history filter, calendar dimming, tag of new notes) and folds the rail to that tab plus thin colour
/// stripes of the others (<c>tags_folded.png</c>); clicking the folded rail opens it again.
/// </summary>
internal sealed partial class TagRail : UserControl
{
    private const double TabWidth = 30;

    private readonly MainViewModel _vm;
    private readonly TagBrushes _brushes;
    private readonly StackPanel _items = new() { Spacing = 4 };
    private bool _folded;

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
                _folded = vm.ActiveTag is not null;
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
        if (_folded && active is not null)
        {
            _items.Children.Add(Tab(active, isActive: true, () => Unfold()));
            _items.Children.Add(Stripes(active));
            return;
        }

        _items.Children.Add(Tab(null, isActive: active is null, () => _ = _vm.SetActiveTagAsync(null)));
        foreach (var t in _vm.RailTags)
        {
            var tag = t.Tag;
            var isActive = active is not null && TagName.Comparer.Equals(active, tag);
            _items.Children.Add(Tab(tag, isActive, () => _ = _vm.SetActiveTagAsync(isActive ? null : tag)));
        }
    }

    private void Unfold()
    {
        _folded = false;
        Render();
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
        text.Width = length;
        text.Height = TabWidth;
        text.Padding = new Thickness(0, 5, 0, 0);
        text.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        text.RenderTransform = new RotateTransform { Angle = -90 };
        Canvas.SetLeft(text, (TabWidth - length) / 2);
        Canvas.SetTop(text, (length - TabWidth) / 2);
        var canvas = new Canvas { Width = TabWidth, Height = length };
        canvas.Children.Add(text);

        var button = new Button
        {
            Content = canvas,
            Width = TabWidth,
            Height = length,
            Padding = new Thickness(0),
            Background = color,
            BorderThickness = new Thickness(0),
            CornerRadius = Radius.Tag,
            HorizontalAlignment = HorizontalAlignment.Right,
            // The active tab sticks out towards the content, like a pulled index tab.
            Margin = new Thickness(0, 0, isActive ? 8 : 2, 0),
        };
        AutomationProperties.SetName(button, label);
        ToolTipService.SetToolTip(button, label);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Folded remainder: one thin stripe per other tag; clicking opens the rail.</summary>
    private Button Stripes(string active)
    {
        var panel = new StackPanel { Spacing = 2 };
        foreach (var t in _vm.RailTags.Where(t => !TagName.Comparer.Equals(t.Tag, active)).Take(12))
        {
            panel.Children.Add(new Rectangle { Height = 4, Width = TabWidth, Fill = _brushes.BrushOf(t.Tag) });
        }

        var button = new Button
        {
            Content = panel,
            Width = TabWidth,
            Padding = new Thickness(0),
            Background = Palette.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = Radius.None,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 2, 0),
        };
        var tip = Strings.Get(R.Rail_Show);
        AutomationProperties.SetName(button, tip);
        ToolTipService.SetToolTip(button, tip);
        button.Click += (_, _) => Unfold();
        return button;
    }
}
