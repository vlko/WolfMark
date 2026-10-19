using Microsoft.UI.Text;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor;

/// <summary>
/// Formatting state at the caret, toggle commands, the "change markdown tag" context menu and the
/// marker overlay. In formatted mode the markdown delimiters stay hidden on every line; on the caret
/// line they are shown as small chips in an overlay at the top corners before and after the formatted
/// text, so revealing them never shifts the text.
/// </summary>
public sealed partial class MarkdownEditor
{
    private readonly Dictionary<int, (string Text, MarkdownToken[] Tokens)> _lineTokens = [];
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly List<ICommandBarElement> _menuItems = [];
    private HashSet<EditorCommand> _activeFormats = [];

    /// <summary>Formats active at the caret (toolbar buttons show them as pressed).</summary>
    public event EventHandler<IReadOnlySet<EditorCommand>>? ActiveFormatsChanged;

    public IReadOnlySet<EditorCommand> ActiveFormats => _activeFormats;

    /// <summary>Localised labels for the context menu (the app provides them; English fallback).</summary>
    public Func<EditorCommand, string>? CommandText { get; set; }

    /// <summary>Icons for the context menu; must return a new element per call (an element has one parent).</summary>
    public Func<EditorCommand, IconElement?>? CommandIcon { get; set; }

    /// <summary>The user's choice; read-only notes are never proofed (see <see cref="ApplySpellCheck"/>).</summary>
    public bool IsSpellCheckEnabled
    {
        get => _spellCheck;
        set
        {
            _spellCheck = value;
            if (ApplySpellCheck())
            {
                Reproof();
            }
        }
    }

    /// <summary>
    /// Proofing runs only on editable notes. The box is made editable for a moment on every format batch
    /// (RichEdit refuses format changes on a read-only document); with proofing on that restarts a background
    /// spell check of the whole note each time, and replacing a long read-only note while one runs crashed
    /// WinUIEdit.dll (access violation). Returns true when proofing was switched on.
    /// </summary>
    private bool ApplySpellCheck()
    {
        var on = _spellCheck && !_readOnly;
        var turnedOn = on && !_box.IsSpellCheckEnabled;
        _box.IsSpellCheckEnabled = on;
        return turnedOn;
    }

    private static readonly Dictionary<MarkdownTokenKind, EditorCommand> InlineCommands = new()
    {
        [MarkdownTokenKind.Strong] = EditorCommand.Bold,
        [MarkdownTokenKind.Emphasis] = EditorCommand.Italic,
        [MarkdownTokenKind.Strikethrough] = EditorCommand.Strikethrough,
        [MarkdownTokenKind.InlineCode] = EditorCommand.Code,
        [MarkdownTokenKind.LinkText] = EditorCommand.Link,
        [MarkdownTokenKind.WikiLink] = EditorCommand.Link,
        [MarkdownTokenKind.AutoLink] = EditorCommand.Link,
    };

    // ---------------------------------------------------------------- caret state

    private void CacheLine(LineStyle style) => _lineTokens[style.Line] = (style.Text, style.Tokens);

    /// <summary>Tokens of the caret line if the cache matches the current text, else null.</summary>
    private (int LineStart, string Text, MarkdownToken[] Tokens)? CaretLine(out int column)
    {
        var (caret, _) = GetSelection();
        var (lineStart, lineEnd) = MarkdownEditing.LineAt(_text, caret);
        column = caret - lineStart;
        var line = LineOf(_text, caret);
        if (_lineTokens.TryGetValue(line, out var cached)
            && cached.Text.Length == lineEnd - lineStart
            && _text.AsSpan(lineStart, lineEnd - lineStart).SequenceEqual(cached.Text))
        {
            return (lineStart, cached.Text, cached.Tokens);
        }

        return null;
    }

