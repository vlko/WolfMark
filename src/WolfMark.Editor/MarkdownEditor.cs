using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.UI.Dispatching;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using VirtualKey = Windows.System.VirtualKey;
using Windows.UI.Core;
using WolfMark.Core.Diagnostics;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor;

public enum MarkdownEditorMode
{
    Raw,
    Formatted,
}

/// <summary>
/// Markdown editor (SPEC §4.4) on top of <see cref="RichEditBox"/>. One plain-text model; highlighting is
/// computed by an incremental line lexer on a background thread and applied in small batches on the
/// UI thread, so typing never waits for it. Native RichEdit undo is disabled (formatting would pollute
/// it); <see cref="UndoHistory"/> tracks text edits instead. See docs/adr/0001-editor.md.
/// </summary>
public sealed partial class MarkdownEditor : UserControl
{
    private const int LinesPerBatch = 150;

    private readonly RichEditBox _box;
    private readonly DispatcherQueue _dispatcher;
    private readonly UndoHistory _undo = new();
    private readonly Channel<Snapshot> _snapshots = Channel.CreateUnbounded<Snapshot>(new UnboundedChannelOptions { SingleReader = true });
    private readonly MarkdownDocumentModel _model = new(); // owned by the highlighter loop
    private readonly CancellationTokenSource _cts = new();

    private MarkdownTheme _theme;
    private MarkdownEditorMode _mode = MarkdownEditorMode.Formatted;
    private string _text = string.Empty;
    private int _version;
    private int _activeLine = -1;
    private int _appliedVersion;
    private int _applying;
    private bool _readOnly;
    private bool _spellCheck = true;
    private bool _contentChanging; // TextChanging reported a content (not formatting-only) change
    private int _storyLength = -1; // RichEdit's length at the last text read
    private bool _selectionPending; // a caret move arrived before its text change (see OnSelectionChanged)

    public MarkdownEditor()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _theme = MarkdownTheme.Light(_ => default);

        _box = new RichEditBox
        {
            AcceptsReturn = true,
            IsSpellCheckEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            DisabledFormattingAccelerators = DisabledFormattingAccelerators.All,
            ClipboardCopyFormat = RichEditClipboardFormat.PlainText,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(32, 12, 32, 48),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_box, ScrollBarVisibility.Auto);

