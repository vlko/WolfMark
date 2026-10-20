using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WolfMark.Core.Diagnostics;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor.Win2D;

/// <summary>
/// Markdown editor drawn with Win2D (docs/adr/0007-canvas-editor.md): one <see cref="MarkdownDocumentModel"/> is
/// both the text and its tokens, every line has its own DirectWrite layout, and only the lines in view are drawn.
/// Unlike RichEdit nothing scales with the note: an edit retokenizes and re-lays out the lines it touched.
/// Prototype – no spell check, IME composition, images, line numbers or accessibility yet.
/// </summary>
public sealed partial class CanvasMarkdownEditor : UserControl, IMarkdownEditor
{
    private readonly CanvasVirtualControl _canvas;
    private readonly ScrollViewer _scroll;
    private readonly DispatcherQueue _dispatcher;
    private readonly UndoHistory _undo = new();
    private readonly HashSet<EditorCommand> _activeFormats = [];

    private MarkdownDocumentModel _doc = new();
    private MarkdownTheme _theme = MarkdownTheme.Light(_ => default);
    private MarkdownEditorMode _mode = MarkdownEditorMode.Formatted;
    private bool _readOnly;
    private bool _hasFocus;
    private bool _moreBelow;

    // Selection in source offsets: the caret moves, the anchor stays (equal when nothing is selected).
    private int _caret;
    private int _anchor;
    private double _preferredX = -1; // x kept while moving up and down

