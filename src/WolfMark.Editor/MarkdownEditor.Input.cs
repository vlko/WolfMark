using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using WolfMark.Core.Diagnostics;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor;

/// <summary>Keyboard, characters, pointer and clipboard. Positions move over shown characters (hidden markers are skipped).</summary>
public sealed partial class MarkdownEditor
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _blink;
    private bool _caretOn = true;
    private bool _dragging;
    private long _keyDownStamp;
    private bool _frameArmed;
    private int _zoomWheel; // Ctrl+wheel delta not yet turned into a font size step
    private int _zoomSteps; // zoom steps waiting for RequestZoom's dispatch
    private bool _zoomReset;
    private bool _zoomQueued;

    private void InitInput()
    {
        _canvas.KeyDown += OnKeyDown;
        _canvas.PreviewKeyDown += OnPreviewKeyDown;
        _canvas.CharacterReceived += OnCharacterReceived;
        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerReleased += OnPointerReleased;
        _canvas.DoubleTapped += OnDoubleTapped;
        _canvas.RightTapped += OnRightTapped;
        _canvas.GotFocus += (_, _) =>
        {
            _hasFocus = true;
            ImeFocusEnter();
            RestartBlink();
            _canvas.Invalidate();
        };
        _canvas.LostFocus += (_, _) =>
        {
            _hasFocus = false;
            ImeFocusLeave();
            _blink?.Stop();
            _canvas.Invalidate();
            EditorLostFocus?.Invoke(this, EventArgs.Empty);
        };
    }

    private void InitCaretBlink()
    {
        var ms = Windows.Win32.PInvoke.GetCaretBlinkTime();
        _blink = _dispatcher.CreateTimer();
        _blink.Interval = TimeSpan.FromMilliseconds(ms is > 0 and < 5000 ? ms : 530);
        _blink.Tick += (_, _) =>
        {
            _caretOn = !_caretOn;
            InvalidateCaret();
        };
    }

    private void RestartBlink()
    {
        _caretOn = true;
        if (_hasFocus)
        {
            _blink?.Stop();
            _blink?.Start();
        }
    }

    private void InvalidateCaret()
    {
        var r = CaretRect(_caret);
        var region = new Rect(r.X - 2, r.Y - 1, r.Width + 4, r.Height + 2);

        // Only within the surface: after a relayout (zoom, a new note) the caret can be laid out below the canvas
        // before it has grown – Invalidate rejects a region outside it (E_INVALIDARG).
        region.Intersect(new Rect(0, 0, _canvas.ActualWidth, _canvas.ActualHeight));
        if (!region.IsEmpty && region.Width > 0 && region.Height > 0)
        {
            _canvas.Invalidate(region);
        }
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>Tab must be taken before focus navigation sees it.</summary>
    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Tab && !IsDown(VirtualKey.Control) && !_readOnly)
        {
            var (start, end) = Selection;
            if (MarkdownEditing.Indent(_doc.GetText(), start, end - start, outdent: IsDown(VirtualKey.Shift)) is { } edit)
            {
                Edit(edit);
            }

            e.Handled = true;
        }
    }

    /// <summary>The font size step of a Ctrl shortcut: +1 for "+" (or "="), −1 for "-", 0 for "0"; null otherwise.</summary>
    private static int? ZoomStep(VirtualKey key, bool shift)
    {
        switch (key)
        {
            case VirtualKey.Add:
                return 1;
            case VirtualKey.Subtract:
                return -1;
            case VirtualKey.Number0 or VirtualKey.NumberPad0:
                return 0;
        }

        return AltGrKeys.CharOf(key, shift) switch
        {
            '+' or '=' => 1,
            '-' => -1,
            _ => null,
        };
    }

    private void OnZoomWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDown(VirtualKey.Control))
        {
            return;
        }

        // Ctrl+wheel zooms instead of scrolling; a touchpad sends small deltas, a step is a mouse notch (120).
        e.Handled = true;
        _zoomWheel += e.GetCurrentPoint(_canvas).Properties.MouseWheelDelta;
        while (Math.Abs(_zoomWheel) >= 120)
        {
            var step = Math.Sign(_zoomWheel);
            _zoomWheel -= step * 120;
            RequestZoom(step);
        }
    }

    /// <summary>
    /// Zoom steps are summed and applied once the input is through: a fast wheel spin is one relayout, not one per
    /// notch inside the input and layout callbacks. Step 0 (Ctrl 0) resets the sum to "the default size".
    /// </summary>
    private void RequestZoom(int step)
    {
        if (step == 0)
        {
            _zoomReset = true;
            _zoomSteps = 0;
        }
        else
        {
            _zoomSteps += step;
        }

        if (_zoomQueued)
        {
            return;
        }

        _zoomQueued = _dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _zoomQueued = false;
            var (reset, steps) = (_zoomReset, _zoomSteps);
            (_zoomReset, _zoomSteps) = (false, 0);
            if (reset)
            {
                FontZoomRequested?.Invoke(this, new FontZoom(Reset: true, 0));
            }

            if (steps != 0)
            {
                FontZoomRequested?.Invoke(this, new FontZoom(Reset: false, steps));
            }
        });
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        _followCaret = false;
        if (_composing)
        {
            return; // the IME owns the keys while composing
        }

        _keyDownStamp = EditorTrace.Current is null ? 0 : Stopwatch.GetTimestamp();
        var ctrl = IsDown(VirtualKey.Control);
        var shift = IsDown(VirtualKey.Shift);
        var alt = IsDown(VirtualKey.Menu);
        if (ctrl && alt && AltGrKeys.IsTyping(e.Key, shift))
        {
            return; // AltGr (= Ctrl+Alt) makes a character on this layout: CharacterReceived types it
        }

        if (ctrl && !alt && ZoomStep(e.Key, shift) is { } step)
        {
            e.Handled = true;
            RequestZoom(step);
            return;
        }

        var handled = true;
        switch (e.Key)
        {
            case VirtualKey.Left:
                SetCaret(ctrl ? WordStep(_caret, forward: false) : Step(_caret, forward: false, shift), shift);
                break;
            case VirtualKey.Right:
                SetCaret(ctrl ? WordStep(_caret, forward: true) : Step(_caret, forward: true, shift), shift);
                break;
            case VirtualKey.Up:
                MoveVertically(-1, shift);
                break;
            case VirtualKey.Down:
                MoveVertically(1, shift);
                break;
            case VirtualKey.PageUp:
                MovePage(-1, shift);
                break;
            case VirtualKey.PageDown:
                MovePage(1, shift);
                break;
            case VirtualKey.Home:
                SetCaret(ctrl ? 0 : RowEdge(_caret, end: false), shift);
                break;
            case VirtualKey.End:
                SetCaret(ctrl ? _doc.Length : RowEdge(_caret, end: true), shift);
                break;
            case VirtualKey.Back when !_readOnly:
                Backspace(ctrl);
                break;
            case VirtualKey.Delete when !_readOnly:
                DeleteForward(ctrl);
                break;
            case VirtualKey.Enter when !_readOnly:
                Enter();
                break;
            case VirtualKey.A when ctrl:
                _anchor = 0;
                SetCaret(_doc.Length, extend: true);
                break;
            case VirtualKey.C when ctrl:
                CopySelection(cut: false);
                break;
            case VirtualKey.X when ctrl && !_readOnly:
                CopySelection(cut: true);
                break;
            case VirtualKey.V when ctrl && !_readOnly:
                _ = PasteAsync();
                break;
            case VirtualKey.Z when ctrl && !shift:
                Undo();
                break;
            case VirtualKey.Y when ctrl:
            case VirtualKey.Z when ctrl && shift:
                Redo();
                break;
            case VirtualKey.B when ctrl && !shift:
                RunCommand(EditorCommand.Bold);
                break;
            case VirtualKey.I when ctrl && !shift:
                RunCommand(EditorCommand.Italic);
                break;
            case VirtualKey.E when ctrl && !shift:
                Mode = _mode == MarkdownEditorMode.Raw ? MarkdownEditorMode.Formatted : MarkdownEditorMode.Raw;
                break;
            case VirtualKey.K when ctrl && !shift && HasSelection && !_readOnly:
                RunCommand(EditorCommand.Link); // without a selection Ctrl+K bubbles up to open search
                break;
            case VirtualKey.L when ctrl && shift && !_readOnly:
                Edit(MarkdownEditing.ToggleTask(_doc.GetText(), _caret));
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }

    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (_composing || _readOnly || e.Character < ' ' || IsDown(VirtualKey.Control) && !IsDown(VirtualKey.Menu))
        {
            return; // control characters come as keys; Ctrl shortcuts are not text
        }

        if (_keyDownStamp == 0 && EditorTrace.Current is not null)
        {
            _keyDownStamp = Stopwatch.GetTimestamp();
        }

        Insert(e.Character.ToString());
        _typingWord = char.IsLetterOrDigit(e.Character);
        e.Handled = true;
    }

    // ---------------------------------------------------------------- editing

    /// <summary>Replaces the selection with <paramref name="text"/>.</summary>
    private void Insert(string text)
    {
        var (start, end) = Selection;
        if (!HasSelection && text.Length > 0 && BlockAt(_caret) is { } block)
        {
            // Beside an image: the text gets a line of its own before or after it.
            Edit(_caret == block.Start
                ? new TextEdit(block.Start, 0, text + "\n", block.Start + text.Length, 0)
                : new TextEdit(block.End, 0, "\n" + text, block.End + 1 + text.Length, 0));
            return;
        }

        Edit(new TextEdit(start, end - start, text, start + text.Length, 0));
    }

    private void Edit(TextEdit edit) => ApplyEdit(edit, recordUndo: true);

    private void Enter()
    {
        _undo.Seal();
        var (start, end) = Selection;
        if (!HasSelection && BlockAt(_caret) is { } block)
        {
            // Before an image: an empty line above it, the caret stays before the image; after it: a new line below.
            Edit(_caret == block.Start
                ? new TextEdit(block.Start, 0, "\n", block.Start + 1, 0)
                : new TextEdit(block.End, 0, "\n", block.End + 1, 0));
        }
        else if (!HasSelection && MarkdownEditing.ContinueList(_doc.GetText(), _caret) is { } list)
        {
            Edit(list);
        }
        else
        {
            Edit(new TextEdit(start, end - start, "\n", start + 1, 0));
        }

        _undo.Seal();
    }

    private void Backspace(bool word)
    {
        if (HasSelection)
        {
            Insert(string.Empty);
            return;
        }

        if (_mode == MarkdownEditorMode.Formatted && MarkdownImageBlock.Backspace(_doc.GetText(), _caret, IsShownBlock) is { } blockEdit)
        {
            ApplyBlockEdit(blockEdit); // the image goes as a whole, or the caret steps over it
            return;
        }

        var (line, presentation, shown) = Locate(_caret);
        var lineStart = _doc.GetLineStart(line);
        if (word)
        {
            var target = WordStep(_caret, forward: false);
            Edit(new TextEdit(target, _caret - target, string.Empty, target, 0));
        }
        else if (shown > 0)
        {
            var (from, to) = presentation.SourceRangeOf(shown - 1, shown);
            Edit(new TextEdit(lineStart + from, to - from, string.Empty, lineStart + from, 0));
        }
        else if (presentation.ToSource(0) > 0)
        {
            // At the start of a heading, list item or quote: the hidden marker goes (the line becomes a paragraph).
            var hidden = presentation.ToSource(0);
            Edit(new TextEdit(lineStart, hidden, string.Empty, lineStart, 0));
        }
        else if (line > 0)
        {
            Edit(new TextEdit(lineStart - 1, 1, string.Empty, lineStart - 1, 0)); // joins the previous line
        }
    }

    private void DeleteForward(bool word)
    {
        if (HasSelection)
        {
            Insert(string.Empty);
            return;
        }

        if (_mode == MarkdownEditorMode.Formatted && MarkdownImageBlock.Delete(_doc.GetText(), _caret, IsShownBlock) is { } blockEdit)
        {
            ApplyBlockEdit(blockEdit);
            return;
        }

        var (line, presentation, shown) = Locate(_caret);
        var lineStart = _doc.GetLineStart(line);
        if (word)
        {
            var target = WordStep(_caret, forward: true);
            Edit(new TextEdit(_caret, target - _caret, string.Empty, _caret, 0));
        }
        else if (shown < presentation.Text.Length)
        {
            var (from, to) = presentation.SourceRangeOf(shown, shown + 1);
            Edit(new TextEdit(lineStart + from, to - from, string.Empty, lineStart + from, 0));
        }
        else if (line < _doc.LineCount - 1)
        {
            var end = lineStart + presentation.SourceLength;
            Edit(new TextEdit(end, 1, string.Empty, end, 0)); // joins the next line
        }
    }

    /// <summary>A block rule's result: a removal is an edit, an empty one only moves the caret.</summary>
    private void ApplyBlockEdit(TextEdit edit)
    {
        if (edit.Length == 0 && edit.Insert.Length == 0)
        {
            SetCaret(edit.SelectionStart, extend: false);
        }
        else
        {
            Edit(edit);
        }
    }

    // ---------------------------------------------------------------- caret movement

    /// <summary>Line, its presentation and the shown column of a source offset.</summary>
    private (int Line, LinePresentation Presentation, int Shown) Locate(int offset)
    {
        var line = _doc.GetLineAtOffset(offset);
        EnsureLayout(line);
        var presentation = _lines[line].Presentation!;
        return (line, presentation, presentation.ToShown(offset - _doc.GetLineStart(line)));
    }

    /// <summary>One shown character left or right; across line ends to the neighbouring line.</summary>
    private int Step(int offset, bool forward, bool extend)
    {
        if (HasSelection && !extend)
        {
            return forward ? Selection.End : Selection.Start; // collapse to the side the arrow points to
        }

        if (BlockAt(offset) is { } block && (forward ? offset == block.Start : offset == block.End))
        {
            return forward ? block.End : block.Start; // over the image in one step
        }

        var (line, presentation, shown) = Locate(offset);
        var lineStart = _doc.GetLineStart(line);
        if (forward)
        {
            if (shown < presentation.Text.Length)
            {
                return lineStart + presentation.ToSource(shown + 1);
            }

            return line + 1 < _doc.LineCount ? _doc.GetLineStart(line + 1) + Locate(_doc.GetLineStart(line + 1)).Presentation.ToSource(0) : offset;
        }

        if (shown > 0)
        {
            return lineStart + presentation.ToSource(shown - 1);
        }

        return line > 0 ? _doc.GetLineStart(line - 1) + _doc.GetLineText(line - 1).Length : offset;
    }

    /// <summary>To the next word start (forward) or the previous one (back), in shown text.</summary>
    private int WordStep(int offset, bool forward)
    {
        var (line, presentation, shown) = Locate(offset);
        var text = presentation.Text;
        var lineStart = _doc.GetLineStart(line);
        if (forward)
        {
            if (shown >= text.Length)
            {
                return Step(offset, forward: true, extend: true);
            }

            var i = shown;
            while (i < text.Length && char.IsLetterOrDigit(text[i]))
            {
                i++;
            }

            while (i < text.Length && !char.IsLetterOrDigit(text[i]))
            {
                i++;
            }

            return lineStart + presentation.ToSource(i == shown ? i + 1 : i);
        }

        if (shown == 0)
        {
            return Step(offset, forward: false, extend: true);
        }

        var j = shown;
        while (j > 0 && !char.IsLetterOrDigit(text[j - 1]))
        {
            j--;
        }

        while (j > 0 && char.IsLetterOrDigit(text[j - 1]))
        {
            j--;
        }

        return lineStart + presentation.ToSource(j);
    }

    /// <summary>Start or end of the visual row (a wrapped line has several).</summary>
    private int RowEdge(int offset, bool end)
    {
        var (line, presentation, shown) = Locate(offset);
        var layout = _lines[line].Layout!;
        var rowStart = 0;
        foreach (var row in layout.LineMetrics)
        {
            var rowEnd = rowStart + row.CharacterCount;
            if (shown < rowEnd || rowEnd >= presentation.Text.Length)
            {
                var target = end
                    ? rowEnd >= presentation.Text.Length ? presentation.Text.Length : Math.Max(rowStart, rowEnd - row.TrailingWhitespaceCount)
                    : rowStart;
                return _doc.GetLineStart(line) + presentation.ToSource(target);
            }

            rowStart = rowEnd;
        }

        return offset;
    }

    private void MoveVertically(int direction, bool extend)
    {
        var caret = CaretRect(_caret);
        if (_preferredX < 0)
        {
            _preferredX = caret.X;
        }

        var y = direction < 0 ? caret.Y - 2 : caret.Bottom + 2;
        if (BlockAt(_caret) is { } block)
        {
            // Beside an image the caret is as tall as the picture, but the line has a gap around it: step from the
            // line's own edge, or the point would land in the same line again.
            EnsureTops();
            y = direction < 0 ? LineTop(block.Line) - 1 : LineTop(block.Line) + LineHeight(block.Line) + 1;
        }

        var x = _preferredX;
        if (y < PadTop)
        {
            SetCaretKeepX(0, extend);
            return;
        }

        if (y > PadTop + LineTopEnd())
        {
            SetCaretKeepX(_doc.Length, extend);
            return;
        }

        SetCaretKeepX(OffsetAt(new Point(x, y)), extend);
    }

    private void MovePage(int direction, bool extend)
    {
        var caret = CaretRect(_caret);
        _preferredX = _preferredX < 0 ? caret.X : _preferredX;
        var page = Math.Max(40, _scroll.ViewportHeight - 40);
        var y = Math.Clamp(caret.Y + caret.Height / 2 + direction * page, PadTop, PadTop + LineTopEnd() - 1);
        _scroll.ChangeView(null, Math.Max(0, _scroll.VerticalOffset + direction * page), null, disableAnimation: true);
        SetCaretKeepX(OffsetAt(new Point(_preferredX, y)), extend);
    }

    private double LineTopEnd()
    {
        EnsureTops();
        return _totalHeight;
    }

    /// <summary>Moves the caret but keeps the remembered x for further up/down moves.</summary>
    private void SetCaretKeepX(int offset, bool extend)
    {
        var x = _preferredX;
        SetCaret(offset, extend);
        _preferredX = x;
    }

    // ---------------------------------------------------------------- pointer

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _followCaret = false;
        _keyDownStamp = 0; // an edit by click (task box, drop) is no key's latency; a stamp of an earlier arrow key stays out

        _canvas.Focus(FocusState.Pointer);
        var point = e.GetCurrentPoint(_canvas);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (TaskBoxAt(point.Position) is { } box)
        {
            // A click on a task's box toggles it (as in the desktop app); the caret and selection stay.
            var toggle = MarkdownEditing.ToggleTask(_doc.GetText(), box);
            _undo.Seal();
            Edit(toggle with { SelectionStart = _anchor, SelectionLength = _caret - _anchor });
            _undo.Seal();
            e.Handled = true;
            return;
        }

        SetCaret(OffsetAt(point.Position), extend: IsDown(VirtualKey.Shift));
        _dragging = _canvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    /// <summary>
    /// The source offset of the task box "[ ]" / "[x]" under a point – in formatted mode, or with Ctrl in raw mode;
    /// never in a read-only note.
    /// </summary>
    private int? TaskBoxAt(Point point)
    {
        if (_readOnly || (_mode != MarkdownEditorMode.Formatted && !IsDown(VirtualKey.Control)) || _lines.Count == 0)
        {
            return null;
        }

        var line = LineAtY(point.Y);
        if (IsBlock(line) || ShownTable(line) is not null)
        {
            return null;
        }

        var lineStart = _doc.GetLineStart(line);
        var column = OffsetAt(point) - lineStart;
        foreach (var t in _doc.GetTokens(line))
        {
            if (t.Kind is MarkdownTokenKind.TaskOpen or MarkdownTokenKind.TaskDone && column >= t.Start && column <= t.End)
            {
                return lineStart + t.Start;
            }
        }

        return null;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        UpdatePictureHover(e.GetCurrentPoint(_canvas).Position);
        if (_dragging)
        {
            SetCaret(OffsetAt(e.GetCurrentPoint(_canvas).Position), extend: true);
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            _canvas.ReleasePointerCapture(e.Pointer);
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TryOpenPictureAt(e.GetPosition(_canvas)) || TaskBoxAt(e.GetPosition(_canvas)) is not null)
        {
            return; // a double-click on a task box toggles it twice (each press), no word selection
        }

        var offset = OffsetAt(e.GetPosition(_canvas));
        var (line, presentation, shown) = Locate(offset);
        var text = presentation.Text;
        var from = shown;
        var to = shown;
        while (from > 0 && char.IsLetterOrDigit(text[from - 1]))
        {
            from--;
        }

        while (to < text.Length && char.IsLetterOrDigit(text[to]))
        {
            to++;
        }

        var lineStart = _doc.GetLineStart(line);
        _anchor = lineStart + presentation.ToSource(from);
        SetCaret(lineStart + presentation.ToSource(to), extend: true);
    }

    // ---------------------------------------------------------------- clipboard

    private enum CopyKind
    {
        Markdown,
        PlainText,
        Html,
    }

    private void CopySelection(bool cut) => _ = CopyAsync(CopyKind.Markdown, cut);

    /// <summary>
    /// Copy and cut give the Markdown, widened to the markup around the selection (hidden in formatted mode, it
    /// would be cut off otherwise). Plain text and HTML are converted from it – the selection, or the whole note
    /// when nothing is selected. Widening and conversion run off the UI thread.
    /// </summary>
    private async Task CopyAsync(CopyKind kind, bool cut)
    {
        if (!HasSelection && kind == CopyKind.Markdown)
        {
            return;
        }

        var text = _doc.GetText();
        var (start, end) = HasSelection ? Selection : (0, text.Length);
        var (from, to, payload, plain) = await Task.Run(() =>
        {
            var (s, e) = MarkdownEditing.ExpandToMarkup(text, start, end);
            var markdown = text[s..e];
            return kind switch
            {
                CopyKind.PlainText => (s, e, MarkdownConvert.ToPlainText(markdown), (string?)null),
                CopyKind.Html => (s, e, MarkdownConvert.ToHtml(markdown), MarkdownConvert.ToPlainText(markdown)),
                _ => (s, e, markdown, (string?)null),
            };
        });

        static string Crlf(string s) => s.Replace("\n", "\r\n", StringComparison.Ordinal);
        var package = new DataPackage();
        if (kind == CopyKind.Html)
        {
            package.SetHtmlFormat(HtmlFormatHelper.CreateHtmlFormat(payload));
            package.SetText(Crlf(plain!));
        }
        else
        {
            package.SetText(Crlf(payload));
        }

        try
        {
            Clipboard.SetContent(package);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return; // the clipboard is locked by another process
        }

        if (cut && !_readOnly && ReferenceEquals(text, _doc.GetText()))
        {
            _undo.Seal();
            Edit(new TextEdit(from, to - from, string.Empty, from, 0));
            _undo.Seal();
        }
    }

    private async Task PasteAsync()
    {
        var content = Clipboard.GetContent();
        string text;
        try
        {
            if (await PasteImageAsync(content) || !content.Contains(StandardDataFormats.Text))
            {
                return;
            }

            text = await content.GetTextAsync();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
        {
            return; // the clipboard is locked by another process
        }

        _undo.Seal();
        Insert(text.Replace("\r\n", "\n", StringComparison.Ordinal));
        _undo.Seal();
    }

    // ---------------------------------------------------------------- trace

    /// <summary>The edit is applied: the next frame shows it – that ends the key's latency.</summary>
    private void TraceArmFrame()
    {
        if (EditorTrace.Current is null || _keyDownStamp == 0 || _frameArmed)
        {
            return;
        }

        _frameArmed = true;
        CompositionTarget.Rendering += OnTraceFrame;
    }

    private void OnTraceFrame(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnTraceFrame;
        _frameArmed = false;
        EditorTrace.Current?.RecordSince(EditorPhase.KeyToFrame, _keyDownStamp, context: () => $"canvas, {_mode}, line {_doc.GetLineAtOffset(_caret) + 1} of {_doc.LineCount}");
        _keyDownStamp = 0;
    }
}
