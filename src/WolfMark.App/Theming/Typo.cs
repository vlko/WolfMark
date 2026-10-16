using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WolfMark.App.Theming;

/// <summary>
/// Typography (ADR 0004). Barlow is bundled (OFL, <c>Assets/Fonts</c>); each family falls back to
/// Segoe UI Variable if the font cannot be loaded.
/// </summary>
internal static class Typo
{
    private const string FontDir = "ms-appx:///Assets/Fonts/";

    /// <summary>Thin condensed lead numbers ("12", times in the gutter).</summary>
    public static FontFamily Lead { get; } = new(FontDir + "BarlowCondensed-Light.ttf#Barlow Condensed Light, Segoe UI Variable Display Light");

    /// <summary>Condensed headers ("CALENDARS", month names).</summary>
    public static FontFamily Condensed { get; } = new(FontDir + "BarlowCondensed-Regular.ttf#Barlow Condensed, Segoe UI Variable Display");

    public static FontFamily CondensedMedium { get; } = new(FontDir + "BarlowCondensed-Medium.ttf#Barlow Condensed Medium, Segoe UI Variable Display");

    /// <summary>Strong condensed headers ("WEDNESDAY", "AFTERNOON").</summary>
    public static FontFamily CondensedBold { get; } = new(FontDir + "BarlowCondensed-SemiBold.ttf#Barlow Condensed SemiBold, Segoe UI Variable Display");

    /// <summary>Heavy numerals of calendar days.</summary>
    public static FontFamily DayNumber { get; } = new(FontDir + "Barlow-ExtraBold.ttf#Barlow ExtraBold, Segoe UI Variable Display");

    public static FontFamily Display { get; } = new("Segoe UI Variable Display");

    public static FontFamily Text { get; } = new("Segoe UI Variable Text");

    public static FontFamily Small { get; } = new("Segoe UI Variable Small");

    public static FontFamily Icons { get; } = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Compact uppercase labels in the condensed face.</summary>
    public static TextBlock Caps(string text, double size = 12.5, Brush? brush = null) => new()
    {
        Text = text.ToUpper(CultureInfo.CurrentCulture),
        FontFamily = CondensedMedium,
        FontSize = size,
        CharacterSpacing = 40,
        Foreground = brush ?? Palette.InkTertiary,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Section header with a hairline to the right edge ("MORNING ————").</summary>
    public static Grid SectionHeader(string text, out TextBlock label, double size = 19)
    {
        label = new TextBlock
        {
            Text = text.ToUpper(CultureInfo.CurrentCulture),
            FontFamily = CondensedBold,
            FontSize = size,
            Foreground = Palette.InkSecondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var line = new Border { Height = 1, Background = Palette.Divider, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 2, 0, 0) };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(line, 1);
        grid.Children.Add(label);
        grid.Children.Add(line);
        return grid;
    }

    public static TextBlock Body(string text = "", double size = 14, Brush? brush = null) => new()
    {
        Text = text,
        FontFamily = Text,
        FontSize = size,
        Foreground = brush ?? Palette.Ink,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static FontIcon Icon(string glyph, double size = 16, Brush? brush = null) => new()
    {
        Glyph = glyph,
        FontFamily = Icons,
        FontSize = size,
        Foreground = brush ?? Palette.InkSecondary,
    };

    /// <summary>
    /// Lead number with offset pink/yellow print layers (reference "12"): three stacked glyph runs.
    /// </summary>
    public static Grid RisoNumber(out Action<string> setText, double size, Brush? ink = null)
    {
        TextBlock Layer(Brush brush, double dx, double dy) => new()
        {
            FontFamily = Lead,
            FontSize = size,
            LineHeight = size,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = brush,
            RenderTransform = new TranslateTransform { X = dx, Y = dy },
            IsHitTestVisible = false,
        };

        var yellow = Layer(Palette.RisoYellow, size * 0.035, size * 0.03);
        var pink = Layer(Palette.RisoPink, -size * 0.04, -size * 0.02);
        var main = Layer(ink ?? Palette.Accent, 0, 0);
        var grid = new Grid { VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(yellow);
        grid.Children.Add(pink);
        grid.Children.Add(main);
        setText = t => yellow.Text = pink.Text = main.Text = t;
        return grid;
    }

    /// <summary>Time as a thin lead hour with small minutes ("14⁰³" / "6³⁰a"), as in the reference day view.</summary>
    public static StackPanel LeadTime(out Action<DateTime?> setTime, double size = 30, Brush? brush = null)
    {
        var hour = new TextBlock { FontFamily = Lead, FontSize = size, Foreground = brush ?? Palette.InkSecondary, LineHeight = size, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
        var minutes = new TextBlock { FontFamily = Condensed, FontSize = size * 0.5, Foreground = brush ?? Palette.InkSecondary, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(1, 0, 0, size * 0.08) };
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

            var twelve = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('h', StringComparison.Ordinal);
            var h = twelve ? (time.Hour % 12 == 0 ? 12 : time.Hour % 12) : time.Hour;
            hour.Text = h.ToString(CultureInfo.CurrentCulture);
            minutes.Text = time.Minute.ToString("00", CultureInfo.CurrentCulture) + (twelve ? (time.Hour < 12 ? "a" : "p") : string.Empty);
        };
        return panel;
    }
}

/// <summary>Segoe Fluent Icons code points.</summary>
internal static class Glyphs
{
    public const string Add = "";
    public const string Calendar = "";
    public const string Search = "";
    public const string Settings = "";
    public const string ChevronLeft = "";
    public const string ChevronRight = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string Close = "";
    public const string Tag = "";
    public const string Lock = "";
    public const string Warning = "";
    public const string Sync = "";
    public const string Delete = "";
    public const string Folder = "";
    public const string Link = "";
    public const string Checkbox = "";
    public const string CheckboxComposite = "";
    public const string Code = "";
    public const string Bold = "";
    public const string Italic = "";
    public const string Today = "";
    public const string Cloud = "";
    public const string Edit = "";
    public const string Expand = "";
    public const string Collapse = "";
}
