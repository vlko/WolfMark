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
    private double _maxLineWidth = 760;

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
        _box.TextChanged += (_, _) => OnBoxTextChanged();
        _box.SelectionChanged += (_, _) => OnSelectionChanged();
        _box.Paste += OnPaste;
        _box.PreviewKeyDown += OnPreviewKeyDown;
        _box.LostFocus += (_, _) => EditorLostFocus?.Invoke(this, EventArgs.Empty);
        _box.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), handledEventsToo: true);
        SizeChanged += (_, _) =>
        {
            UpdatePadding();
            UpdateOverlay();
        };

        var host = new Grid();
        host.Children.Add(_box);
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

    /// <summary>Stores a pasted image (PNG bytes) and returns the markdown link target, or null.</summary>
    public Func<byte[], Task<string?>>? ImagePasteHandler { get; set; }

    public string Text => _text;

    public bool IsReadOnly
    {
        get => _box.IsReadOnly;
        set => _box.IsReadOnly = value;
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
            RequestHighlight(full: true);
            ModeChanged?.Invoke(this, value);
            UpdateOverlay();
        }
    }

    public double MaxLineWidth
    {
        get => _maxLineWidth;
        set
        {
            _maxLineWidth = value;
            UpdatePadding();
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
            RequestHighlight(full: true);
        }
    }

    public string PlaceholderText
    {
        get => _box.PlaceholderText;
        set => _box.PlaceholderText = value;
    }

    /// <summary>Replaces the whole document (opening a note). Clears undo history.</summary>
    public void Load(string text, bool caretToEnd)
    {
        text = text.Replace("\r\n", "\n");
        Interlocked.Increment(ref _applying);
        try
        {
            var wasReadOnly = _box.IsReadOnly;
            _box.IsReadOnly = false;
            _box.TextDocument.SetText(TextSetOptions.None, text);
            _box.IsReadOnly = wasReadOnly;
            ResetAllFormatting();
            var caret = caretToEnd ? text.Length : 0;
            _box.TextDocument.Selection.SetRange(caret, caret);
        }
        finally
        {
            Interlocked.Decrement(ref _applying);
        }

        _text = ReadBoxText();
        _undo.Clear();
        _lineTokens.Clear();
        _activeLine = LineOf(_text, caretToEnd ? _text.Length : 0);
        RequestHighlight(full: true);
    }

    public void FocusEditor() => _box.Focus(FocusState.Programmatic);

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
        if (_box.IsReadOnly)
        {
            return;
        }

        var wantsToggle = _mode == MarkdownEditorMode.Formatted || IsDown(VirtualKey.Control);
        if (!wantsToggle)
        {
            return;
        }

        var point = e.GetCurrentPoint(_box).Position;
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
            if (content.Contains(StandardDataFormats.Bitmap) && ImagePasteHandler is { } handler)
            {
                var reference = await content.GetBitmapAsync();
                using var stream = await reference.OpenReadAsync();
                var png = await ImageEncoding.ToPngAsync(stream);
                var link = await handler(png);
                if (link is not null)
                {
                    InsertAtSelection($"![]({link})");
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
        _undo.Seal();
        ApplyEdit(new TextEdit(start, length, text, start + text.Length, 0), recordUndo: true);
        _undo.Seal();
    }

    // ------------------------------------------------------------------ text model

    private void OnBoxTextChanged()
    {
        if (Volatile.Read(ref _applying) > 0)
        {
            return;
        }

        var newText = ReadBoxText();
        if (string.Equals(newText, _text, StringComparison.Ordinal))
        {
            return; // formatting-only change
        }

        var old = _text;
        _text = newText;
        var (caret, _) = GetSelection();
        _undo.Record(old, newText, Math.Min(caret, old.Length), caret);
        _activeLine = LineOf(_text, caret);
        RequestHighlight(full: false);
        TextEdited?.Invoke(this, newText);
    }

    private void ApplyEdit(TextEdit edit, bool recordUndo)
    {
        var old = _text;
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
        if (recordUndo)
        {
            _undo.Record(old, _text, edit.Start, edit.SelectionStart);
        }

        _activeLine = LineOf(_text, edit.SelectionStart);
        RequestHighlight(full: false);
        _box.TextDocument.Selection.ScrollIntoView(PointOptions.None);
        TextEdited?.Invoke(this, _text);
    }

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
        var (caret, _) = GetSelection();
        var line = LineOf(_text, caret);
        if (line != _activeLine)
        {
            _undo.Seal();
            _activeLine = line;
        }

        // Markers stay hidden; the caret line only gets overlay chips and toolbar state.
        RefreshCaretState();
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

        if (AlignLeft)
        {
            _box.Padding = new Thickness(18, 6, Math.Max(24, width - _maxLineWidth - 18), 24);
            return;
        }

        var side = Math.Max(24, (width - _maxLineWidth) / 2);
        _box.Padding = new Thickness(side, 12, side, 48);
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

    private void RequestHighlight(bool full, int extraLine = -1)
    {
        var version = Interlocked.Increment(ref _version);
        _snapshots.Writer.TryWrite(new Snapshot(version, _text, _activeLine, extraLine, _mode, full));
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
                var extras = new HashSet<int>();
                while (_snapshots.Reader.TryRead(out var s))
                {
                    latest = s;
                    full |= s.Full;
                    if (s.ExtraLine >= 0)
                    {
                        extras.Add(s.ExtraLine);
                    }
                }

                if (latest is null)
                {
                    continue;
                }

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

    private void DispatchStyles(Snapshot snapshot, List<LineStyle> styles, int from)
    {
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (snapshot.Version != Volatile.Read(ref _version))
            {
                return; // stale – a newer snapshot is on its way
            }

            var to = Math.Min(from + LinesPerBatch, styles.Count);
            var readOnly = _box.IsReadOnly;
            Interlocked.Increment(ref _applying);
            try
            {
                // RichEdit refuses format changes on a read-only document.
                _box.IsReadOnly = false;
                _box.TextDocument.BatchDisplayUpdates();
                for (var i = from; i < to; i++)
                {
                    ApplyLineStyle(styles[i], snapshot.Mode, styles[i].Line == _activeLine);
                    CacheLine(styles[i]);
                }
            }
            finally
            {
                _box.TextDocument.ApplyDisplayUpdates();
                _box.IsReadOnly = readOnly;
                Interlocked.Decrement(ref _applying);
            }

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
    }

    private void ResetFormat(ITextCharacterFormat f)
    {
        f.Size = _theme.BasePoints;
        f.Name = _theme.FontFamily;
        f.ForegroundColor = _theme.Text;
        f.BackgroundColor = Microsoft.UI.Colors.Transparent;
        f.Bold = FormatEffect.Off;
        f.Italic = FormatEffect.Off;
        f.Strikethrough = FormatEffect.Off;
        f.Underline = UnderlineType.None;
        f.Hidden = FormatEffect.Off;
    }

    private void ApplyLineStyle(LineStyle style, MarkdownEditorMode mode, bool isActiveLine)
    {
        var doc = _box.TextDocument;
        if (style.Start + style.Length > _text.Length)
        {
            return;
        }

        var lineRange = doc.GetRange(style.Start, style.Start + style.Length);
        ResetFormat(lineRange.CharacterFormat);
        // Formatted mode hides delimiters on every line – revealing them would shift the text (ADR 0001).
        var hideSyntax = mode == MarkdownEditorMode.Formatted;

        foreach (var token in style.Tokens)
        {
            if (token.Length <= 0)
            {
                continue;
            }

            var f = doc.GetRange(style.Start + token.Start, style.Start + token.End).CharacterFormat;
            switch (token.Kind)
            {
                case MarkdownTokenKind.Heading1 or MarkdownTokenKind.Heading2 or MarkdownTokenKind.Heading3
                    or MarkdownTokenKind.Heading4 or MarkdownTokenKind.Heading5 or MarkdownTokenKind.Heading6:
                    f.Size = _theme.HeadingPoints(token.Kind);
                    f.Bold = FormatEffect.On;
                    f.ForegroundColor = _theme.Heading;
                    break;
                case MarkdownTokenKind.Syntax:
                    f.ForegroundColor = _theme.Muted;
                    if (hideSyntax)
                    {
                        f.Hidden = FormatEffect.On;
                    }

                    break;
                case MarkdownTokenKind.Strong:
                    f.Bold = FormatEffect.On;
                    break;
                case MarkdownTokenKind.Emphasis:
                    f.Italic = FormatEffect.On;
                    break;
                case MarkdownTokenKind.Strikethrough:
                    f.Strikethrough = FormatEffect.On;
                    break;
                case MarkdownTokenKind.InlineCode or MarkdownTokenKind.CodeBlock:
                    f.Name = _theme.MonoFontFamily;
                    f.ForegroundColor = _theme.Code;
                    f.BackgroundColor = _theme.CodeBackground;
                    break;
                case MarkdownTokenKind.CodeFence or MarkdownTokenKind.FrontMatter:
                    f.Name = _theme.MonoFontFamily;
                    f.ForegroundColor = _theme.Muted;
                    f.Size = _theme.BasePoints * 0.9f;
                    break;
                case MarkdownTokenKind.LinkText or MarkdownTokenKind.WikiLink or MarkdownTokenKind.AutoLink:
                    f.ForegroundColor = _theme.Link;
                    f.Underline = UnderlineType.Single;
                    break;
                case MarkdownTokenKind.LinkUrl:
                    f.ForegroundColor = _theme.Muted;
                    if (hideSyntax)
                    {
                        f.Hidden = FormatEffect.On;
                    }

                    break;
                case MarkdownTokenKind.Image:
                    f.ForegroundColor = _theme.Link;
                    break;
                case MarkdownTokenKind.ListMarker:
                    f.ForegroundColor = _theme.Muted;
                    f.Bold = FormatEffect.On;
                    break;
                case MarkdownTokenKind.TaskOpen:
                    f.ForegroundColor = _theme.TaskOpen;
                    f.Bold = FormatEffect.On;
                    f.Name = _theme.MonoFontFamily;
                    break;
                case MarkdownTokenKind.TaskDone:
                    f.ForegroundColor = _theme.TaskDone;
                    f.Bold = FormatEffect.On;
                    f.Name = _theme.MonoFontFamily;
                    break;
                case MarkdownTokenKind.TaskDoneText:
                    f.ForegroundColor = _theme.TaskDone;
                    f.Strikethrough = FormatEffect.On;
                    break;
                case MarkdownTokenKind.Quote:
                    f.ForegroundColor = _theme.Quote;
                    f.Italic = FormatEffect.On;
                    break;
                case MarkdownTokenKind.HorizontalRule:
                    f.ForegroundColor = _theme.Muted;
                    break;
                case MarkdownTokenKind.Hashtag:
                    f.ForegroundColor = _theme.TagColor(style.Text.Substring(token.Start + 1, token.Length - 1));
                    f.Bold = FormatEffect.On;
                    break;
            }
        }
    }

    private sealed record Snapshot(int Version, string Text, int ActiveLine, int ExtraLine, MarkdownEditorMode Mode, bool Full);
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
