using Windows.UI;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor;

/// <summary>Colours and sizes used for syntax highlighting. Recreate on theme change.</summary>
public sealed class MarkdownTheme
{
    public required Color Text { get; init; }

    public required Color Muted { get; init; }

    public required Color Heading { get; init; }

    public required Color Link { get; init; }

    public required Color Code { get; init; }

    public required Color CodeBackground { get; init; }

    public required Color Quote { get; init; }

    public required Color TaskOpen { get; init; }

    public required Color TaskDone { get; init; }

    /// <summary>Hashtag colour by tag name (the tag colour from tags.json / palette).</summary>
    public required Func<string, Color> TagColor { get; init; }

    public string FontFamily { get; init; } = "Segoe UI Variable Text";

    public string MonoFontFamily { get; init; } = "Cascadia Mono, Consolas";

    /// <summary>The first family of a XAML fallback list – DirectWrite takes one family name.</summary>
    public static string ToFaceName(string family)
    {
        return family.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "Segoe UI";
    }

    /// <summary>Base size in device-independent pixels.</summary>
    public double FontSize { get; init; } = 15;

    public static MarkdownTheme Light(Func<string, Color> tagColor) => new()
    {
        Text = Color.FromArgb(255, 0x23, 0x22, 0x20),
        Muted = Color.FromArgb(255, 0xA9, 0xA3, 0x99),
        Heading = Color.FromArgb(255, 0x16, 0x15, 0x13),
        Link = Color.FromArgb(255, 0x2F, 0x6F, 0xDF),
        Code = Color.FromArgb(255, 0x8A, 0x3F, 0x2A),
        CodeBackground = Color.FromArgb(255, 0xF1, 0xEC, 0xE4),
        Quote = Color.FromArgb(255, 0x6B, 0x66, 0x5E),
        TaskOpen = Color.FromArgb(255, 0x2F, 0x6F, 0xDF),
        TaskDone = Color.FromArgb(255, 0x9A, 0x95, 0x8C),
        TagColor = tagColor,
    };

    public static MarkdownTheme Dark(Func<string, Color> tagColor) => new()
    {
        Text = Color.FromArgb(255, 0xEC, 0xE8, 0xE1),
        Muted = Color.FromArgb(255, 0x7C, 0x77, 0x6F),
        Heading = Color.FromArgb(255, 0xFA, 0xF7, 0xF2),
        Link = Color.FromArgb(255, 0x7F, 0xAE, 0xFF),
        Code = Color.FromArgb(255, 0xF0, 0xA9, 0x8A),
        CodeBackground = Color.FromArgb(255, 0x33, 0x30, 0x2C),
        Quote = Color.FromArgb(255, 0xB5, 0xAF, 0xA5),
        TaskOpen = Color.FromArgb(255, 0x7F, 0xAE, 0xFF),
        TaskDone = Color.FromArgb(255, 0x85, 0x80, 0x77),
        TagColor = tagColor,
    };
}
