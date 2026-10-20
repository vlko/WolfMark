using Windows.Foundation;
using Windows.UI.Text.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor.Win2D;

/// <summary>
/// Input method composition (IME, emoji panel) through <see cref="CoreTextEditContext"/>, which works in WinUI 3
/// desktop without a hidden TextBox (verified by WinUIRichEditor). The IME sees the caret's line in source
/// columns; plain typing still arrives as <c>CharacterReceived</c>, composed text as <c>TextUpdating</c>.
/// </summary>
public sealed partial class CanvasMarkdownEditor
{
    private CoreTextEditContext? _ime;
    private bool _composing;
    private bool _inImeUpdate;
    private (int Start, int Length)? _compositionRange; // source columns of the caret line, underlined
    private int _imeLine = -1;
    private string _imeText = string.Empty;

    private void InitIme()
    {
        try
        {
            var context = CoreTextServicesManager.GetForCurrentView().CreateEditContext();
            context.InputScope = CoreTextInputScope.Text;
            context.InputPaneDisplayPolicy = CoreTextInputPaneDisplayPolicy.Automatic;
            context.TextRequested += OnImeTextRequested;
            context.SelectionRequested += (_, e) => e.Request.Selection = ImeSelection();
            context.TextUpdating += OnImeTextUpdating;
            context.SelectionUpdating += OnImeSelectionUpdating;
            context.FormatUpdating += (_, e) =>
            {
                _compositionRange = (e.Range.StartCaretPosition, Math.Max(0, e.Range.EndCaretPosition - e.Range.StartCaretPosition));
                _canvas.Invalidate();
            };
            context.LayoutRequested += OnImeLayoutRequested;
            context.CompositionStarted += (_, _) => _composing = true;
            context.CompositionCompleted += (_, _) =>
            {
                _composing = false;
                _compositionRange = null;
                _undo.Seal();
                _canvas.Invalidate();
            };
            context.FocusRemoved += (_, _) =>
            {
                _composing = false;
                _compositionRange = null;
            };
            _ime = context;
        }
        catch (Exception)
        {
            _ime = null; // no text services: plain CharacterReceived input only
        }
    }

    private void ImeFocusEnter()
    {
        if (_ime is null)
        {
            return;
        }

        _ime.NotifyFocusEnter();
        RememberImeBuffer();
    }

    private void ImeFocusLeave()
    {
        _composing = false;
        _compositionRange = null;
        _ime?.NotifyFocusLeave();
    }

    private void RememberImeBuffer()
    {
        _imeLine = _doc.GetLineAtOffset(_caret);
        _imeText = _doc.GetLineText(_imeLine);
    }

    /// <summary>After our own edits and caret moves: the IME's copy of the caret line follows.</summary>
    private void SyncIme()
    {
        if (_ime is null || _inImeUpdate || !_hasFocus)
        {
            return;
        }

        var line = _doc.GetLineAtOffset(_caret);
        var text = _doc.GetLineText(line);
        if (line != _imeLine || !ReferenceEquals(text, _imeText))
        {
            _ime.NotifyTextChanged(new CoreTextRange { StartCaretPosition = 0, EndCaretPosition = _imeText.Length }, text.Length, ImeSelection());
            _imeLine = line;
            _imeText = text;
        }
        else
        {
            _ime.NotifySelectionChanged(ImeSelection());
        }
    }

    /// <summary>The selection in caret-line columns (a selection beyond the line is shown to the IME as the caret).</summary>
    private CoreTextRange ImeSelection()
    {
        var line = _doc.GetLineAtOffset(_caret);
        var lineStart = _doc.GetLineStart(line);
        var length = _doc.GetLineText(line).Length;
        var (start, end) = Selection;
        if (start < lineStart || end > lineStart + length)
        {
            start = end = _caret;
        }

        return new CoreTextRange { StartCaretPosition = start - lineStart, EndCaretPosition = end - lineStart };
    }

    private void OnImeTextRequested(CoreTextEditContext sender, CoreTextTextRequestedEventArgs e)
    {
        var text = _doc.GetLineText(_doc.GetLineAtOffset(_caret));
        var start = Math.Clamp(e.Request.Range.StartCaretPosition, 0, text.Length);
        var end = Math.Clamp(e.Request.Range.EndCaretPosition, start, text.Length);
        e.Request.Text = text[start..end];
    }

