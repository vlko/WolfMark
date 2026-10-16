using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WolfMark.Core.Tags;

namespace WolfMark.App.Theming;

/// <summary>
/// Design tokens (docs/adr/0004-flat-design.md): one flat paper colour for the whole window, ink tones,
/// vivid tag colours. Brushes are shared instances whose colours are updated in place on theme change.
/// </summary>
internal static class Palette
{
    /// <summary>Window background – the single surface of the flat design.</summary>
    public static SolidColorBrush WindowTint { get; } = new();

    /// <summary>Same as the window in the flat design; kept for overlays (search) that sit on top.</summary>
    public static SolidColorBrush Card { get; } = new();

    public static SolidColorBrush CardStroke { get; } = new();

    public static SolidColorBrush Ink { get; } = new();

    public static SolidColorBrush InkSecondary { get; } = new();

    public static SolidColorBrush InkTertiary { get; } = new();

    /// <summary>Flat fill of buttons, chips and selected rows.</summary>
    public static SolidColorBrush SubtleFill { get; } = new();

    public static SolidColorBrush SubtleFillHover { get; } = new();

    public static SolidColorBrush Accent { get; } = new();

    public static SolidColorBrush TodayFill { get; } = new();

    public static SolidColorBrush TodayInk { get; } = new();

    public static SolidColorBrush Divider { get; } = new();

    public static SolidColorBrush EmptyCell { get; } = new();

    /// <summary>Grey of weekday bubbles and week-number tags.</summary>
    public static SolidColorBrush LabelGrey { get; } = new();

    public static SolidColorBrush LabelInk { get; } = new();

    /// <summary>Offset layers of the "riso" lead numbers.</summary>
    public static SolidColorBrush RisoPink { get; } = new();

    public static SolidColorBrush RisoYellow { get; } = new();

    /// <summary>Two-tone dashed frame of today (yellow under black dashes).</summary>
    public static SolidColorBrush TodayFrame { get; } = new();

    public static SolidColorBrush TodayFrameDash { get; } = new();

    public static SolidColorBrush Overlay { get; } = new();

    public static SolidColorBrush Transparent { get; } = new(Microsoft.UI.Colors.Transparent);

    /// <summary>Diagonal stripes laid over past days of the week band (reference: hatched pastel days).</summary>
    public static LinearGradientBrush PastHatch { get; } = new()
    {
        MappingMode = BrushMappingMode.Absolute,
        StartPoint = new Windows.Foundation.Point(0, 0),
        EndPoint = new Windows.Foundation.Point(5, 5),
        SpreadMethod = GradientSpreadMethod.Repeat,
        GradientStops =
        {
            new GradientStop { Offset = 0 },
            new GradientStop { Offset = 0.5 },
            new GradientStop { Color = Microsoft.UI.Colors.Transparent, Offset = 0.5 },
            new GradientStop { Color = Microsoft.UI.Colors.Transparent, Offset = 1 },
        },
    };

    /// <summary>Vertical dotted line (repeating 2px dash / 3px gap) in the tertiary ink.</summary>
    public static LinearGradientBrush Dotted { get; } = new()
    {
        MappingMode = BrushMappingMode.Absolute,
        StartPoint = new Windows.Foundation.Point(0, 0),
        EndPoint = new Windows.Foundation.Point(0, 5),
        SpreadMethod = GradientSpreadMethod.Repeat,
        GradientStops =
        {
            new GradientStop { Offset = 0 },
            new GradientStop { Offset = 0.4 },
            new GradientStop { Color = Microsoft.UI.Colors.Transparent, Offset = 0.4 },
            new GradientStop { Color = Microsoft.UI.Colors.Transparent, Offset = 1 },
        },
    };

    public static bool IsDark { get; private set; }

    public static event EventHandler? Changed;

