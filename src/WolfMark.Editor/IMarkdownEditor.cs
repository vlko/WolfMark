using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WolfMark.Editor;

/// <summary>
/// What the app uses of an editor: the RichEdit one (<see cref="MarkdownEditor"/>) or the Win2D one
/// (<see cref="Win2D.CanvasMarkdownEditor"/>, docs/adr/0007-canvas-editor.md).
/// </summary>
public interface IMarkdownEditor
{
    event EventHandler<string>? TextEdited;

    event EventHandler? EditorLostFocus;

    event EventHandler<MarkdownEditorMode>? ModeChanged;

    event EventHandler<bool>? OverflowChanged;

    event EventHandler<IReadOnlySet<EditorCommand>>? ActiveFormatsChanged;

    event EventHandler? SpellCheckSuspendedChanged;

    /// <summary>The control to place in the window.</summary>
    FrameworkElement View { get; }

    MarkdownEditorMode Mode { get; set; }

    MarkdownTheme Theme { get; set; }

    bool AlignLeft { get; set; }

    bool ShowLineNumbers { get; set; }

    string PlaceholderText { get; set; }

    IReadOnlySet<EditorCommand> ActiveFormats { get; }

    Func<EditorCommand, string>? CommandText { get; set; }

    Func<EditorCommand, IconElement?>? CommandIcon { get; set; }

    bool IsSpellCheckEnabled { get; set; }

    bool IsSpellCheckSuspended { get; }

    Func<ReadOnlyMemory<byte>, string, Task<string?>>? ImagePasteHandler { get; set; }

    string DropImageCaption { get; set; }

    string OpenImageTip { get; set; }

    string? PreviewCacheDirectory { get; set; }

    string? NotesRoot { get; set; }

    string? NoteDirectory { get; set; }

    void Load(string text, bool caretToEnd, bool readOnly);

    void Execute(EditorCommand command);

    void FocusEditor();

    void ScrollToStart();

    void ScrollToEnd();

    void ScrollToCaret();
}