    private void OnImeTextUpdating(CoreTextEditContext sender, CoreTextTextUpdatingEventArgs e)
    {
        if (_readOnly)
        {
            e.Result = CoreTextTextUpdatingResult.Failed;
            return;
        }

        _inImeUpdate = true;
        _keyDownStamp = 0; // composed text is not a key's latency (the emoji panel waits for the user's pick)
        try
        {
            var line = _doc.GetLineAtOffset(_caret);
            var lineStart = _doc.GetLineStart(line);
            var length = _doc.GetLineText(line).Length;
            var start = Math.Clamp(e.Range.StartCaretPosition, 0, length);
            var end = Math.Clamp(e.Range.EndCaretPosition, start, length);
            var caret = lineStart + Math.Clamp(e.NewSelection.EndCaretPosition, 0, length - (end - start) + e.Text.Length);
            if (HasSelection && (Selection.Start < lineStart || Selection.End > lineStart + length))
            {
                Insert(string.Empty); // a selection across lines goes first, as typing over it would
                line = _doc.GetLineAtOffset(_caret);
                lineStart = _doc.GetLineStart(line);
                caret = _caret + e.Text.Length;
                (start, end) = (_caret - lineStart, _caret - lineStart);
            }

            ApplyEdit(new TextEdit(lineStart + start, end - start, e.Text, caret, 0), recordUndo: true);
            RememberImeBuffer();
            e.Result = CoreTextTextUpdatingResult.Succeeded;
        }
        catch (Exception)
        {
            e.Result = CoreTextTextUpdatingResult.Failed;
        }
        finally
        {
            _inImeUpdate = false;
        }
    }

    private void OnImeSelectionUpdating(CoreTextEditContext sender, CoreTextSelectionUpdatingEventArgs e)
    {
        var line = _doc.GetLineAtOffset(_caret);
        var lineStart = _doc.GetLineStart(line);
        var length = _doc.GetLineText(line).Length;
        _anchor = lineStart + Math.Clamp(e.Selection.StartCaretPosition, 0, length);
        _inImeUpdate = true;
        try
        {
            SetCaret(lineStart + Math.Clamp(e.Selection.EndCaretPosition, 0, length), extend: true);
        }
        finally
        {
            _inImeUpdate = false;
        }

        e.Result = CoreTextSelectionUpdatingResult.Succeeded;
    }

    /// <summary>Where the candidate window goes: the caret in physical screen pixels.</summary>
    private void OnImeLayoutRequested(CoreTextEditContext sender, CoreTextLayoutRequestedEventArgs e)
    {
        try
        {
            var caret = CaretRect(_caret);
            var scale = XamlRoot?.RasterizationScale ?? 1.0;
            var inWindow = _canvas.TransformToVisual(null).TransformPoint(new Point(caret.X, caret.Y));
            var origin = new System.Drawing.Point(0, 0);
            var window = PInvoke.GetActiveWindow();
            if (!window.IsNull)
            {
                PInvoke.ClientToScreen(window, ref origin);
            }

            var rect = new Rect(origin.X + inWindow.X * scale, origin.Y + inWindow.Y * scale, 2 * scale, caret.Height * scale);
            e.Request.LayoutBounds.TextBounds = rect;
            e.Request.LayoutBounds.ControlBounds = rect;
        }
        catch (Exception)
        {
            // keep the IME's default placement
        }
    }

    /// <summary>The composition underline on the caret line (drawn after the text).</summary>
    private void DrawComposition(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, int line, Microsoft.Graphics.Canvas.Text.CanvasTextLayout layout, LinePresentation presentation, float x, float y)
    {
        if (_compositionRange is not { } range || range.Length <= 0 || line != _doc.GetLineAtOffset(_caret))
        {
            return;
        }

        var from = presentation.ToShown(range.Start);
        var to = presentation.ToShown(range.Start + range.Length);
        if (to <= from)
        {
            return;
        }

        foreach (var r in layout.GetCharacterRegions(from, to - from))
        {
            ds.FillRectangle(x + (float)r.LayoutBounds.X, y + (float)r.LayoutBounds.Bottom - 2, (float)r.LayoutBounds.Width, 1.5f, _theme.Text);
        }
    }
}
