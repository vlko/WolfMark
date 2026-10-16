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

    /// <summary>Flat icon button used in toolbars.</summary>
    public static Button IconButton(string glyph, string tooltip, Action onClick, double size = 16)
    {
        var button = new Button
        {
            Content = Typo.Icon(glyph, size),
            Padding = new Thickness(8),
            MinWidth = 36,
            MinHeight = 36,
            Background = Palette.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
        };
        ToolTipService.SetToolTip(button, tooltip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Pill-shaped button: coloured dot + label (+ optional count) – the tag pill of SPEC §5.1.</summary>
    public static Button Pill(Brush dot, string label, string? count, Action onClick, bool active = false)
    {
        var content = HStack(6,
            new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 8, Height = 8, Fill = dot, VerticalAlignment = VerticalAlignment.Center },
            Typo.Body(label, 12.5, Palette.Ink));
        if (count is not null)
        {
            content.Children.Add(Typo.Body(count, 12, Palette.InkTertiary));
        }

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(10, 4, 12, 4),
            MinHeight = 28,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(active ? 1.5 : 1),
            BorderBrush = active ? dot : Palette.Divider,
            Background = active ? Palette.SubtleFillHover : Palette.Card,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, count is null ? label : $"{label} {count}");
        button.Click += (_, _) => onClick();
        return button;
    }

    public static Border Card(UIElement child, double radius = 18) => new()
    {
        Child = child,
        Background = Palette.Card,
        BorderBrush = Palette.CardStroke,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius),
    };
}
