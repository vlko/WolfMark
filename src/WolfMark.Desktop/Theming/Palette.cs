using Avalonia;
using Avalonia.Media;
using WolfMark.Core.Tags;

namespace WolfMark.Desktop.Theming;

/// <summary>
/// Design tokens of the Windows app (src/WolfMark.App/Theming/Palette.cs, ADR 0004): one flat paper colour, ink
/// tones, vivid tag colours. Shared brush instances, recoloured in place on theme change.
/// </summary>
internal static class Palette
{
    public static SolidColorBrush WindowTint { get; } = new();

    public static SolidColorBrush Ink { get; } = new();

    public static SolidColorBrush InkSecondary { get; } = new();

    public static SolidColorBrush InkTertiary { get; } = new();

    public static SolidColorBrush SubtleFill { get; } = new();

    public static SolidColorBrush SubtleFillHover { get; } = new();

    public static SolidColorBrush Accent { get; } = new();

    public static SolidColorBrush TodayFill { get; } = new();

    public static SolidColorBrush TodayInk { get; } = new();

    public static SolidColorBrush Divider { get; } = new();

    public static SolidColorBrush RisoPink { get; } = new();

    public static SolidColorBrush RisoYellow { get; } = new();

    public static SolidColorBrush EmptyCell { get; } = new();

    /// <summary>Grey of weekday bubbles and week-number tags.</summary>
    public static SolidColorBrush LabelGrey { get; } = new();

    public static SolidColorBrush LabelInk { get; } = new();

    /// <summary>Two-tone dashed frame of today (yellow under black dashes).</summary>
    public static SolidColorBrush TodayFrame { get; } = new();

    public static SolidColorBrush TodayFrameDash { get; } = new();

    public static SolidColorBrush Transparent { get; } = new(Colors.Transparent);

    /// <summary>Diagonal stripes laid over past days of the week band.</summary>
    public static LinearGradientBrush PastHatch { get; } = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
        EndPoint = new RelativePoint(5, 5, RelativeUnit.Absolute),
        SpreadMethod = GradientSpreadMethod.Repeat,
        GradientStops =
        {
            new GradientStop(Colors.Transparent, 0),
            new GradientStop(Colors.Transparent, 0.5),
            new GradientStop(Colors.Transparent, 0.5),
            new GradientStop(Colors.Transparent, 1),
        },
    };

    public static bool IsDark { get; private set; }

    public static event EventHandler? Changed;

    public static void Apply(bool dark)
    {
        IsDark = dark;
        if (dark)
        {
            Set(WindowTint, 0x1A, 0x1B, 0x1D);
            Set(Ink, 0xEE, 0xEC, 0xE7);
            Set(InkSecondary, 0xAA, 0xA7, 0xA0);
            Set(InkTertiary, 0x70, 0x6D, 0x68);
            Set(SubtleFill, 0x2A, 0x2C, 0x30);
            Set(SubtleFillHover, 0x35, 0x37, 0x3C);
            Set(Accent, 0x4F, 0xA3, 0xFF);
            Set(TodayFill, 0xEE, 0xEC, 0xE7);
            Set(TodayInk, 0x1A, 0x1B, 0x1D);
            Set(Divider, 0x34, 0x36, 0x3A);
            Set(RisoPink, 0xC2, 0x4F, 0x7D);
            Set(RisoYellow, 0xB8, 0x94, 0x2A);
            Set(EmptyCell, 0x25, 0x27, 0x2A);
            Set(LabelGrey, 0x46, 0x49, 0x4E);
            Set(LabelInk, 0xE6, 0xE6, 0xE6);
            Set(TodayFrame, 0xF6, 0xC6, 0x2F);
            Set(TodayFrameDash, 0x0E, 0x0F, 0x10);
        }
        else
        {
            Set(WindowTint, 0xFA, 0xF9, 0xF4);
            Set(Ink, 0x1F, 0x1F, 0x1E);
            Set(InkSecondary, 0x6B, 0x68, 0x62);
            Set(InkTertiary, 0xA8, 0xA4, 0x9B);
            Set(SubtleFill, 0xEF, 0xED, 0xE6);
            Set(SubtleFillHover, 0xE5, 0xE2, 0xD9);
            Set(Accent, 0x1E, 0x88, 0xF5);
            Set(TodayFill, 0x1F, 0x1F, 0x1E);
            Set(TodayInk, 0xFF, 0xFF, 0xFF);
            Set(Divider, 0xE4, 0xE0, 0xD6);
            Set(RisoPink, 0xF4, 0x9C, 0xBB);
            Set(RisoYellow, 0xF6, 0xD3, 0x5B);
            Set(EmptyCell, 0xEE, 0xEC, 0xE5);
            Set(LabelGrey, 0xC6, 0xC9, 0xCE);
            Set(LabelInk, 0xFF, 0xFF, 0xFF);
            Set(TodayFrame, 0xF6, 0xC6, 0x2F);
            Set(TodayFrameDash, 0x1F, 0x1F, 0x1E);
        }

        var hatch = dark ? Color.FromArgb(0x5C, 0x1A, 0x1B, 0x1D) : Color.FromArgb(0x6E, 0xFF, 0xFF, 0xFF);
        PastHatch.GradientStops[0].Color = hatch;
        PastHatch.GradientStops[1].Color = hatch;

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static Color ToColor(TagColor c) => Color.FromRgb(c.R, c.G, c.B);

    public static Color ForegroundOn(TagColor c) =>
        c.PrefersDarkForeground ? Color.FromRgb(0x1F, 0x1F, 0x1E) : Color.FromRgb(0xFF, 0xFF, 0xFF);

    public static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + ((b.R - a.R) * t)),
        (byte)(a.G + ((b.G - a.G) * t)),
        (byte)(a.B + ((b.B - a.B) * t)));

    private static void Set(SolidColorBrush brush, byte r, byte g, byte b) => brush.Color = Color.FromRgb(r, g, b);
}

/// <summary>Shared layout measures (same values as the Windows app).</summary>
internal static class Metrics
{
    /// <summary>Width of the time column left of the dotted line, in the editor and in history.</summary>
    public const double TimeGutter = 100;

    public const double TimeGutterGap = 9;
}
