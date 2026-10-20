using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.UI;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor;

/// <summary>Labels of the editor's context menu (the app sets them in its language).</summary>
public sealed record EditorMenuTexts(
    string Cut = "Cut",
    string Copy = "Copy",
    string Paste = "Paste",
    string SelectAll = "Select all",
    string AddToDictionary = "Add to dictionary",
    string Ignore = "Ignore",
    string NoSuggestions = "No suggestions");

/// <summary>
/// Spell check of the lines in view: each line's prose (<see cref="MarkdownSpelling.ProseOnly"/>) goes to the
/// <see cref="SpellService"/> thread once, the result is cached by that text – the note's length does not matter.
/// The word being typed is not marked until the caret leaves it.
/// </summary>
public sealed partial class MarkdownEditor
{
    private static readonly Color SquiggleColor = Color.FromArgb(255, 0xD1, 0x34, 0x38);

    private readonly Dictionary<string, SpellError[]> _spellCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _spellPending = new(StringComparer.Ordinal);
    private SpellService? _speller;
    private bool _spellCheck;
    private bool _typingWord;

    public EditorMenuTexts MenuTexts { get; set; } = new();

    /// <summary>The user's choice; read-only notes are never proofed.</summary>
    public bool IsSpellCheckEnabled
    {
        get => _spellCheck;
        set
        {
            _spellCheck = value;
            if (value)
            {
                _speller ??= new SpellService(SpellService.DefaultLanguages(), action => _dispatcher.TryEnqueue(() => action()));
            }

            _canvas.Invalidate();
        }
    }

    private bool Proofing => _spellCheck && !_readOnly && _speller is not null;

    /// <summary>Errors of a line in source columns, or none yet (then the check is requested).</summary>
    private SpellError[] SpellErrors(int line)
    {
        if (!Proofing)
        {
            return [];
        }

        var prose = MarkdownSpelling.ProseOnly(_doc.GetLineText(line), _doc.GetTokens(line));
        if (string.IsNullOrWhiteSpace(prose))
        {
            return [];
        }

        if (_spellCache.TryGetValue(prose, out var errors))
        {
            return errors;
        }

        if (_spellPending.Add(prose))
        {
            _speller!.Check(prose, (text, found) =>
            {
                _spellPending.Remove(text);
                if (_spellCache.Count > 20_000)
                {
                    _spellCache.Clear(); // a bound, not an LRU: the visible lines are asked again
                }

                _spellCache[text] = found;
                if (found.Length > 0)
                {
                    _canvas.Invalidate();
                }
            });
        }

        return [];
    }

    private void DrawSquiggles(CanvasDrawingSession ds, int line, CanvasTextLayout layout, LinePresentation presentation, float x, float y)
    {
        var errors = SpellErrors(line);
        if (errors.Length == 0)
        {
            return;
        }

        var lineStart = _doc.GetLineStart(line);
        var caretColumn = _caret - lineStart;
        var onCaretLine = _doc.GetLineAtOffset(_caret) == line;
        foreach (var error in errors)
        {
            if (_typingWord && onCaretLine && caretColumn >= error.Start && caretColumn <= error.Start + error.Length)
            {
                continue; // still being typed
            }

            var from = presentation.ToShown(error.Start);
            var to = presentation.ToShown(error.Start + error.Length);
            if (to <= from)
            {
                continue;
            }

            foreach (var region in layout.GetCharacterRegions(from, to - from))
            {
                var r = region.LayoutBounds;
                Squiggle(ds, x + (float)r.X, x + (float)(r.X + r.Width), y + (float)(r.Y + r.Height) - 1.5f);
            }
        }
    }

    private static void Squiggle(CanvasDrawingSession ds, float left, float right, float baseline)
    {
        const float Step = 2f;
        var up = false;
        for (var px = left; px < right; px += Step)
        {
            var next = Math.Min(right, px + Step);
            ds.DrawLine(px, baseline + (up ? -1f : 1f), next, baseline + (up ? 1f : -1f), SquiggleColor, 1f);
            up = !up;
        }
    }

    /// <summary>The spelling error under a source offset, with its word.</summary>
    private (int Start, string Word)? ErrorAt(int offset)
    {
        var line = _doc.GetLineAtOffset(offset);
        var lineStart = _doc.GetLineStart(line);
        var column = offset - lineStart;
        foreach (var error in SpellErrors(line))
        {
            if (column >= error.Start && column <= error.Start + error.Length)
            {
                return (lineStart + error.Start, _doc.GetLineText(line).Substring(error.Start, error.Length));
            }
        }

        return null;
    }