    public CanvasMarkdownEditor()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _canvas = new CanvasVirtualControl
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            ClearColor = Microsoft.UI.Colors.Transparent,
            UseSharedDevice = true, // the layouts come from the shared device too
            IsTabStop = true,
            UseSystemFocusVisuals = false,
        };
        _canvas.RegionsInvalidated += OnRegionsInvalidated;
        _canvas.CreateResources += (_, _) =>
        {
            ClearLayouts(); // a new device: layouts made on the old one cannot be drawn
            _canvas.Invalidate();
        };

        _scroll = new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ZoomMode = ZoomMode.Disabled,
            IsTabStop = false,
        };
        _scroll.SizeChanged += (_, _) => OnViewportResized();
        _scroll.ViewChanged += (_, _) => UpdateOverflow();
        Content = _scroll;
        IsTabStop = false;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.IBeam);

        InitInput();
        InitIme();
        InitCaretBlink();
        Load(string.Empty, caretToEnd: false, readOnly: false);
    }

    public event EventHandler<string>? TextEdited;

    public event EventHandler? EditorLostFocus;

    public event EventHandler<MarkdownEditorMode>? ModeChanged;

    public event EventHandler<bool>? OverflowChanged;

    public event EventHandler<IReadOnlySet<EditorCommand>>? ActiveFormatsChanged;

    public event EventHandler? SpellCheckSuspendedChanged
    {
        add { }
        remove { }
    }

    public FrameworkElement View => this;

    public string Text => _doc.GetText();

    public MarkdownEditorMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value)
            {
                return;
            }

            _mode = value;
            RelayoutAll();
            ModeChanged?.Invoke(this, value);
        }
    }

    public MarkdownTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            RecreateFormat();
            RelayoutAll();
        }
    }

    public bool AlignLeft { get; set; }

    /// <summary>Not drawn yet by this prototype.</summary>
    public bool ShowLineNumbers { get; set; }

    public string PlaceholderText { get; set; } = string.Empty;

    public IReadOnlySet<EditorCommand> ActiveFormats => _activeFormats;

    public Func<EditorCommand, string>? CommandText { get; set; }

    public Func<EditorCommand, IconElement?>? CommandIcon { get; set; }

    /// <summary>Kept for the toolbar; this prototype does not proof.</summary>
    public bool IsSpellCheckEnabled { get; set; }

    public bool IsSpellCheckSuspended => false;

    public Func<ReadOnlyMemory<byte>, string, Task<string?>>? ImagePasteHandler { get; set; }

    public string DropImageCaption { get; set; } = "Insert image";

    public string OpenImageTip { get; set; } = "Double-click to open";

    public string? PreviewCacheDirectory { get; set; }

    public string? NotesRoot { get; set; }

    public string? NoteDirectory { get; set; }

    private bool HasSelection => _caret != _anchor;

    private (int Start, int End) Selection => (Math.Min(_caret, _anchor), Math.Max(_caret, _anchor));

    /// <summary>Replaces the whole document (opening a note). Clears undo history.</summary>
    public void Load(string text, bool caretToEnd, bool readOnly)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        _readOnly = readOnly;
        _doc = new MarkdownDocumentModel();
        _doc.Update(text.Replace("\r\n", "\n", StringComparison.Ordinal));
        _undo.Clear();
        _caret = _anchor = caretToEnd ? _doc.Length : 0;
        _preferredX = -1;
        ResetLines();
        RelayoutAll(keepView: false);
        EditorTrace.Current?.RecordSince(EditorPhase.LoadFormatted, started, _doc.LineCount, () => $"canvas, {_doc.Length} chars, {_doc.LineCount} lines, {_mode}");
        EditorTrace.Current?.Mark($"editor: canvas loaded ({_doc.Length} chars)");
        RefreshActiveFormats();
    }

    public void FocusEditor() => _canvas.Focus(FocusState.Programmatic);

    public void ScrollToStart()
    {
        SetCaret(0, extend: false);
        _scroll.ChangeView(null, 0, null, disableAnimation: true);
    }

    public void ScrollToEnd() => _scroll.ChangeView(null, _scroll.ScrollableHeight, null);

    public void ScrollToCaret() => _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, EnsureCaretVisible);

    /// <summary>Applies an editing command at the current selection (toolbar buttons call this).</summary>
    public void Execute(EditorCommand command)
    {
        if (command is EditorCommand.CopyPlainText or EditorCommand.CopyHtml)
        {
            CopySelection(cut: false);
            return;
        }

        if (_readOnly)
        {
            return;
        }

        var text = _doc.GetText();
        var (start, end) = Selection;
        var length = end - start;
        var edit = command switch
        {
            EditorCommand.Bold => MarkdownEditing.ToggleWrap(text, start, length, "**"),
            EditorCommand.Italic => MarkdownEditing.ToggleWrap(text, start, length, "*"),
            EditorCommand.Code => MarkdownEditing.ToggleWrap(text, start, length, "`"),
            EditorCommand.Strikethrough => MarkdownEditing.ToggleWrap(text, start, length, "~~"),
            EditorCommand.Link => MarkdownEditing.InsertLink(text, start, length),
            EditorCommand.Task => MarkdownEditing.GetBlock(text, start) == BlockKind.Task
                ? MarkdownEditing.SetBlock(text, start, BlockKind.Paragraph)
                : MarkdownEditing.SetBlock(text, start, BlockKind.Task),
            EditorCommand.Heading1 => MarkdownEditing.ToggleHeading(text, start, 1),
            EditorCommand.Heading2 => MarkdownEditing.ToggleHeading(text, start, 2),
            EditorCommand.Heading3 => MarkdownEditing.ToggleHeading(text, start, 3),
            EditorCommand.Paragraph => MarkdownEditing.SetBlock(text, start, BlockKind.Paragraph),
            EditorCommand.Quote => MarkdownEditing.SetBlock(text, start, MarkdownEditing.GetBlock(text, start) == BlockKind.Quote ? BlockKind.Paragraph : BlockKind.Quote),
            EditorCommand.BulletList => MarkdownEditing.SetBlock(text, start, MarkdownEditing.GetBlock(text, start) == BlockKind.Bullet ? BlockKind.Paragraph : BlockKind.Bullet),
            EditorCommand.NumberedList => MarkdownEditing.SetBlock(text, start, MarkdownEditing.GetBlock(text, start) == BlockKind.Numbered ? BlockKind.Paragraph : BlockKind.Numbered),
            EditorCommand.Indent => MarkdownEditing.Indent(text, start, length, outdent: false),
            EditorCommand.Outdent => MarkdownEditing.Indent(text, start, length, outdent: true),
            _ => (TextEdit?)null,
        };

        if (edit is { } e)
        {
            _undo.Seal();
            ApplyEdit(e, recordUndo: true);
            _undo.Seal();
        }

        FocusEditor();
    }

    public void Undo()
    {
        if (!_readOnly && _undo.Undo() is { } edit)
        {
            ApplyEdit(edit, recordUndo: false);
        }
    }

    public void Redo()
    {
        if (!_readOnly && _undo.Redo() is { } edit)
        {
            ApplyEdit(edit, recordUndo: false);
        }
    }

    /// <summary>
    /// The one way text changes: the model replaces the range (retokenizing only what the edit reaches), the
    /// changed lines are laid out again, the selection lands where <paramref name="edit"/> puts it.
    /// </summary>
    private void ApplyEdit(TextEdit edit, bool recordUndo)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var before = _caret;
        var removed = Slice(edit.Start, edit.Length);
        var update = _doc.Replace(edit.Start, edit.Length, edit.Insert);
        if (recordUndo)
        {
            _undo.RecordEdit(edit.Start, removed, edit.Insert, before, edit.SelectionStart + edit.SelectionLength);
        }

        SpliceLines(update);
        _anchor = Math.Clamp(edit.SelectionStart, 0, _doc.Length);
        _caret = Math.Clamp(edit.SelectionStart + edit.SelectionLength, 0, _doc.Length);
        _preferredX = -1;
        AfterChange();
        EditorTrace.Current?.RecordSince(EditorPhase.CanvasEdit, started, update.LineCount);
        TraceArmFrame();
        TextEdited?.Invoke(this, _doc.GetText());
    }

    /// <summary>Source text of [start, start + length): from the line itself when it does not cross a line break.</summary>
    private string Slice(int start, int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        var line = _doc.GetLineAtOffset(start);
        var lineStart = _doc.GetLineStart(line);
        var text = _doc.GetLineText(line);
        return start - lineStart + length <= text.Length
            ? text.Substring(start - lineStart, length)
            : _doc.GetText().Substring(start, Math.Min(length, _doc.Length - start));
    }

    /// <summary>Caret moved or text changed: redraw, keep the caret in view, update the toolbar state.</summary>
    private void AfterChange()
    {
        RestartBlink();
        UpdateCanvasSize();
        _canvas.Invalidate();
        EnsureCaretVisible();
        RefreshActiveFormats();
        SyncIme();
    }

    private void SetCaret(int offset, bool extend)
    {
        _caret = Math.Clamp(offset, 0, _doc.Length);
        if (!extend)
        {
            _anchor = _caret;
        }

        _undo.Seal();
        AfterChange();
    }

    private void UpdateOverflow()
    {
        var more = _scroll.VerticalOffset < _scroll.ScrollableHeight - 1;
        if (more != _moreBelow)
        {
            _moreBelow = more;
            OverflowChanged?.Invoke(this, more);
        }
    }

    /// <summary>Formats at the caret for the toolbar (same rules as the RichEdit editor).</summary>
    private void RefreshActiveFormats()
    {
        var line = _doc.GetLineAtOffset(_caret);
        var column = _caret - _doc.GetLineStart(line);
        var text = _doc.GetLineText(line);
        var active = new HashSet<EditorCommand>();
        foreach (var t in _doc.GetTokens(line))
        {
            switch (t.Kind)
            {
                case MarkdownTokenKind.Heading1:
                    active.Add(EditorCommand.Heading1);
                    break;
                case MarkdownTokenKind.Heading2:
                    active.Add(EditorCommand.Heading2);
                    break;
                case MarkdownTokenKind.Heading3:
                    active.Add(EditorCommand.Heading3);
                    break;
                case MarkdownTokenKind.Quote:
                    active.Add(EditorCommand.Quote);
                    break;
                case MarkdownTokenKind.TaskOpen or MarkdownTokenKind.TaskDone:
                    active.Add(EditorCommand.Task);
                    break;
                case MarkdownTokenKind.ListMarker when t.Start < text.Length:
                    active.Add(char.IsAsciiDigit(text[t.Start]) ? EditorCommand.NumberedList : EditorCommand.BulletList);
                    break;
                case MarkdownTokenKind.Strong when column >= t.Start && column <= t.End:
                    active.Add(EditorCommand.Bold);
                    break;
                case MarkdownTokenKind.Emphasis when column >= t.Start && column <= t.End:
                    active.Add(EditorCommand.Italic);
                    break;
                case MarkdownTokenKind.InlineCode when column >= t.Start && column <= t.End:
                    active.Add(EditorCommand.Code);
                    break;
                case MarkdownTokenKind.Strikethrough when column >= t.Start && column <= t.End:
                    active.Add(EditorCommand.Strikethrough);
                    break;
                case MarkdownTokenKind.LinkText when column >= t.Start && column <= t.End:
                    active.Add(EditorCommand.Link);
                    break;
            }
        }

        if (active.Contains(EditorCommand.Task))
        {
            active.Remove(EditorCommand.BulletList);
            active.Remove(EditorCommand.NumberedList);
        }

        if (!active.SetEquals(_activeFormats))
        {
            _activeFormats.Clear();
            _activeFormats.UnionWith(active);
            ActiveFormatsChanged?.Invoke(this, _activeFormats);
        }
    }
}