    /// <summary>Recomputes the active formats and the marker overlay for the caret line.</summary>
    private void RefreshCaretState()
    {
        var info = CaretLine(out var column);
        if (info is null)
        {
            // Lines shifted by an edit are not in the cache yet – ask the background lexer for this one.
            RequestHighlight(full: false, extraLine: LineOf(_text, GetSelection().Start));
            SetActiveFormats([]);
            _overlay.Children.Clear();
            return;
        }

        var (_, text, tokens) = info.Value;
        var active = new HashSet<EditorCommand>();
        foreach (var t in tokens)
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
                case MarkdownTokenKind.ListMarker:
                    active.Add(char.IsAsciiDigit(text[t.Start]) ? EditorCommand.NumberedList : EditorCommand.BulletList);
                    break;
                default:
                    if (InlineCommands.TryGetValue(t.Kind, out var command) && t.Length > 0 && column >= t.Start && column <= t.End)
                    {
                        active.Add(command);
                    }

                    break;
            }
        }

        if (active.Contains(EditorCommand.Task))
        {
            active.Remove(EditorCommand.BulletList);
            active.Remove(EditorCommand.NumberedList);
        }

        SetActiveFormats(active);
        UpdateOverlay();
    }

    private void SetActiveFormats(HashSet<EditorCommand> formats)
    {
        if (formats.SetEquals(_activeFormats))
        {
            return;
        }

        _activeFormats = formats;
        ActiveFormatsChanged?.Invoke(this, formats);
    }

    /// <summary>
    /// Toggling an inline format off while the caret is inside it (no selection) removes its delimiters.
    /// Returns null when the format is not active at the caret.
    /// </summary>
    private TextEdit? UnwrapAtCaret(EditorCommand command)
    {
        if (CaretLine(out var column) is not { } info)
        {
            return null;
        }

        var (lineStart, _, tokens) = info;
        foreach (var content in tokens)
        {
            if (!InlineCommands.TryGetValue(content.Kind, out var c) || c != command || content.Kind == MarkdownTokenKind.AutoLink
                || column < content.Start || column > content.End)
            {
                continue;
            }

            var open = tokens.FirstOrDefault(t => t.Kind == MarkdownTokenKind.Syntax && t.End == content.Start && t.Length > 0);
            var close = tokens.FirstOrDefault(t => t.Kind == MarkdownTokenKind.Syntax && t.Start == content.End && t.Length > 0);
            if (open.Length == 0 || close.Length == 0)
            {
                continue;
            }

            if (content.Kind == MarkdownTokenKind.LinkText)
            {
                // "[text](url)": closing part is "](" + url + ")".
                var urlEnd = tokens.FirstOrDefault(t => t.Kind == MarkdownTokenKind.Syntax && t.Start > content.End && _text[lineStart + t.Start] == ')');
                if (urlEnd.Length > 0)
                {
                    close = new MarkdownToken(close.Start, urlEnd.End - close.Start, MarkdownTokenKind.Syntax);
                }
            }

            return MarkdownEditing.Unwrap(_text, lineStart + open.Start, open.Length, lineStart + close.Start, close.Length, lineStart + column);
        }

        return null;
    }

    // ---------------------------------------------------------------- overlay chips

    private void UpdateOverlay()
    {
        _overlay.Children.Clear();
        if (_mode != MarkdownEditorMode.Formatted || CaretLine(out _) is not { } info)
        {
            return;
        }

        var (lineStart, text, tokens) = info;
        _overlayOrigin = ViewportOrigin();
        foreach (var t in tokens)
        {
            if (t.Kind is >= MarkdownTokenKind.Heading1 and <= MarkdownTokenKind.Heading6)
            {
                var marker = tokens.FirstOrDefault(s => s.Kind == MarkdownTokenKind.Syntax && s.Start == 0);
                if (marker.End < text.Length)
                {
                    AddChip("H" + (t.Kind - MarkdownTokenKind.Heading1 + 1), lineStart + marker.End, before: true);
                }

                continue;
            }

            if (t.Kind == MarkdownTokenKind.Quote)
            {
                var marker = tokens.LastOrDefault(s => s.Kind == MarkdownTokenKind.Syntax && s.End <= text.Length && text[s.Start] == '>');
                if (marker.Length > 0 && marker.End < text.Length)
                {
                    AddChip(">", lineStart + marker.End, before: true);
                }

                continue;
            }

            if (!InlineCommands.ContainsKey(t.Kind) || t.Kind == MarkdownTokenKind.AutoLink || t.Length == 0)
            {
                continue;
            }

            var open = tokens.FirstOrDefault(s => s.Kind == MarkdownTokenKind.Syntax && s.End == t.Start && s.Length > 0);
            var close = tokens.FirstOrDefault(s => s.Kind == MarkdownTokenKind.Syntax && s.Start == t.End && s.Length > 0);
            if (open.Length == 0 || close.Length == 0)
            {
                continue;
            }

            var closeLabel = t.Kind == MarkdownTokenKind.LinkText ? "]↗" : text.Substring(close.Start, close.Length);
            AddChip(text.Substring(open.Start, open.Length), lineStart + t.Start, before: true);
            AddChip(closeLabel, lineStart + t.End, before: false);
        }
    }

    /// <summary>A tiny marker chip at the top-left of <paramref name="offset"/> (before) or the top-right of the char before it (after).</summary>
    private void AddChip(string label, int offset, bool before)
    {
        if (offset < 0 || offset > _text.Length)
        {
            return;
        }

        var range = before
            ? _box.TextDocument.GetRange(offset, Math.Min(offset + 1, _text.Length))
            : _box.TextDocument.GetRange(Math.Max(0, offset - 1), offset);
        range.GetRect(PointOptions.ClientCoordinates, out var rect, out _);
        if (rect.Width <= 0 && rect.Height <= 0)
        {
            return;
        }

        var chip = new Border
        {
            Child = new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily(_theme.MonoFontFamily),
                FontSize = 9,
                Foreground = new SolidColorBrush(_theme.Muted),
            },
            Background = new SolidColorBrush(_theme.CodeBackground),
            Padding = new Thickness(2, 0, 2, 0),
            CornerRadius = new CornerRadius(2),
            Opacity = 0.9,
        };
        chip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = chip.DesiredSize;

        // GetRect is relative to the text viewport, the overlay to the whole control.
        var left = _overlayOrigin.X + (before ? rect.Left - size.Width + 2 : rect.Right - 2);
        var top = _overlayOrigin.Y + rect.Top - (size.Height * 0.55);
        Canvas.SetLeft(chip, left);
        Canvas.SetTop(chip, top);
        _overlay.Children.Add(chip);
    }

    private Point _overlayOrigin;

    /// <summary>
    /// Top-left of the text viewport (the ScrollContentPresenter inside border and padding) in overlay
    /// coordinates; falls back to border + padding before the template is loaded.
    /// </summary>
    private Point ViewportOrigin()
    {
        if (_scrollViewer is not null && FindDescendant<ScrollContentPresenter>(_scrollViewer) is { } presenter)
        {
            try
            {
                return presenter.TransformToVisual(_overlay).TransformPoint(new Point(0, 0));
            }
            catch (ArgumentException)
            {
                // not in the same visual tree yet
            }
        }

        return new Point(_box.BorderThickness.Left + _box.Padding.Left, _box.BorderThickness.Top + _box.Padding.Top);
    }

    // ---------------------------------------------------------------- clipboard

    private enum CopyKind
    {
        Markdown,
        PlainText,
        Html,
    }

    /// <summary>
    /// Copy/cut always produce markdown from the text model (RichEdit would drop delimiters hidden in
    /// formatted mode). Covers Ctrl+C/Ctrl+X and the menu's Copy/Cut.
    /// </summary>
    private void InitClipboard()
    {
        _box.CopyingToClipboard += (sender, e) =>
        {
            e.Handled = true;
            _ = CopyAsync(CopyKind.Markdown, cut: false);
        };
        _box.CuttingToClipboard += (sender, e) =>
        {
            e.Handled = true;
            _ = CopyAsync(CopyKind.Markdown, cut: !_box.IsReadOnly);
        };
    }

    private async Task CopyAsync(CopyKind kind, bool cut)
    {
        var (start, length) = GetSelection();
        if (length == 0)
        {
            return;
        }

        var text = _text;
        // Selection widening and conversion are parsing – off the UI thread (CLAUDE.md rule 3).
        var (from, to, payload, plain) = await Task.Run(() =>
        {
            var (s, e) = MarkdownEditing.ExpandToMarkup(text, start, start + length);
            var markdown = text[s..e];
            return kind switch
            {
                CopyKind.PlainText => (s, e, MarkdownConvert.ToPlainText(markdown), (string?)null),
                CopyKind.Html => (s, e, MarkdownConvert.ToHtml(markdown), MarkdownConvert.ToPlainText(markdown)),
                _ => (s, e, markdown, (string?)null),
            };
        });

        static string Crlf(string s) => s.Replace("\n", "\r\n", StringComparison.Ordinal);
        var data = new DataPackage();
        if (kind == CopyKind.Html)
        {
            data.SetHtmlFormat(HtmlFormatHelper.CreateHtmlFormat(payload));
            data.SetText(Crlf(plain!));
        }
        else
        {
            data.SetText(Crlf(payload));
        }

        try
        {
            Clipboard.SetContent(data);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return; // clipboard locked by another process
        }

        if (cut && ReferenceEquals(text, _text))
        {
            _undo.Seal();
            ApplyEdit(new TextEdit(from, to - from, string.Empty, from, 0), recordUndo: true);
            _undo.Seal();
        }
    }

    // ---------------------------------------------------------------- context menu

    /// <summary>Adds "change markdown tag" items to the standard text context menu (spelling and clipboard stay).</summary>
    /// <remarks>
    /// The control's default flyout is attached only when its template loads, so hooking it in the
    /// constructor never fires. We own the flyout instead; a TextCommandBarFlyout targeting a RichEditBox
    /// still adds the clipboard and proofing commands by itself.
    /// </remarks>
    private void InitContextMenu()
    {
        var flyout = new TextCommandBarFlyout();
        flyout.Opening += (_, _) => FillMenu(flyout.SecondaryCommands);
        _box.ContextFlyout = flyout;
    }

    private void FillMenu(IList<ICommandBarElement> commands)
    {
        foreach (var item in _menuItems)
        {
            commands.Remove(item);
        }

        _menuItems.Clear();
        var (selectionStart, selectionLength) = GetSelection();
        if (selectionLength > 0)
        {
            _menuItems.Add(new AppBarSeparator());
            foreach (var copy in new[] { EditorCommand.CopyPlainText, EditorCommand.CopyHtml })
            {
                var item = new AppBarButton { Label = Label(copy), Icon = CommandIcon?.Invoke(copy) };
                item.Click += (_, _) => Execute(copy);
                _menuItems.Add(item);
            }
        }

        if (_box.IsReadOnly)
        {
            foreach (var item in _menuItems)
            {
                commands.Add(item);
            }

            return;
        }

        var block = MarkdownEditing.GetBlock(_text, selectionStart);
        // Plain AppBarButtons like the built-in items: in the overflow menu AppBarToggleButton reserves a
        // check column and lays its icon out higher than the label. The current state is shown as "✓"
        // in the accelerator column on the right instead.
        void Toggle(EditorCommand command, bool isChecked)
        {
            var item = new AppBarButton { Label = Label(command), Icon = CommandIcon?.Invoke(command) };
            if (isChecked)
            {
                item.KeyboardAcceleratorTextOverride = "✓";
            }

            item.Click += (_, _) => Execute(command);
            _menuItems.Add(item);
        }

        _menuItems.Add(new AppBarSeparator());
        Toggle(EditorCommand.Paragraph, block == BlockKind.Paragraph);
        Toggle(EditorCommand.Heading1, block == BlockKind.Heading1);
        Toggle(EditorCommand.Heading2, block == BlockKind.Heading2);
        Toggle(EditorCommand.Heading3, block == BlockKind.Heading3);
        Toggle(EditorCommand.Quote, block == BlockKind.Quote);
        Toggle(EditorCommand.BulletList, block == BlockKind.Bullet);
        Toggle(EditorCommand.NumberedList, block == BlockKind.Numbered);
        Toggle(EditorCommand.Task, block == BlockKind.Task);
        _menuItems.Add(new AppBarSeparator());
        foreach (var inline in new[] { EditorCommand.Bold, EditorCommand.Italic, EditorCommand.Strikethrough, EditorCommand.Code, EditorCommand.Link })
        {
            Toggle(inline, _activeFormats.Contains(inline));
        }

        foreach (var item in _menuItems)
        {
            commands.Add(item);
        }
    }

    private string Label(EditorCommand command) => CommandText?.Invoke(command) ?? command switch
    {
        EditorCommand.Paragraph => "Paragraph",
        EditorCommand.Heading1 => "Heading 1",
        EditorCommand.Heading2 => "Heading 2",
        EditorCommand.Heading3 => "Heading 3",
        EditorCommand.Quote => "Quote",
        EditorCommand.BulletList => "Bulleted list",
        EditorCommand.NumberedList => "Numbered list",
        EditorCommand.Task => "Task",
        EditorCommand.CopyPlainText => "Copy as plain text",
        EditorCommand.CopyHtml => "Copy as HTML",
        _ => command.ToString(),
    };
}
