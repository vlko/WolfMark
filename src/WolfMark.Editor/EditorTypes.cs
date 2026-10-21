namespace WolfMark.Editor;

public enum MarkdownEditorMode
{
    Raw,
    Formatted,
}

/// <summary>Editing commands of the toolbar, shortcuts and context menu.</summary>
public enum EditorCommand
{
    Bold,
    Italic,
    Code,
    Strikethrough,
    Link,
    Task,
    Indent,
    Outdent,
    Paragraph,
    Heading1,
    Heading2,
    Heading3,
    Quote,
    BulletList,
    NumberedList,
    CopyPlainText,
    CopyHtml,
}

/// <summary>A font size change asked for in the editor: back to the default size, or whole steps up (+) or down (−).</summary>
public readonly record struct FontZoom(bool Reset, int Steps);
