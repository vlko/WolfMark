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