    /// <param name="translucent">True with a Mica/Acrylic backdrop – the paper then lets it shine through.</param>
    public static void Apply(ElementTheme theme, bool translucent)
    {
        IsDark = theme == ElementTheme.Dark;
        byte paperAlpha = translucent ? (byte)0xE0 : (byte)0xFF;
        if (IsDark)
        {
            Set(WindowTint, paperAlpha, 0x1A, 0x1B, 0x1D);
            Set(Card, 0xFF, 0x22, 0x23, 0x26);
            Set(CardStroke, 0xFF, 0x34, 0x36, 0x3A);
            Set(Ink, 0xFF, 0xEE, 0xEC, 0xE7);
            Set(InkSecondary, 0xFF, 0xAA, 0xA7, 0xA0);
            Set(InkTertiary, 0xFF, 0x70, 0x6D, 0x68);
            Set(SubtleFill, 0xFF, 0x2A, 0x2C, 0x30);
            Set(SubtleFillHover, 0xFF, 0x35, 0x37, 0x3C);
            Set(Accent, 0xFF, 0x4F, 0xA3, 0xFF);
            Set(TodayFill, 0xFF, 0xEE, 0xEC, 0xE7);
            Set(TodayInk, 0xFF, 0x1A, 0x1B, 0x1D);
            Set(Divider, 0xFF, 0x34, 0x36, 0x3A);
            Set(EmptyCell, 0xFF, 0x25, 0x27, 0x2A);
            Set(LabelGrey, 0xFF, 0x46, 0x49, 0x4E);
            Set(LabelInk, 0xFF, 0xE6, 0xE6, 0xE6);
            Set(RisoPink, 0xFF, 0xC2, 0x4F, 0x7D);
            Set(RisoYellow, 0xFF, 0xB8, 0x94, 0x2A);
            Set(TodayFrame, 0xFF, 0xF6, 0xC6, 0x2F);
            Set(TodayFrameDash, 0xFF, 0x0E, 0x0F, 0x10);
            Set(Overlay, 0x99, 0x00, 0x00, 0x00);
        }
        else
        {
            Set(WindowTint, paperAlpha, 0xFA, 0xF9, 0xF4);
            Set(Card, 0xFF, 0xFF, 0xFF, 0xFC);
            Set(CardStroke, 0xFF, 0xE6, 0xE3, 0xDA);
            Set(Ink, 0xFF, 0x1F, 0x1F, 0x1E);
            Set(InkSecondary, 0xFF, 0x6B, 0x68, 0x62);
            Set(InkTertiary, 0xFF, 0xA8, 0xA4, 0x9B);
            Set(SubtleFill, 0xFF, 0xEF, 0xED, 0xE6);
            Set(SubtleFillHover, 0xFF, 0xE5, 0xE2, 0xD9);
            Set(Accent, 0xFF, 0x1E, 0x88, 0xF5);
            Set(TodayFill, 0xFF, 0x1F, 0x1F, 0x1E);
            Set(TodayInk, 0xFF, 0xFF, 0xFF, 0xFF);
            Set(Divider, 0xFF, 0xE4, 0xE0, 0xD6);
            Set(EmptyCell, 0xFF, 0xEE, 0xEC, 0xE5);
            Set(LabelGrey, 0xFF, 0xC6, 0xC9, 0xCE);
            Set(LabelInk, 0xFF, 0xFF, 0xFF, 0xFF);
            Set(RisoPink, 0xFF, 0xF4, 0x9C, 0xBB);
            Set(RisoYellow, 0xFF, 0xF6, 0xD3, 0x5B);
            Set(TodayFrame, 0xFF, 0xF6, 0xC6, 0x2F);
            Set(TodayFrameDash, 0xFF, 0x1F, 0x1F, 0x1E);
            Set(Overlay, 0x66, 0x20, 0x1C, 0x16);
        }

        var hatch = IsDark ? Color.FromArgb(0x70, 0x1A, 0x1B, 0x1D) : Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF);
        PastHatch.GradientStops[0].Color = hatch;
        PastHatch.GradientStops[1].Color = hatch;
        Dotted.GradientStops[0].Color = InkTertiary.Color;
        Dotted.GradientStops[1].Color = InkTertiary.Color;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static ElementTheme CurrentTheme => IsDark ? ElementTheme.Dark : ElementTheme.Light;

    public static Color ToColor(TagColor c) => Color.FromArgb(255, c.R, c.G, c.B);

    public static Color ForegroundOn(TagColor c) =>
        c.PrefersDarkForeground ? Color.FromArgb(255, 0x1F, 0x1F, 0x1E) : Color.FromArgb(255, 0xFF, 0xFF, 0xFF);

    public static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        255,
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)));

    private static void Set(SolidColorBrush brush, byte a, byte r, byte g, byte b) => brush.Color = Color.FromArgb(a, r, g, b);
}

/// <summary>Shared layout measures.</summary>
internal static class Metrics
{
    /// <summary>
    /// Width of the time column left of the dotted line – identical in the editor and in history so both
    /// dotted lines are one vertical line.
    /// </summary>
    public const double TimeGutter = 100;

    /// <summary>Gap between the time and the dotted line.</summary>
    public const double TimeGutterGap = 9;
}

/// <summary>Corner radii: surfaces are square; buttons and tags are only slightly rounded.</summary>
internal static class Radius
{
    public static CornerRadius None { get; } = new(0);

    public static CornerRadius Control { get; } = new(3);

    public static CornerRadius Tag { get; } = new(4);
}
