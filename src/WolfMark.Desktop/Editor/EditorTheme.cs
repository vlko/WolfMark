using Avalonia.Media;

namespace WolfMark.Desktop.Editor;

/// <summary>Markdown colours and fonts (same values as src/WolfMark.Editor/MarkdownTheme.cs).</summary>
internal sealed record EditorTheme
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

    public required Func<string, Color> TagColor { get; init; }

    public double FontSize { get; init; } = 15;

    public FontFamily Font { get; init; } = FontFamily.Default;

    public FontFamily MonoFont { get; init; } = new("Cascadia Mono, DejaVu Sans Mono, Menlo, monospace");

    public static EditorTheme Light(Func<string, Color> tagColor) => new()
    {
        Text = Color.FromRgb(0x23, 0x22, 0x20),
        Muted = Color.FromRgb(0xA9, 0xA3, 0x99),
        Heading = Color.FromRgb(0x16, 0x15, 0x13),
        Link = Color.FromRgb(0x2F, 0x6F, 0xDF),
        Code = Color.FromRgb(0x8A, 0x3F, 0x2A),
        CodeBackground = Color.FromRgb(0xF1, 0xEC, 0xE4),
        Quote = Color.FromRgb(0x6B, 0x66, 0x5E),
        TaskOpen = Color.FromRgb(0x2F, 0x6F, 0xDF),
        TaskDone = Color.FromRgb(0x9A, 0x95, 0x8C),
        TagColor = tagColor,
    };

    public static EditorTheme Dark(Func<string, Color> tagColor) => new()
    {
        Text = Color.FromRgb(0xEC, 0xE8, 0xE1),
        Muted = Color.FromRgb(0x7C, 0x77, 0x6F),
        Heading = Color.FromRgb(0xFA, 0xF7, 0xF2),
        Link = Color.FromRgb(0x7F, 0xAE, 0xFF),
        Code = Color.FromRgb(0xF0, 0xA9, 0x8A),
        CodeBackground = Color.FromRgb(0x33, 0x30, 0x2C),
        Quote = Color.FromRgb(0xB5, 0xAF, 0xA5),
        TaskOpen = Color.FromRgb(0x7F, 0xAE, 0xFF),
        TaskDone = Color.FromRgb(0x85, 0x80, 0x77),
        TagColor = tagColor,
    };

    public double HeadingSize(WolfMark.Core.Markdown.MarkdownTokenKind kind) => kind switch
    {
        WolfMark.Core.Markdown.MarkdownTokenKind.Heading1 => FontSize * 1.6,
        WolfMark.Core.Markdown.MarkdownTokenKind.Heading2 => FontSize * 1.35,
        WolfMark.Core.Markdown.MarkdownTokenKind.Heading3 => FontSize * 1.18,
        _ => FontSize * 1.05,
    };
}
