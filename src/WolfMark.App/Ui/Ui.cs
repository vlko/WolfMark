using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WolfMark.App.Theming;

namespace WolfMark.App.Ui;

/// <summary>
/// Helpers for building WinUI trees in C# (docs/adr/0002-ui-in-code.md). Bindings are explicit:
/// <see cref="Bind"/> runs an update now and whenever the named property changes – no reflection.
/// </summary>
internal static class Ui
{
    public static T Bind<T>(this T element, INotifyPropertyChanged source, string property, Action apply)
        where T : DependencyObject
    {
        apply();
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null || e.PropertyName == property)
            {
                apply();
            }
        };
        return element;
    }

    public static T Bind<T>(this T element, INotifyPropertyChanged source, string[] properties, Action apply)
        where T : DependencyObject
    {
        apply();
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null || Array.IndexOf(properties, e.PropertyName) >= 0)
            {
                apply();
            }
        };
        return element;
    }

    public static Grid Rows(params (GridLength Height, UIElement? Child)[] rows)
    {
        var grid = new Grid();
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = rows[i].Height });
            if (rows[i].Child is { } child)
            {
                Grid.SetRow((FrameworkElement)child, i);
                grid.Children.Add(child);
            }
        }

        return grid;
    }

    public static Grid Columns(params (GridLength Width, UIElement? Child)[] columns)
    {
        var grid = new Grid();
        for (var i = 0; i < columns.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = columns[i].Width });
            if (columns[i].Child is { } child)
            {
                Grid.SetColumn((FrameworkElement)child, i);
                grid.Children.Add(child);
            }
        }

        return grid;
    }

    public static StackPanel HStack(double spacing, params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children)
        {
            panel.Children.Add(c);
        }

        return panel;
    }

    public static StackPanel VStack(double spacing, params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = spacing };
        foreach (var c in children)
        {
            panel.Children.Add(c);
        }

        return panel;
    }

    public static GridLength Auto => GridLength.Auto;

    public static GridLength Star => new(1, GridUnitType.Star);

    public static GridLength Px(double v) => new(v, GridUnitType.Pixel);

    public static GridLength Stars(double v) => new(v, GridUnitType.Star);

    /// <summary>Flat icon button used in toolbars (slightly rounded, like tags).</summary>
    public static Button IconButton(string glyph, string tooltip, Action onClick, double size = 16)
    {
        var button = new Button
        {
            Content = Typo.Icon(glyph, size),
            Padding = new Thickness(8),
            MinWidth = 34,
            MinHeight = 34,
            Background = Palette.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = Radius.Control,
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Tag-style button: flat fill, condensed caps label, slight rounding.</summary>
    public static Button TagButton(string text, Action onClick, Brush? fill = null, Brush? ink = null, string? glyph = null)
    {
        var label = Typo.Caps(text, 13, ink ?? Palette.Ink);
        UIElement content = glyph is null ? label : HStack(6, Typo.Icon(glyph, 12, ink ?? Palette.Ink), label);
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(10, 4, 10, 4),
            MinHeight = 30,
            CornerRadius = Radius.Control,
            BorderThickness = new Thickness(0),
            Background = fill ?? Palette.SubtleFill,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Tag chip: coloured square + label (+ optional count).</summary>
    public static Button Pill(Brush dot, string label, string? count, Action onClick, bool active = false)
    {
        var content = HStack(6,
            new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 9, Height = 9, Fill = dot, VerticalAlignment = VerticalAlignment.Center },
            Typo.Caps(label, 13, Palette.Ink));
        if (count is not null)
        {
            content.Children.Add(Typo.Caps(count, 12.5, Palette.InkTertiary));
        }

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(9, 3, 10, 3),
            MinHeight = 28,
            CornerRadius = Radius.Control,
            BorderThickness = new Thickness(0),
            Background = active ? Palette.SubtleFillHover : Palette.SubtleFill,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, count is null ? label : $"{label} {count}");
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Vertical dotted separator (time gutter ┆ content).</summary>
    public static Border DottedLine() => new() { Width = 1, VerticalAlignment = VerticalAlignment.Stretch, Background = Palette.Dotted };

    /// <summary>Rounded card – only for floating surfaces (search palette); the main UI is flat.</summary>
    public static Border Card(UIElement child, double radius = 18) => new()
    {
        Child = child,
        Background = Palette.Card,
        BorderBrush = Palette.CardStroke,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius),
    };
}
