using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Zapisnik.Core.Tags;

namespace Zapisnik.App.Theming;

/// <summary>
/// "Paper" design tokens (SPEC §5.1). Brushes are shared instances whose colours are updated in place
/// on theme change, so every element using them follows without rebinding.
/// </summary>
internal static class Palette
{
    public static SolidColorBrush WindowTint { get; } = new();

    public static SolidColorBrush Card { get; } = new();

    public static SolidColorBrush CardStroke { get; } = new();

    public static SolidColorBrush Ink { get; } = new();

    public static SolidColorBrush InkSecondary { get; } = new();

    public static SolidColorBrush InkTertiary { get; } = new();

    public static SolidColorBrush SubtleFill { get; } = new();

    public static SolidColorBrush SubtleFillHover { get; } = new();

    public static SolidColorBrush Accent { get; } = new();

    public static SolidColorBrush TodayFill { get; } = new();

    public static SolidColorBrush TodayInk { get; } = new();

    public static SolidColorBrush Divider { get; } = new();

    public static SolidColorBrush EmptyCell { get; } = new();

    public static SolidColorBrush Overlay { get; } = new();

    public static SolidColorBrush Transparent { get; } = new(Microsoft.UI.Colors.Transparent);

    public static bool IsDark { get; private set; }

    public static event EventHandler? Changed;

    public static void Apply(ElementTheme theme)
    {
        IsDark = theme == ElementTheme.Dark;
        if (IsDark)
        {
            Set(WindowTint, 0xD9, 0x1C, 0x1B, 0x19);
            Set(Card, 0xFF, 0x26, 0x24, 0x22);
            Set(CardStroke, 0x24, 0xFF, 0xFF, 0xFF);
            Set(Ink, 0xFF, 0xEC, 0xE8, 0xE1);
            Set(InkSecondary, 0xFF, 0xB0, 0xAA, 0xA0);
            Set(InkTertiary, 0xFF, 0x80, 0x7B, 0x72);
            Set(SubtleFill, 0xFF, 0x34, 0x31, 0x2D);
            Set(SubtleFillHover, 0xFF, 0x3F, 0x3B, 0x36);
            Set(Accent, 0xFF, 0x7F, 0xAE, 0xFF);
            Set(TodayFill, 0xFF, 0xED, 0xE8, 0xDF);
            Set(TodayInk, 0xFF, 0x1E, 0x24, 0x33);
            Set(Divider, 0xFF, 0x3A, 0x37, 0x33);
            Set(EmptyCell, 0xFF, 0x30, 0x2E, 0x2B);
            Set(Overlay, 0x99, 0x00, 0x00, 0x00);
        }
        else
        {
            Set(WindowTint, 0xD9, 0xEF, 0xEB, 0xE4);
            Set(Card, 0xFF, 0xFB, 0xFA, 0xF7);
            Set(CardStroke, 0x14, 0x00, 0x00, 0x00);
            Set(Ink, 0xFF, 0x23, 0x22, 0x20);
            Set(InkSecondary, 0xFF, 0x6E, 0x6A, 0x63);
            Set(InkTertiary, 0xFF, 0xA3, 0x9E, 0x95);
            Set(SubtleFill, 0xFF, 0xEC, 0xE8, 0xE1);
            Set(SubtleFillHover, 0xFF, 0xE2, 0xDD, 0xD4);
            Set(Accent, 0xFF, 0x2F, 0x6F, 0xDF);
            Set(TodayFill, 0xFF, 0x1E, 0x24, 0x33);
            Set(TodayInk, 0xFF, 0xFF, 0xFF, 0xFF);
            Set(Divider, 0xFF, 0xE6, 0xE1, 0xD9);
            Set(EmptyCell, 0xFF, 0xF0, 0xED, 0xE8);
            Set(Overlay, 0x66, 0x20, 0x1C, 0x16);
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static Color ToColor(TagColor c) => Color.FromArgb(255, c.R, c.G, c.B);

    public static Color ForegroundOn(TagColor c) =>
        c.PrefersDarkForeground ? Color.FromArgb(255, 0x23, 0x22, 0x20) : Color.FromArgb(255, 0xFF, 0xFF, 0xFF);

    private static void Set(SolidColorBrush brush, byte a, byte r, byte g, byte b) => brush.Color = Color.FromArgb(a, r, g, b);
}
