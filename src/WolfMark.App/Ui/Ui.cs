using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

    private static readonly Thickness GlowThickness = new(2);

    /// <summary>
    /// Hover/pressed look for every button (ADR 0004): the fill never changes; a fine inner glow (the
    /// border, transparent at rest) and a different text colour show the state. Uses WinUI lightweight
    /// styling resources, so the default theme hover background never replaces the fill. Texts and icons
    /// inside inherit the button foreground. Pass a brush you own as <paramref name="fill"/> if the fill
    /// changes later – mutate its colour instead of replacing it.
    /// </summary>
    /// <param name="onColor">True for coloured fills (tags, accent button).</param>
    public static T StyleButton<T>(this T button, Brush fill, Brush ink, bool onColor = false, Brush? checkedFill = null)
        where T : ButtonBase
    {
        Brush hoverInk = Palette.Accent;
        Brush glow = Palette.GlowNeutral;
        Brush glowPressed = Palette.GlowNeutralStrong;
        if (onColor && fill is SolidColorBrush f && ink is SolidColorBrush restInk)
        {
            // Coloured fills: glow and hover text share one colour from {black, white, accent}.
            var hover = HoverColor(f.Color, restInk.Color);
            hoverInk = new SolidColorBrush(hover);
            glow = new SolidColorBrush(Windows.UI.Color.FromArgb(0xB4, hover.R, hover.G, hover.B));
            glowPressed = new SolidColorBrush(hover);
        }
        var chk = checkedFill ?? Palette.SubtleFillHover;

        button.Background = fill;
        button.Foreground = ink;
        button.BorderBrush = Palette.Transparent;
        button.BorderThickness = GlowThickness;
        // Fill under the border: otherwise the transparent glow border is a ring around the fill (coloured
        // buttons look 2px smaller) and the glow is drawn outside the colour, invisible against the paper.
        button.BackgroundSizing = BackgroundSizing.OuterBorderEdge;

        var r = button.Resources;
        foreach (var p in new[] { "Button", "ToggleButton" })
        {
            r[p + "Background"] = fill;
            r[p + "BackgroundPointerOver"] = fill;
            r[p + "BackgroundPressed"] = fill;
            r[p + "Foreground"] = ink;
            r[p + "ForegroundPointerOver"] = hoverInk;
            r[p + "ForegroundPressed"] = hoverInk;
            r[p + "BorderBrush"] = Palette.Transparent;
            r[p + "BorderBrushPointerOver"] = glow;
            r[p + "BorderBrushPressed"] = glowPressed;
        }

        r["ToggleButtonBackgroundChecked"] = chk;
        r["ToggleButtonBackgroundCheckedPointerOver"] = chk;
        r["ToggleButtonBackgroundCheckedPressed"] = chk;
        r["ToggleButtonForegroundChecked"] = ink;
        r["ToggleButtonForegroundCheckedPointerOver"] = hoverInk;
        r["ToggleButtonForegroundCheckedPressed"] = hoverInk;
        r["ToggleButtonBorderBrushChecked"] = Palette.Transparent;
        r["ToggleButtonBorderBrushCheckedPointerOver"] = glow;
        r["ToggleButtonBorderBrushCheckedPressed"] = glowPressed;

        InheritForeground(button.Content);
        return button;
    }

    private static readonly Windows.UI.Color HoverBlack = Windows.UI.Color.FromArgb(255, 0x11, 0x11, 0x11);
    private static readonly Windows.UI.Color HoverWhite = Windows.UI.Color.FromArgb(255, 0xFF, 0xFF, 0xFF);

    /// <summary>
    /// Hover colour for a coloured button (ADR 0004): one of black, white and the accent – never the colour
    /// the text already has at rest (so the text visibly changes) and never one that disappears in the
    /// fill; among the rest the one with the highest WCAG contrast against the fill.
    /// </summary>
    internal static Windows.UI.Color HoverColor(Windows.UI.Color fill, Windows.UI.Color restInk)
    {
        Windows.UI.Color[] candidates = [HoverBlack, HoverWhite, Palette.Accent.Color];
        var best = HoverBlack;
        var bestContrast = -1.0;
        foreach (var c in candidates)
        {
            if (Contrast(c, restInk) < 1.3)
            {
                continue; // same as the text at rest – would not show a change
            }

            var contrast = Contrast(c, fill);
            if (contrast > bestContrast)
            {
                best = c;
                bestContrast = contrast;
            }
        }

        return best;
    }

    private static double Contrast(Windows.UI.Color a, Windows.UI.Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Windows.UI.Color c)
    {
        static double Channel(byte v)
        {
            var x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    /// <summary>Lets texts and icons take the button's (state-dependent) foreground.</summary>
    public static void InheritForeground(object? content)
    {
        switch (content)
        {
            case TextBlock t:
                t.ClearValue(TextBlock.ForegroundProperty);
                break;
            case IconElement i:
                i.ClearValue(IconElement.ForegroundProperty);
                break;
            case Panel p:
                foreach (var child in p.Children)
                {
                    InheritForeground(child);
                }

                break;
            case Border b:
                InheritForeground(b.Child);
                break;
        }
    }

    /// <summary>Plain text button in the flat tag style (dialogs, info bars).</summary>
    public static Button TextButton(string text, Action? onClick = null)
    {
        var button = new Button { Content = text, CornerRadius = Radius.Control, Padding = new Thickness(12, 4, 12, 4) };
        button.StyleButton(Palette.SubtleFill, Palette.Ink);
        if (onClick is not null)
        {
            button.Click += (_, _) => onClick();
        }

        return button;
    }

    /// <summary>Flat icon button used in toolbars (slightly rounded, like tags).</summary>
    public static Button IconButton(string glyph, string tooltip, Action onClick, double size = 16)
    {
        var button = new Button
        {
            Content = Typo.Icon(glyph, size),
            Padding = new Thickness(7),
            MinWidth = Metrics.ButtonHeight,
            Height = Metrics.ButtonHeight,
            CornerRadius = Radius.Control,
        };
        button.StyleButton(Palette.Transparent, Palette.InkSecondary);
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
            Padding = new Thickness(10, 0, 10, 0),
            Height = Metrics.ButtonHeight,
            CornerRadius = Radius.Control,
        };
        button.StyleButton(fill ?? Palette.SubtleFill, ink ?? Palette.Ink, onColor: fill is not null);
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
        };
        button.StyleButton(active ? Palette.SubtleFillHover : Palette.SubtleFill, Palette.Ink);
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
