namespace WolfMark.Core.Markdown;

public enum MarkdownTokenKind : byte
{
    /// <summary>Delimiter characters (#, **, `, [, ](…), &gt;) – dimmed in raw mode, hidden in formatted mode.</summary>
    Syntax,
    Heading1,
    Heading2,
    Heading3,
    Heading4,
    Heading5,
    Heading6,
    Strong,
    Emphasis,
    Strikethrough,
    InlineCode,
    LinkText,
    LinkUrl,
    Image,
    WikiLink,
    AutoLink,
    ListMarker,
    TaskOpen,
    TaskDone,
    TaskDoneText,
    Quote,
    CodeFence,
    CodeBlock,
    HorizontalRule,
    Hashtag,
    FrontMatter,
}

/// <summary>A styled range within one line. Tokens may overlap; later tokens refine earlier ones.</summary>
public readonly record struct MarkdownToken(int Start, int Length, MarkdownTokenKind Kind)
{
    public int End => Start + Length;
}

public enum LineStateKind : byte
{
    /// <summary>First line of the document – front matter may start here.</summary>
    DocumentStart,
    Normal,
    FrontMatter,
    FencedCode,
}

/// <summary>Lexer state at a line boundary (needed for fenced code blocks and front matter).</summary>
public readonly record struct LineState(LineStateKind Kind, char FenceChar = '\0', int FenceLength = 0)
{
    public static LineState DocumentStart { get; } = new(LineStateKind.DocumentStart);

    public static LineState Normal { get; } = new(LineStateKind.Normal);
}
