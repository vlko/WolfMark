using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WolfMark.App.Theming;

/// <summary>Typography tokens.</summary>
internal static class Typo
{
    public static FontFamily Display { get; } = new("Segoe UI Variable Display");

    public static FontFamily Text { get; } = new("Segoe UI Variable Text");

    public static FontFamily Small { get; } = new("Segoe UI Variable Small");

    public static FontFamily Icons { get; } = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Compact uppercase labels ("OKTÓBER 2026", "DNES").</summary>
    public static TextBlock Caps(string text, double size = 11, Brush? brush = null) => new()
    {
        Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture),
        FontFamily = Small,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        CharacterSpacing = 70,
        Foreground = brush ?? Palette.InkTertiary,
        VerticalAlignment = VerticalAlignment.Center,
    };

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
}