        // Lightweight styling: no box, no focus underline – the editor is the page.
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBackgroundDisabled", "TextControlBorderBrush", "TextControlBorderBrushPointerOver",
                     "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled",
                 })
        {
            _box.Resources[key] = transparent;
        }

        _box.TextDocument.UndoLimit = 0;
        _box.TextChanging += (_, e) => _contentChanging |= e.IsContentChanging;
        _box.TextChanged += (_, _) => OnBoxTextChanged();
        _box.SelectionChanged += (_, _) => OnSelectionChanged();
        _box.Paste += OnPaste;
        _box.PreviewKeyDown += OnPreviewKeyDown;
        _box.LostFocus += (_, _) =>
        {
            UpdateImageCaret();
            EditorLostFocus?.Invoke(this, EventArgs.Empty);
        };
        _box.GotFocus += (_, _) => UpdateImageCaret();
        _box.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), handledEventsToo: true);
        SizeChanged += (_, _) =>
        {
            UpdatePadding();
            OnImageColumnResized();
            UpdateOverlay();
            ScheduleImageLayout();
            ScheduleLineNumbers();
        };
        InitImages();
        InitLineNumbers();

        var host = new Grid();
        host.Children.Add(_box);
        host.Children.Add(_lineNumbers);
        host.Children.Add(_images);
        host.Children.Add(_overlay);
        Content = host;
        InitContextMenu();
        InitClipboard();
        IsTabStop = false;
        Unloaded += (_, _) => _cts.Cancel();
        _box.Loaded += (_, _) =>
        {
            _box.TextDocument.UndoLimit = 0; // the template may reset it
            HookScrollViewer();
        };

        _ = Task.Run(() => HighlightLoopAsync(_cts.Token));
    }

    /// <summary>Raised on the UI thread after every user edit with the full new text.</summary>
    public event EventHandler<string>? TextEdited;

    public event EventHandler? EditorLostFocus;

    public event EventHandler<MarkdownEditorMode>? ModeChanged;

    /// <summary>True when text continues below the visible area (raised on the UI thread).</summary>
    public event EventHandler<bool>? OverflowChanged;

    public bool HasMoreBelow { get; private set; }

    /// <summary>
    /// Left-aligned text with a fixed small left margin (used next to the time gutter) instead of a
    /// centred column.
    /// </summary>
    public bool AlignLeft { get; set; }

    /// <summary>
    /// Stores a pasted or dropped image (bytes, extension such as ".png") in the note's assets and returns the
    /// markdown link target, or null.
    /// </summary>
    public Func<ReadOnlyMemory<byte>, string, Task<string?>>? ImagePasteHandler { get; set; }

    public string Text => _text;

    public bool IsReadOnly
    {
        get => _readOnly;
        set
        {
            if (_readOnly == value)
            {
                return;
            }

            _readOnly = value;
            _box.IsReadOnly = value;
            if (ApplySpellCheck())
            {
                Reproof();
            }
        }
    }

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
            // The whole note restyles: one RTF set like opening it. Per-range formats in batches of 150 lines
            // took seconds on long notes (RichEdit re-lays out after every batch).
            RequestHighlight(full: true, load: true);
            ModeChanged?.Invoke(this, value);
            UpdateOverlay();
        }
    }

    public MarkdownTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            _box.FontFamily = new FontFamily(value.FontFamily);
            _box.FontSize = value.FontSize;
            _box.Foreground = new SolidColorBrush(value.Text);
            UpdatePadding(); // the line-number gutter follows the font size
            RequestHighlight(full: true, load: true); // see Mode
        }
    }

    public string PlaceholderText
    {
        get => _box.PlaceholderText;
        set => _box.PlaceholderText = value;
    }

    /// <summary>Replaces the whole document (opening a note). Clears undo history.</summary>
    public void Load(string text, bool caretToEnd, bool readOnly)
    {
        text = text.Replace("\r\n", "\n");
        Interlocked.Increment(ref _applying);
        try
        {
            _readOnly = readOnly;
            _box.IsReadOnly = false;
            // The old text is never replaced while the spell checker works on it (a WinUIEdit crash): proofing
            // is off until the formatted text replaces this plain one (LoadFormatted), and comes back – for an
            // editable note only – with that.
            _box.IsSpellCheckEnabled = false;
            ResetImages();
            // New text takes the format at the start of the old note – often a hidden "# " marker, which made
            // the whole note hidden until the reset below; placing the caret in it then crashed WinUIEdit.dll
            // (access violation). Start from an empty, plainly formatted document instead.
            _box.TextDocument.SetText(TextSetOptions.None, string.Empty);
            ResetAllFormatting(); // while editable: RichEdit refuses format changes on a read-only document
            _box.TextDocument.SetText(TextSetOptions.None, text);
            _text = ReadBoxText();
            _storyLength = StoryLength();
            _lineTokens.Clear();
            var caret = caretToEnd ? _text.Length : 0;
            _activeLine = LineOf(_text, caret);
            _box.TextDocument.Selection.SetRange(caret, caret);
        }
        finally
        {
            _box.IsReadOnly = readOnly;
            Interlocked.Decrement(ref _applying);
        }

        _undo.Clear();
        RequestHighlight(full: true, load: true);
        HideUntilStyled();
    }

    private DispatcherQueueTimer? _revealTimer;

    /// <summary>
    /// A freshly loaded note is plain text until the first style batch lands; keep it invisible until then, so
    /// opening a note does not flash raw Markdown. A timer is the fallback if no batch comes.
    /// </summary>
    private void HideUntilStyled()
    {
        _box.Opacity = 0;
        _revealTimer ??= CreateRevealTimer();
        _revealTimer.Start();
    }

    private DispatcherQueueTimer CreateRevealTimer()
    {
        var timer = _dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(400);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => RevealAfterLoad();
        return timer;
    }

    private void RevealAfterLoad()
    {
        _revealTimer?.Stop();
        if (_box.Opacity < 1)
        {
            _box.Opacity = 1;
        }
    }

    /// <summary>
    /// RichEdit only proofs text typed after spell check was switched on; putting the same text back makes it
    /// check the whole note. Selection, scroll position and our undo history stay as they were.
    /// </summary>
    private void Reproof()
    {
        var selection = _box.TextDocument.Selection;
        var (start, end) = (selection.StartPosition, selection.EndPosition);
        var offset = _scrollViewer?.VerticalOffset;
        Interlocked.Increment(ref _applying);
        try
        {
            _box.IsReadOnly = false;
            _box.IsSpellCheckEnabled = false; // see Load: comes back with the formatted text
            _box.TextDocument.SetText(TextSetOptions.None, string.Empty);
            ResetAllFormatting();
            _box.TextDocument.SetText(TextSetOptions.None, _text);
            _box.TextDocument.Selection.SetRange(start, end);
        }
        finally
        {
            _box.IsReadOnly = _readOnly;
            Interlocked.Decrement(ref _applying);
        }

        _lineTokens.Clear();
        RequestHighlight(full: true, load: true);
        if (offset is { } y)
        {
            _scrollViewer?.ChangeView(null, y, null, disableAnimation: true);
        }
    }

    public void FocusEditor() => _box.Focus(FocusState.Programmatic);

    /// <summary>Caret and view to the start of the note (the README screenshots show a note from its title).</summary>
    public void ScrollToStart()
    {
        _box.TextDocument.Selection.SetRange(0, 0);
        _scrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
    }

    public void ScrollToCaret()
    {
        _box.TextDocument.Selection.ScrollIntoView(PointOptions.None);
    }

    /// <summary>Applies an editing command at the current selection (toolbar buttons call this).</summary>
    public void Execute(EditorCommand command)
    {
        if (_box.IsReadOnly && command is not (EditorCommand.CopyPlainText or EditorCommand.CopyHtml))
        {
            return;
        }

        if (command is EditorCommand.CopyPlainText or EditorCommand.CopyHtml)
        {
            _ = CopyAsync(command == EditorCommand.CopyHtml ? CopyKind.Html : CopyKind.PlainText, cut: false);
            return;
        }

        var (start, length) = GetSelection();
        var unwrap = length == 0 && _activeFormats.Contains(command) ? UnwrapAtCaret(command) : null;
        TextEdit? edit = unwrap ?? command switch
        {
            EditorCommand.Bold => MarkdownEditing.ToggleWrap(_text, start, length, "**"),
            EditorCommand.Italic => MarkdownEditing.ToggleWrap(_text, start, length, "*"),
            EditorCommand.Code => MarkdownEditing.ToggleWrap(_text, start, length, "`"),
            EditorCommand.Strikethrough => MarkdownEditing.ToggleWrap(_text, start, length, "~~"),
            EditorCommand.Link => MarkdownEditing.InsertLink(_text, start, length),
            EditorCommand.Task => MarkdownEditing.GetBlock(_text, start) == BlockKind.Task
                ? MarkdownEditing.SetBlock(_text, start, BlockKind.Paragraph)
                : MarkdownEditing.SetBlock(_text, start, BlockKind.Task),
            EditorCommand.Heading1 => MarkdownEditing.ToggleHeading(_text, start, 1),
            EditorCommand.Heading2 => MarkdownEditing.ToggleHeading(_text, start, 2),
            EditorCommand.Heading3 => MarkdownEditing.ToggleHeading(_text, start, 3),
            EditorCommand.Paragraph => MarkdownEditing.SetBlock(_text, start, BlockKind.Paragraph),
            EditorCommand.Quote => MarkdownEditing.SetBlock(_text, start, MarkdownEditing.GetBlock(_text, start) == BlockKind.Quote ? BlockKind.Paragraph : BlockKind.Quote),
            EditorCommand.BulletList => MarkdownEditing.SetBlock(_text, start, MarkdownEditing.GetBlock(_text, start) == BlockKind.Bullet ? BlockKind.Paragraph : BlockKind.Bullet),
            EditorCommand.NumberedList => MarkdownEditing.SetBlock(_text, start, MarkdownEditing.GetBlock(_text, start) == BlockKind.Numbered ? BlockKind.Paragraph : BlockKind.Numbered),
            EditorCommand.Indent => MarkdownEditing.Indent(_text, start, length, outdent: false),
            EditorCommand.Outdent => MarkdownEditing.Indent(_text, start, length, outdent: true),
            _ => null,
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
        if (_undo.Undo() is { } edit)
        {
            ApplyEdit(edit, recordUndo: false);
        }
    }

    public void Redo()
    {
        if (_undo.Redo() is { } edit)
        {
            ApplyEdit(edit, recordUndo: false);
        }
    }

    // ------------------------------------------------------------------ input

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        TraceKeyDown();
        var ctrl = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);
        var alt = IsDown(VirtualKey.Menu);
        if (alt)
        {
            return;
        }

        var (start, length) = GetSelection();
        TextEdit? edit = null;
        var handled = true;

        switch (e.Key)
        {
            case VirtualKey.Z when ctrl && !shift:
                Undo();
                break;
            case VirtualKey.Z when ctrl && shift:
            case VirtualKey.Y when ctrl:
                Redo();
                break;
            case VirtualKey.E when ctrl && !shift:
                Mode = Mode == MarkdownEditorMode.Raw ? MarkdownEditorMode.Formatted : MarkdownEditorMode.Raw;
                break;
            case VirtualKey.B when ctrl && !shift && !_box.IsReadOnly:
                Execute(EditorCommand.Bold); // same toggle logic as the toolbar (unwraps when active)
                break;
            case VirtualKey.I when ctrl && !shift && !_box.IsReadOnly:
                Execute(EditorCommand.Italic);
                break;
            case VirtualKey.K when ctrl && !shift && length > 0 && !_box.IsReadOnly:
                // With a selection Ctrl+K creates a link; without one it bubbles up to open search.
                edit = MarkdownEditing.InsertLink(_text, start, length);
                break;
            case VirtualKey.L when ctrl && shift && !_box.IsReadOnly:
                edit = MarkdownEditing.ToggleTask(_text, start);
                break;
            case VirtualKey.Enter when !ctrl && !shift && length == 0 && !_box.IsReadOnly:
                edit = MarkdownEditing.ContinueList(_text, start);
                handled = edit is not null;
                if (!handled)
                {
                    _undo.Seal();
                }

                break;
            case VirtualKey.Back when !ctrl && length == 0 && !_box.IsReadOnly && _mode == MarkdownEditorMode.Formatted:
                edit = MarkdownImageBlock.Backspace(_text, start, IsCollapsedImageLine);
                handled = edit is not null;
                break;
            case VirtualKey.Delete when !ctrl && length == 0 && !_box.IsReadOnly && _mode == MarkdownEditorMode.Formatted:
                edit = MarkdownImageBlock.Delete(_text, start, IsCollapsedImageLine);
                handled = edit is not null;
                break;
            case VirtualKey.Right when !ctrl && !shift && length == 0 && ImageEdgeStep(start, forward: true) is { } after:
                _box.TextDocument.Selection.SetRange(after, after); // over the image in one step
                break;
            case VirtualKey.Left when !ctrl && !shift && length == 0 && ImageEdgeStep(start, forward: false) is { } before:
                _box.TextDocument.Selection.SetRange(before, before);
                break;
            case VirtualKey.Tab when !ctrl && !_box.IsReadOnly:
                edit = MarkdownEditing.Indent(_text, start, length, outdent: shift);
                handled = edit is not null;
                break;
            default:
                handled = false;
                break;
        }

        if (edit is { } textEdit)
        {
            _undo.Seal();
            ApplyEdit(textEdit, recordUndo: true);
            _undo.Seal();
        }

        e.Handled = handled;
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_box).Position;
        if (IsDown(VirtualKey.Control) && TryOpenImageAt(point))
        {
            return; // works in read-only notes too
        }

        if (_box.IsReadOnly)
        {
            return;
        }

        var wantsToggle = _mode == MarkdownEditorMode.Formatted || IsDown(VirtualKey.Control);
        if (!wantsToggle)
        {
            return;
        }

        var range = _box.TextDocument.GetRangeFromPoint(point, PointOptions.ClientCoordinates);
        var pos = range.StartPosition;
        var (lineStart, lineEnd) = MarkdownEditing.LineAt(_text, pos);
        var line = _text.AsSpan(lineStart, lineEnd - lineStart);
        var indent = line.Length - line.TrimStart(' ').Length;
        var marker = MarkdownLineLexer.ListMarkerLength(line, indent);
        if (marker == 0)
        {
            return;
        }

        var box = lineStart + indent + marker;
        if (box + 3 <= lineEnd && _text[box] == '[' && _text[box + 2] == ']' && pos >= box && pos <= box + 3)
        {
            var (selStart, selLength) = GetSelection();
            var edit = MarkdownEditing.ToggleTask(_text, box);
            _undo.Seal();
            ApplyEdit(edit with { SelectionStart = selStart, SelectionLength = selLength }, recordUndo: true);
            _undo.Seal();
        }
    }

    private async void OnPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true; // always paste plain text / images ourselves
        if (_box.IsReadOnly)
        {
            return;
        }

        try
        {
            var content = Clipboard.GetContent();
            // Image files copied in Explorer are copied into the assets like dropped ones.
            if (content.Contains(StandardDataFormats.StorageItems)
                && await InsertImageFilesAsync(await content.GetStorageItemsAsync(), at: null))
            {
                return;
            }

            if (content.Contains(StandardDataFormats.Bitmap) && ImagePasteHandler is { } handler)
            {
                var reference = await content.GetBitmapAsync();
                using var stream = await reference.OpenReadAsync();
                var png = await ImageEncoding.ToPngAsync(stream);
                var link = await handler(png, ".png");
                if (link is not null)
                {
                    InsertImageLinks($"![]({link})", at: null);
                }

                return;
            }

            if (content.Contains(StandardDataFormats.Text))
            {
                var text = (await content.GetTextAsync()).Replace("\r\n", "\n").Replace('\r', '\n');
                InsertAtSelection(text);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
        {
            // Clipboard can be locked by another process; ignore this paste.
        }
    }

    private void InsertAtSelection(string text)
    {
        var (start, length) = GetSelection();
        var old = _text;
        _undo.Seal();
        ApplyEdit(new TextEdit(start, length, text, start + text.Length, 0), recordUndo: true);
        if (_mode == MarkdownEditorMode.Formatted && MarkdownImageBlock.KeepOnOwnLine(old, _text, IsCollapsedImageLine) is { } ownLine)
        {
            ApplyEdit(ownLine, recordUndo: true); // pasted beside an image: on a line of its own
            RestyleImageLines();
        }

        _undo.Seal();
    }

    // ------------------------------------------------------------------ text model

    private void OnBoxTextChanged()
    {
        OnBoxTextChangedCore();
        if (_selectionPending && Volatile.Read(ref _applying) == 0)
        {
            _selectionPending = false;
            OnSelectionChanged();
        }
    }

    private void OnBoxTextChangedCore()
    {
        if (Volatile.Read(ref _applying) > 0)
        {
            return;
        }

        // RichEdit raises TextChanged for its own formatting changes too (~20 per style batch); copying out the
        // whole note for each blocked typing in long notes. The length check guards a content change that did
        // not come through TextChanging.
        var content = _contentChanging;
        _contentChanging = false;
        var length = StoryLength();
        if (!content && length == _storyLength)
        {
            return; // formatting-only change
        }

        _storyLength = length;
        using var traced = EditorTrace.Current?.Measure(EditorPhase.TextChanged, () => $"content {content}, {_text.Length} chars");
        string newText;
        using (EditorTrace.Current?.Measure(EditorPhase.ReadText))
        {
            newText = ReadBoxText();
        }

        if (string.Equals(newText, _text, StringComparison.Ordinal))
        {
            return; // formatting-only change
        }

        TraceArmFrame();
        var old = _text;
        _text = newText;
        var (caret, _) = GetSelection();
        using (EditorTrace.Current?.Measure(EditorPhase.UndoRecord))
        {
            _undo.Record(old, newText, Math.Min(caret, old.Length), caret);
        }
        if (_mode == MarkdownEditorMode.Formatted && MarkdownImageBlock.KeepOnOwnLine(old, newText, IsCollapsedImageLine) is { } ownLine)
        {
            ApplyEdit(ownLine, recordUndo: true); // typed beside an image: the text gets a line of its own
            RestyleImageLines();
            return;
        }

        _activeLine = LineOf(_text, caret);
        PlainAtLineStart(caret);
        RequestHighlight(full: false);
        using (EditorTrace.Current?.Measure(EditorPhase.TextEditedHandler))
        {
            TextEdited?.Invoke(this, newText);
        }
    }

    private void ApplyEdit(TextEdit edit, bool recordUndo)
    {
        var old = _text;
        var offset = _scrollViewer?.VerticalOffset;
        Interlocked.Increment(ref _applying);
        try
        {
            var range = _box.TextDocument.GetRange(edit.Start, edit.Start + edit.Length);
            range.SetText(TextSetOptions.None, edit.Insert);
            _box.TextDocument.Selection.SetRange(edit.SelectionStart, edit.SelectionStart + edit.SelectionLength);
        }
        finally
        {
            Interlocked.Decrement(ref _applying);
        }

        _text = ReadBoxText();
        _storyLength = StoryLength();
        if (recordUndo)
        {
            _undo.Record(old, _text, edit.Start, edit.SelectionStart);
        }

        _activeLine = LineOf(_text, edit.SelectionStart);
        RequestHighlight(full: false);
        KeepView(offset);
        TextEdited?.Invoke(this, _text);
    }

    /// <summary>
    /// Enter at the end of a heading gives the new line break the heading's format, and text typed at the start
    /// of the next line would take it until the restyle catches up. The insertion format of a caret at a line
    /// start is reset to the base format (the lexer formats the line properly right after).
    /// </summary>
    private void PlainAtLineStart(int caret)
    {
        if (caret == 0 || _text[caret - 1] != '\n' || GetSelection().Length > 0)
        {
            return;
        }

        Interlocked.Increment(ref _applying);
        try
        {
            var selection = _box.TextDocument.Selection;
            var f = selection.CharacterFormat.GetClone();
            ResetFormat(f);
            selection.CharacterFormat = f;
        }
        finally
        {
            Interlocked.Decrement(ref _applying);
        }
    }

    /// <summary>
    /// After an edit (toolbar format, list continuation…): the view stays at <paramref name="offset"/> and scrolls
    /// only when the caret is outside it. <c>ScrollIntoView</c> alone moved long notes to their last line. Checked
    /// after RichEdit's layout of the edit.
    /// </summary>
    private void KeepView(double? offset)
    {
        if (offset is not { } y || _scrollViewer is not { } sv)
        {
            _box.TextDocument.Selection.ScrollIntoView(PointOptions.None);
            return;
        }

        sv.ChangeView(null, y, null, disableAnimation: true);
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            var selection = _box.TextDocument.Selection;
            _rectQueries++;
            selection.GetRect(PointOptions.ClientCoordinates, out var rect, out _);
            var top = ScrollShift() + rect.Top + sv.VerticalOffset; // in document coordinates
            if (top >= y && top + rect.Height <= y + sv.ViewportHeight)
            {
                sv.ChangeView(null, y, null, disableAnimation: true);
            }
            else
            {
                selection.ScrollIntoView(PointOptions.None);
            }
        });
    }

    private int StoryLength() => _box.TextDocument.GetRange(0, int.MaxValue).EndPosition;

    private string ReadBoxText()
    {
        _box.TextDocument.GetText(TextGetOptions.UseLf, out var text);
        return text;
    }

    private (int Start, int Length) GetSelection()
    {
        var sel = _box.TextDocument.Selection;
        var start = Math.Clamp(sel.StartPosition, 0, _text.Length);
        var end = Math.Clamp(sel.EndPosition, start, _text.Length);
        return (start, end - start);
    }

    private void OnSelectionChanged()
    {
        // Typing moves the caret before TextChanged delivers the text: measured against the old text the caret
        // at a line end counts as the next line, and the line number highlight flickered. OnBoxTextChanged
        // comes next and runs this again.
        if (Volatile.Read(ref _applying) == 0 && StoryLength() != _storyLength)
        {
            _selectionPending = true;
            return;
        }

        using var traced = EditorTrace.Current?.Measure(EditorPhase.SelectionChanged);
        if (SnapCaretOutOfImage())
        {
            return; // the caret lands before or after the image; that change comes back here
        }

        var (caret, _) = GetSelection();
        var line = LineOf(_text, caret);
        if (line != _activeLine)
        {
            _undo.Seal();
            _activeLine = line;
            HighlightActiveLineNumber();
        }

        // Markers stay hidden; the caret line only gets overlay chips and toolbar state.
        RefreshCaretState();
        UpdateImageCaret();
    }

    private static int LineOf(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        return text.AsSpan(0, offset).Count('\n');
    }

    private void UpdatePadding()
    {
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }

        // The text uses the full width; long lines wrap at the right edge (marked in the line-number gutter).
        var gutter = LineNumberGutter;
        _box.Padding = AlignLeft
            ? new Thickness(18 + gutter, 6, 24, 24)
            : new Thickness(24 + gutter, 12, 24, 48);
    }

    public void ScrollToEnd()
    {
        _scrollViewer?.ChangeView(null, _scrollViewer.ScrollableHeight, null);
    }

    private ScrollViewer? _scrollViewer;

    private void HookScrollViewer()
    {
        if (_scrollViewer is not null || FindDescendant<ScrollViewer>(_box) is not { } sv)
        {
            return;
        }

        _scrollViewer = sv;
        sv.ViewChanged += (_, _) =>
        {
            UpdateOverflow();
            UpdateOverlay();
            ScrollImages();
            ScrollLineNumbers();
        };
        sv.RegisterPropertyChangedCallback(ScrollViewer.ScrollableHeightProperty, (_, _) => UpdateOverflow());
        UpdateOverflow();
    }

    private void UpdateOverflow()
    {
        var more = _scrollViewer is { } sv && sv.VerticalOffset < sv.ScrollableHeight - 1;
        if (more != HasMoreBelow)
        {
            HasMoreBelow = more;
            OverflowChanged?.Invoke(this, more);
        }
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------ highlighting

    /// <param name="load">The whole note is (re)formatted in one go (RTF): opening it, a mode or theme switch.</param>
    private void RequestHighlight(bool full, int extraLine = -1, bool load = false)
    {
        var version = Interlocked.Increment(ref _version);
        _snapshots.Writer.TryWrite(new Snapshot(version, _text, _activeLine, extraLine, _mode, full || load, load));
    }

    private async Task HighlightLoopAsync(CancellationToken ct)
    {
        // Lines dispatched for a version the UI has not finished applying (it drops stale batches).
        // They are carried forward through each edit so fast typing never leaves lines unstyled.
        var pending = new SortedSet<int>();
        var dispatchedVersion = 0;
        try
        {
            while (await _snapshots.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                // Coalesce: only the newest snapshot matters, but "full" requests stick.
                Snapshot? latest = null;
                var full = false;
                var load = false;
                var extras = new HashSet<int>();
                while (_snapshots.Reader.TryRead(out var s))
                {
                    latest = s;
                    full |= s.Full;
                    load |= s.Load;
                    if (s.ExtraLine >= 0)
                    {
                        extras.Add(s.ExtraLine);
                    }
                }

                if (latest is null)
                {
                    continue;
                }

                var lexStart = Stopwatch.GetTimestamp();
                var update = _model.Update(latest.Text);
                if (Volatile.Read(ref _appliedVersion) >= dispatchedVersion)
                {
                    pending.Clear();
                }

                var lines = new SortedSet<int>(extras);
                foreach (var old in pending)
                {
                    if (update.MapLine(old) is var mapped and >= 0)
                    {
                        lines.Add(mapped);
                    }
                }

                if (full)
                {
                    for (var i = 0; i < _model.LineCount; i++)
                    {
                        lines.Add(i);
                    }
                }
                else
                {
                    for (var i = update.FirstLine; i < update.FirstLine + update.LineCount; i++)
                    {
                        lines.Add(i);
                    }

                    if (latest.ActiveLine >= 0)
                    {
                        lines.Add(latest.ActiveLine);
                    }
                }

                var styles = new List<LineStyle>(lines.Count);
                foreach (var i in lines)
                {
                    if (i >= 0 && i < _model.LineCount)
                    {
                        var text = _model.GetLineText(i);
                        styles.Add(new LineStyle(i, _model.GetLineStart(i), text.Length, text, [.. _model.GetTokens(i)]));
                    }
                }

                EditorTrace.Current?.RecordSince(EditorPhase.LexerUpdate, lexStart, styles.Count, () => TraceDescribe(latest));
                if (load && styles.Count > 0)
                {
                    pending = [.. styles.Select(st => st.Line)];
                    dispatchedVersion = latest.Version;
                    DispatchLoad(latest, styles);
                    continue;
                }

                // Large updates (opening a note) style the lines around the caret first – that is what is
                // on screen – instead of from the top of a long note down.
                if (styles.Count > LinesPerBatch && latest.ActiveLine >= 0)
                {
                    var active = latest.ActiveLine;
                    styles.Sort((a, b) => Math.Abs(a.Line - active).CompareTo(Math.Abs(b.Line - active)));
                }

                if (styles.Count > 0)
                {
                    pending = [.. styles.Select(st => st.Line)];
                    dispatchedVersion = latest.Version;
                    DispatchStyles(latest, styles, 0);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>A freshly loaded note: formatted in one go, or – if RichEdit read the RTF differently – in batches.</summary>
    private void DispatchLoad(Snapshot snapshot, List<LineStyle> styles)
    {
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (snapshot.Version != Volatile.Read(ref _version))
            {
                // Typed meanwhile: the newer text gets the one-go formatting (and proofing) instead.
                RequestHighlight(full: true, load: true);
                return;
            }

            if (!LoadFormatted(snapshot, styles))
            {
                DispatchStyles(snapshot, styles, 0);
                return;
            }

            foreach (var style in styles)
            {
                CacheLine(style);
            }

            Volatile.Write(ref _appliedVersion, snapshot.Version);
            ScheduleImageLayout();
            ScheduleLineNumbers();
            RevealAfterLoad();
            RefreshCaretState();
        });
    }

    /// <summary>
    /// Replaces the plain text of a just loaded note with the same text formatted as RTF – one call instead of a
    /// range format per token. Checked: if RichEdit reads the text back differently, the plain text returns and
    /// false sends the note through the batches. Proofing is switched on over an empty document right before the
    /// one RTF set, so the checker sees only the final text and the long note is set once. Only when the read-back
    /// differs (rare) is the text replaced again – with proofing off first, as replacing text under a running check
    /// crashed WinUIEdit.dll.
    /// </summary>
    private bool LoadFormatted(Snapshot snapshot, List<LineStyle> styles)
    {
        using var traced = EditorTrace.Current?.Measure(
            EditorPhase.LoadFormatted,
            () => $"{snapshot.Text.Length} chars, {styles.Count} lines, {snapshot.Mode}, spell check {_spellCheck && !_readOnly}");
        var basis = BaseStyle();
        var rtf = new RtfDocument(basis);
        foreach (var style in styles.OrderBy(st => st.Line))
        {
            var chars = new CharStyle[style.Length];
            Array.Fill(chars, basis);
            StyleTokens(style, snapshot.Mode == MarkdownEditorMode.Formatted, (from, to) => new CharRangeFormat(chars, from, to));
            rtf.AddLine(style.Text, chars);
        }

        var doc = _box.TextDocument;
        var (start, end) = (doc.Selection.StartPosition, doc.Selection.EndPosition);
        var offset = _scrollViewer?.VerticalOffset;
        var ok = false;
        Interlocked.Increment(ref _applying);
        try
        {
            _box.IsReadOnly = false;
            _box.IsSpellCheckEnabled = false; // stops a check of the previous note before its text goes
            var proof = _spellCheck && !_readOnly;
            if (proof)
            {
                doc.SetText(TextSetOptions.None, string.Empty);
                ApplySpellCheck();
            }

            var candidate = rtf.Build(extraParagraph: false);
            doc.SetText(TextSetOptions.FormatRtf, candidate);
            var read = ReadBoxText();
            ok = string.Equals(read, snapshot.Text, StringComparison.Ordinal);
            if (!ok)
            {
                _box.IsSpellCheckEnabled = false;
                string? accepted = null;
                if (string.Equals(read + "\n", snapshot.Text, StringComparison.Ordinal))
                {
                    // Only a lost final line break is worth the second try.
                    candidate = rtf.Build(extraParagraph: true);
                    doc.SetText(TextSetOptions.FormatRtf, candidate);
                    if (string.Equals(ReadBoxText(), snapshot.Text, StringComparison.Ordinal))
                    {
                        accepted = candidate;
                    }
                }

                ok = accepted is not null;
                doc.SetText(TextSetOptions.None, string.Empty);
                if (!ok)
                {
                    ResetAllFormatting();
                }

                ApplySpellCheck();
                doc.SetText(ok ? TextSetOptions.FormatRtf : TextSetOptions.None, ok ? accepted : snapshot.Text);
            }

            if (ok)
            {
                // Image lines need paragraph space and collapsing – only a few, through the range calls.
                doc.BatchDisplayUpdates();
                foreach (var style in styles)
                {
                    if (style.Text.Contains("![", StringComparison.Ordinal))
                    {
                        ApplyLineStyle(style, snapshot.Mode, false);
                    }
                }

                doc.ApplyDisplayUpdates();
            }

            var length = _text.Length;
            doc.Selection.SetRange(Math.Clamp(start, 0, length), Math.Clamp(end, 0, length));
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // RTF refused: plain text back, formatted in batches.
            ok = false;
            _box.IsSpellCheckEnabled = false;
            doc.SetText(TextSetOptions.None, string.Empty);
            ResetAllFormatting();
            ApplySpellCheck();
            doc.SetText(TextSetOptions.None, snapshot.Text);
        }
        finally
        {
            _box.IsReadOnly = _readOnly;
            Interlocked.Decrement(ref _applying);
        }

        if (offset is { } y)
        {
            // Again after RichEdit has laid out the new text: until then the scroll range is not updated and the
            // view ended at the last line (mode switch).
            _scrollViewer?.ChangeView(null, y, null, disableAnimation: true);
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => _scrollViewer?.ChangeView(null, y, null, disableAnimation: true));
        }

        return ok;
    }

    private void DispatchStyles(Snapshot snapshot, List<LineStyle> styles, int from)
    {
        var queued = Stopwatch.GetTimestamp();
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            EditorTrace.Current?.RecordSince(EditorPhase.StyleQueueDelay, queued, context: () => $"batch from {from} of {styles.Count}, {TraceDescribe(snapshot)}");
            if (snapshot.Version != Volatile.Read(ref _version))
            {
                return; // stale – a newer snapshot is on its way
            }

            var to = Math.Min(from + LinesPerBatch, styles.Count);
            var batchStart = Stopwatch.GetTimestamp();
            Interlocked.Increment(ref _applying);
            try
            {
                // RichEdit refuses format changes on a read-only document.
                _box.IsReadOnly = false;
                _box.TextDocument.BatchDisplayUpdates();
                using (EditorTrace.Current?.Measure(EditorPhase.StyleFormats))
                {
                    for (var i = from; i < to; i++)
                    {
                        ApplyLineStyle(styles[i], snapshot.Mode, styles[i].Line == _activeLine);
                        CacheLine(styles[i]);
                    }
                }
            }
            finally
            {
                using (EditorTrace.Current?.Measure(EditorPhase.StyleDisplay))
                {
                    _box.TextDocument.ApplyDisplayUpdates();
                }

                _box.IsReadOnly = _readOnly;
                Interlocked.Decrement(ref _applying);
            }

            EditorTrace.Current?.RecordSince(EditorPhase.StyleBatch, batchStart, to - from, () => $"lines {from}-{to} of {styles.Count}, {TraceDescribe(snapshot)}");
            ScheduleImageLayout(); // reserved space above moves the previews below
            ScheduleLineNumbers();
            RevealAfterLoad();

            if (to < styles.Count)
            {
                DispatchStyles(snapshot, styles, to);
            }
            else
            {
                Volatile.Write(ref _appliedVersion, snapshot.Version);
                RefreshCaretState();
            }
        });
    }

    private void ResetAllFormatting()
    {
        var range = _box.TextDocument.GetRange(0, int.MaxValue);
        ResetFormat(range.CharacterFormat);
        range.ParagraphFormat.SpaceAfter = 0; // image previews; new text must not inherit the room
    }

    private void ResetFormat(ITextCharacterFormat f) => ResetFormat(new RangeFormat(f));

    private void ResetFormat(ICharFormatSink f)
    {
        f.Size = _theme.BasePoints;
        f.Name = _theme.FaceName;
        f.Foreground = _theme.Text;
        f.Background = Microsoft.UI.Colors.Transparent;
        f.Bold = false;
        f.Italic = false;
        f.Strikethrough = false;
        f.Underline = false;
        f.Hidden = false;
    }

    /// <summary>The format <see cref="ResetFormat(ICharFormatSink)"/> sets, for the RTF path.</summary>
    private CharStyle BaseStyle() => new(
        _theme.FaceName, RtfDocument.HalfPoints(_theme.BasePoints), _theme.Text, Microsoft.UI.Colors.Transparent,
        false, false, false, false, false);

    private void ApplyLineStyle(LineStyle style, MarkdownEditorMode mode, bool isActiveLine)
    {
        var doc = _box.TextDocument;
        if (style.Start + style.Length > _text.Length)
        {
            return;
        }

        var lineRange = doc.GetRange(style.Start, style.Start + style.Length);
        // With the paragraph mark: a collapsed image line shrinks it too, and it has to grow back.
        var withMark = doc.GetRange(style.Start, style.Start + style.Length + 1);
        if (ReserveImageSpace(style, lineRange, mode))
        {
            ResetFormat(withMark.CharacterFormat);
            CollapseImageLine(withMark);
            return;
        }

        // The final format of every character first (as for RTF), then one format set per run of equal
        // characters. Setting properties on a range one by one is a document change each – a dozen or more per
        // line, and each costs RichEdit relayout work (~130 ms per typed character in a 5,000-line note).
        var basis = BaseStyle();
        var chars = new CharStyle[style.Length];
        Array.Fill(chars, basis);
        // Formatted mode hides delimiters on every line – revealing them would shift the text (ADR 0001).
        StyleTokens(style, mode == MarkdownEditorMode.Formatted, (from, to) => new CharRangeFormat(chars, from, to));

        var template = withMark.CharacterFormat.GetClone();
        var i = 0;
        while (i < chars.Length)
        {
            var end = i + 1;
            while (end < chars.Length && chars[end] == chars[i])
            {
                end++;
            }

            SetRunFormat(doc.GetRange(style.Start + i, style.Start + end), template, chars[i]);
            i = end;
        }

        SetRunFormat(doc.GetRange(style.Start + style.Length, style.Start + style.Length + 1), template, basis); // paragraph mark
    }

    /// <summary>Applies <paramref name="style"/> to <paramref name="range"/> in one call through a detached clone.</summary>
    private static void SetRunFormat(ITextRange range, ITextCharacterFormat template, CharStyle style)
    {
        var f = template.GetClone();
        var sink = new RangeFormat(f);
        sink.Name = style.Name;
        f.Size = style.Size; // already in half points
        sink.Foreground = style.Foreground;
        sink.Background = style.Background;
        sink.Bold = style.Bold;
        sink.Italic = style.Italic;
        sink.Strikethrough = style.Strikethrough;
        sink.Underline = style.Underline;
        sink.Hidden = style.Hidden;
        range.CharacterFormat = f;
    }

    /// <summary>Formats of a line's tokens, in order (later tokens override); <paramref name="format"/> gives the target of a token's range within the line.</summary>
    private void StyleTokens(LineStyle style, bool hideSyntax, Func<int, int, ICharFormatSink> format)
    {
        foreach (var token in style.Tokens)
        {
            if (token.Length <= 0)
            {
                continue;
            }

            var f = format(token.Start, token.End);
            switch (token.Kind)
            {
                case MarkdownTokenKind.Heading1 or MarkdownTokenKind.Heading2 or MarkdownTokenKind.Heading3
                    or MarkdownTokenKind.Heading4 or MarkdownTokenKind.Heading5 or MarkdownTokenKind.Heading6:
                    f.Size = _theme.HeadingPoints(token.Kind);
                    f.Bold = true;
                    f.Foreground = _theme.Heading;
                    break;
                case MarkdownTokenKind.Syntax:
                    f.Foreground = _theme.Muted;
                    if (hideSyntax)
                    {
                        f.Hidden = true;
                    }

                    break;
                case MarkdownTokenKind.Strong:
                    f.Bold = true;
                    break;
                case MarkdownTokenKind.Emphasis:
                    f.Italic = true;
                    break;
                case MarkdownTokenKind.Strikethrough:
                    f.Strikethrough = true;
                    break;
                case MarkdownTokenKind.InlineCode or MarkdownTokenKind.CodeBlock:
                    f.Name = _theme.MonoFaceName;
                    f.Foreground = _theme.Code;
                    f.Background = _theme.CodeBackground;
                    break;
                case MarkdownTokenKind.CodeFence or MarkdownTokenKind.FrontMatter:
                    f.Name = _theme.MonoFaceName;
                    f.Foreground = _theme.Muted;
                    f.Size = _theme.BasePoints * 0.9f;
                    break;
                case MarkdownTokenKind.LinkText or MarkdownTokenKind.WikiLink or MarkdownTokenKind.AutoLink:
                    f.Foreground = _theme.Link;
                    f.Underline = true;
                    break;
                case MarkdownTokenKind.LinkUrl:
                    f.Foreground = _theme.Muted;
                    if (hideSyntax)
                    {
                        f.Hidden = true;
                    }

                    break;
                case MarkdownTokenKind.Image:
                    f.Foreground = _theme.Link;
                    break;
                case MarkdownTokenKind.ListMarker:
                    f.Foreground = _theme.Muted;
                    f.Bold = true;
                    break;
                case MarkdownTokenKind.TaskOpen:
                    f.Foreground = _theme.TaskOpen;
                    f.Bold = true;
                    f.Name = _theme.MonoFaceName;
                    break;
                case MarkdownTokenKind.TaskDone:
                    f.Foreground = _theme.TaskDone;
                    f.Bold = true;
                    f.Name = _theme.MonoFaceName;
                    break;
                case MarkdownTokenKind.TaskDoneText:
                    f.Foreground = _theme.TaskDone;
                    f.Strikethrough = true;
                    break;
                case MarkdownTokenKind.Quote:
                    f.Foreground = _theme.Quote;
                    f.Italic = true;
                    break;
                case MarkdownTokenKind.HorizontalRule:
                    f.Foreground = _theme.Muted;
                    break;
                case MarkdownTokenKind.Hashtag:
                    f.Foreground = _theme.TagColor(style.Text.Substring(token.Start + 1, token.Length - 1));
                    f.Bold = true;
                    break;
            }
        }
    }

    private sealed record Snapshot(int Version, string Text, int ActiveLine, int ExtraLine, MarkdownEditorMode Mode, bool Full, bool Load);
}

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