    private void ForgetSpelling()
    {
        _spellCache.Clear();
        _spellPending.Clear();
        _canvas.Invalidate();
    }

    // ---------------------------------------------------------------- context menu

    private async void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var point = e.GetPosition(_canvas);
        var offset = OffsetAt(point);
        var (start, end) = Selection;
        if (offset < start || offset > end || !HasSelection)
        {
            SetCaret(offset, extend: false);
        }

        e.Handled = true;
        var menu = new MenuFlyout();
        if (Proofing && !_readOnly && ErrorAt(offset) is { } error)
        {
            var suggestions = await _speller!.SuggestAsync(error.Word);
            foreach (var suggestion in suggestions)
            {
                var item = new MenuFlyoutItem { Text = suggestion, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                item.Click += (_, _) => Edit(new TextEdit(error.Start, error.Word.Length, suggestion, error.Start + suggestion.Length, 0));
                menu.Items.Add(item);
            }

            if (suggestions.Count == 0)
            {
                menu.Items.Add(new MenuFlyoutItem { Text = MenuTexts.NoSuggestions, IsEnabled = false });
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(MenuItem(MenuTexts.AddToDictionary, () =>
            {
                _speller.Add(error.Word);
                ForgetSpelling();
            }));
            menu.Items.Add(MenuItem(MenuTexts.Ignore, () =>
            {
                _speller.Ignore(error.Word);
                ForgetSpelling();
            }));
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        if (!_readOnly)
        {
            menu.Items.Add(MenuItem(MenuTexts.Cut, () => CopySelection(cut: true), HasSelection));
        }

        menu.Items.Add(MenuItem(MenuTexts.Copy, () => CopySelection(cut: false), HasSelection));
        if (!_readOnly)
        {
            menu.Items.Add(MenuItem(MenuTexts.Paste, () => _ = PasteAsync()));
        }

        menu.Items.Add(MenuItem(MenuTexts.SelectAll, () =>
        {
            _anchor = 0;
            SetCaret(_doc.Length, extend: true);
        }));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(CommandItem(EditorCommand.CopyPlainText));
        menu.Items.Add(CommandItem(EditorCommand.CopyHtml));
        if (!_readOnly)
        {
            // The line's type and the inline formats, the current ones checked (as the toolbar).
            var block = MarkdownEditing.GetBlock(_doc.GetText(), _caret);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(CommandItem(EditorCommand.Paragraph, block == BlockKind.Paragraph));
            menu.Items.Add(CommandItem(EditorCommand.Heading1, block == BlockKind.Heading1));
            menu.Items.Add(CommandItem(EditorCommand.Heading2, block == BlockKind.Heading2));
            menu.Items.Add(CommandItem(EditorCommand.Heading3, block == BlockKind.Heading3));
            menu.Items.Add(CommandItem(EditorCommand.Quote, block == BlockKind.Quote));
            menu.Items.Add(CommandItem(EditorCommand.BulletList, block == BlockKind.Bullet));
            menu.Items.Add(CommandItem(EditorCommand.NumberedList, block == BlockKind.Numbered));
            menu.Items.Add(CommandItem(EditorCommand.Task, block == BlockKind.Task));
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var inline in new[] { EditorCommand.Bold, EditorCommand.Italic, EditorCommand.Strikethrough, EditorCommand.Code, EditorCommand.Link })
            {
                menu.Items.Add(CommandItem(inline, _activeFormats.Contains(inline)));
            }
        }

        menu.ShowAt(_canvas, new FlyoutShowOptions { Position = point });
    }

    private static MenuFlyoutItem MenuItem(string text, Action action, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>An editor command with the app's label and icon; "✓" in the accelerator column when it is active.</summary>
    private MenuFlyoutItem CommandItem(EditorCommand command, bool isChecked = false)
    {
        var item = new MenuFlyoutItem { Text = CommandText?.Invoke(command) ?? command.ToString(), Icon = CommandIcon?.Invoke(command) };
        if (isChecked)
        {
            item.KeyboardAcceleratorTextOverride = "\u2713";
        }

        item.Click += (_, _) => Execute(command);
        return item;
    }
}
