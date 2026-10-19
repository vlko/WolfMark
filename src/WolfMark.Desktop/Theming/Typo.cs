using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace WolfMark.Desktop.Theming;

/// <summary>Type scale of the Windows app: Barlow (bundled, SIL OFL) for numbers and caps, the system UI font for text.</summary>
internal static class Typo
{
    private const string Fonts = "avares://WolfMark.Desktop/Assets/Fonts/";

    // One font file per family, as the Windows app does (src/WolfMark.App/Theming/Typo.cs): all Barlow Condensed
    // files share one typographic family, and picking a weight inside it fell back to a heavier face – the thin
    // time digits came out bold.

    /// <summary>Thin lead numbers: times, the month in the day heading.</summary>
    public static FontFamily Lead { get; } = new(Fonts + "BarlowCondensed-Light.ttf#Barlow Condensed");

    public static FontFamily Condensed { get; } = new(Fonts + "BarlowCondensed-Regular.ttf#Barlow Condensed");

    public static FontFamily CondensedMedium { get; } = new(Fonts + "BarlowCondensed-Medium.ttf#Barlow Condensed");

    public static FontFamily CondensedBold { get; } = new(Fonts + "BarlowCondensed-SemiBold.ttf#Barlow Condensed");

    /// <summary>Heavy calendar numerals.</summary>
    public static FontFamily DayNumber { get; } = new(Fonts + "Barlow-ExtraBold.ttf#Barlow");

    public static TextBlock Caps(string text, double size = 12.5, IBrush? brush = null) => new()
    {
        Text = text,
        FontFamily = CondensedMedium,
        FontSize = size,
        LetterSpacing = size * 0.06,
        Foreground = brush ?? Palette.InkSecondary,
    };

    public static TextBlock Body(string text = "", double size = 14, IBrush? brush = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = brush ?? Palette.Ink,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>Lead number with offset pink/yellow print layers (the "riso" day number).</summary>
    public static Panel RisoNumber(out Action<string> setText, double size)
    {
        TextBlock Layer(IBrush brush, double dx, double dy) => new()
        {
            FontFamily = Lead,
            FontSize = size,
            LineHeight = size,
            Foreground = brush,
            RenderTransform = new TranslateTransform(dx, dy),
            IsHitTestVisible = false,
        };

        var yellow = Layer(Palette.RisoYellow, size * 0.035, size * 0.03);
        var pink = Layer(Palette.RisoPink, -size * 0.04, -size * 0.02);
        var main = Layer(Palette.Accent, 0, 0);
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(yellow);
        grid.Children.Add(pink);
        grid.Children.Add(main);
        setText = t => yellow.Text = pink.Text = main.Text = t;
        return grid;
    }

    /// <summary>Time as a thin lead hour with small minutes ("14⁰³").</summary>
    public static StackPanel LeadTime(out Action<DateTime?> setTime, double size = 30, IBrush? brush = null)
    {
        var hour = new TextBlock { FontFamily = Lead, FontSize = size, LineHeight = size, Foreground = brush ?? Palette.InkSecondary };
        var minutes = new TextBlock
        {
            FontFamily = Condensed,
            FontSize = size * 0.5,
            Foreground = brush ?? Palette.InkSecondary,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(1, 0, 0, size * 0.08),
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(hour);
        panel.Children.Add(minutes);
        setTime = t =>
        {
            if (t is not { } time)
            {
                hour.Text = minutes.Text = string.Empty;
                return;
            }

            hour.Text = time.Hour.ToString(CultureInfo.CurrentCulture);
            minutes.Text = time.Minute.ToString("00", CultureInfo.CurrentCulture);
        };
        return panel;
    }
}
