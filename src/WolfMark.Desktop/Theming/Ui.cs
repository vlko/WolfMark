using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WolfMark.Desktop.Theming;

/// <summary>Code-only layout and binding helpers (the Avalonia twin of src/WolfMark.App/Ui/Ui.cs).</summary>
internal static class Ui
{
    /// <summary>Runs <paramref name="update"/> now and whenever one of the properties of <paramref name="source"/> changes.</summary>
    public static void Bind(INotifyPropertyChanged source, string[] properties, Action update)
    {
        update();
        source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is null || properties.Contains(e.PropertyName))
            {
                update();
            }
        };
    }

    public static void Bind(INotifyPropertyChanged source, string property, Action update) => Bind(source, [property], update);

    public static StackPanel VStack(double spacing, params Control[] children)
    {
        var panel = new StackPanel { Spacing = spacing };
        panel.Children.AddRange(children);
        return panel;
    }

    public static StackPanel HStack(double spacing, params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        panel.Children.AddRange(children);
        return panel;
    }

    /// <summary>A grid of columns; null cells stay empty.</summary>
    public static Grid Columns(params (GridLength Width, Control? Child)[] cells)
    {
        var grid = new Grid();
        for (var i = 0; i < cells.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(cells[i].Width));
            if (cells[i].Child is { } child)
            {
                Grid.SetColumn(child, i);
                grid.Children.Add(child);
            }
        }

        return grid;
    }

    public static GridLength Px(double value) => new(value, GridUnitType.Pixel);

    public static GridLength Star => new(1, GridUnitType.Star);

    public static GridLength Auto => GridLength.Auto;

    /// <summary>One height for every toolbar/tag-style button, so rows of buttons line up.</summary>
    public const double ButtonHeight = 32;

    /// <summary>Square transparent button with an icon (calendar arrows, expand, settings).</summary>
    public static Button IconButton(Control icon, string tooltip, Action onClick)
    {
        var button = new Button
        {
            Content = icon,
            Width = ButtonHeight,
            Height = ButtonHeight,
            Padding = new Thickness(0),
            Background = Palette.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Caps label on the subtle fill ("TODAY").</summary>
    public static Button TagButton(string text, Action onClick)
    {
        var label = Typo.Caps(text.ToUpper(System.Globalization.CultureInfo.CurrentCulture), 13, Palette.InkSecondary);
        var button = FlatButton(label);
        button.Height = ButtonHeight;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Square toolbar button (format toggles): fixed size whatever the content.</summary>
    public static Button ToolButton(Control content, string tooltip)
    {
        var button = new Button
        {
            Content = content,
            Width = ButtonHeight + 4,
            Height = ButtonHeight,
            Padding = new Thickness(0),
            Background = Palette.Transparent,
            Foreground = Palette.Ink,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Focusable = false, // the editor keeps its selection
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    /// <summary>"HISTORY ————": condensed bold caps and a hairline to the end.</summary>
    public static Grid SectionHeader(string text, double size = 19)
    {
        var label = new TextBlock
        {
            Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
            FontFamily = Typo.CondensedBold,
            FontSize = size,
            Foreground = Palette.InkSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var line = new Border { Height = 1, Background = Palette.Divider, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 2, 0, 0) };
        return Columns((Auto, label), (Star, line));
    }

    /// <summary>Flat button (ADR 0004): no border, a subtle fill.</summary>
    public static Button FlatButton(object content, IBrush? background = null) => new()
    {
        Content = content,
        Background = background ?? Palette.SubtleFill,
        Foreground = Palette.Ink,
        BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(10, 5),
    };
}

/// <summary>Vertical dotted line (2 px dash, 3 px gap) in the tertiary ink – the time gutter's edge.</summary>
internal sealed class DottedLine : Control
{
    public DottedLine()
    {
        Width = 1;
        Palette.Changed += (_, _) => InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Palette.InkTertiary, 1);
        for (double y = 0; y < Bounds.Height; y += 5)
        {
            context.DrawLine(pen, new Point(0.5, y), new Point(0.5, Math.Min(y + 2, Bounds.Height)));
        }
    }
}
