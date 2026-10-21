using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using WolfMark.App.Infrastructure;
using WolfMark.Core.Markdown;
using WolfMark.Core.Settings;

namespace WolfMark.Desktop.Editor;

/// <summary>
/// Markdown editor on AvaloniaEdit (the macOS/Linux twin of src/WolfMark.Editor/MarkdownEditor.cs). One plain-text
/// document (ADR 0001); tokens come from the shared lexer off the UI thread; formatted mode hides delimiters and
/// shows image links as pictures. Editing shortcuts are the shared <see cref="MarkdownEditing"/> functions.
/// </summary>
internal enum FormatCommand
{
    Bold,
    Italic,
    Strikethrough,
    Code,
    Link,
    Task,
    Heading1,
    Heading2,
    Heading3,
    Quote,
    Bullet,
    Numbered,
    Paragraph,
}

internal sealed class MarkdownTextEditor : UserControl
{
    private const int PreviewRows = 5;

    private readonly TextEditor _editor = new();
    private readonly MarkdownHighlighter _highlighter = new();
    private readonly Dictionary<int, List<MarkupSegment>> _segments = [];
    private readonly Dictionary<string, ImagePreview?> _previews = new(StringComparer.Ordinal);
    private readonly HashSet<string> _previewLoading = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _reveal = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private LineTokens[] _lines = [];
    private EditorTheme _theme = EditorTheme.Light(_ => Colors.Gray);
    private EditorMode _mode = EditorMode.Formatted;
    private string _text = string.Empty;
    private double _zoomWheel; // Ctrl+wheel delta not yet turned into a font size step
    private int _applying;
    private bool _ownLinePending;
    private readonly LineNumberMargin _lineNumbers;
    private readonly MarkerChips _chips;
    private int _caretLine;
    private IReadOnlySet<FormatCommand> _activeFormats = new HashSet<FormatCommand>();

    public MarkdownTextEditor()
    {
        _editor.WordWrap = true;
        _editor.Background = Brushes.Transparent;
        _editor.Padding = new Thickness(18, 0, 24, 24);
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.HighlightCurrentLine = false;
        _editor.Options.ConvertTabsToSpaces = false;
        _editor.Options.AllowScrollBelowDocument = false; // no empty page after the last line
        _lineNumbers = new LineNumberMargin(this);
        _editor.TextArea.LeftMargins.Insert(0, _lineNumbers);
        _chips = new MarkerChips(this, _editor.TextArea.TextView);
        _editor.TextArea.Caret.PositionChanged += (_, _) => OnCaretMoved();
        _editor.ContextMenu = BuildContextMenu();
        _editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => UpdateOverflow();
        _editor.TextArea.TextView.VisualLinesChanged += (_, _) => UpdateOverflow();
        _editor.TextArea.TextView.LineTransformers.Add(new MarkdownColorizer(this));
        _editor.TextArea.TextView.ElementGenerators.Add(new MarkupElementGenerator(this));
        _editor.Document.Changed += (_, _) => _segments.Clear();
        _editor.TextChanged += (_, _) => OnTextChanged();
        _editor.LostFocus += (_, _) => EditorLostFocus?.Invoke(this, EventArgs.Empty);
        _editor.TextArea.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _editor.AddHandler(PointerWheelChangedEvent, OnZoomWheel, RoutingStrategies.Tunnel);
        _editor.TextArea.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(_editor, true);
        DragDrop.AddDragOverHandler(_editor, OnDragOver);
        DragDrop.AddDropHandler(_editor, OnDrop);
        _highlighter.Updated += OnTokens;
        _reveal.Tick += (_, _) => Reveal();
        var host = new Grid();
        host.Children.Add(_editor);
        host.Children.Add(_chips);
        Content = host;
    }

    /// <summary>Raised after every user edit with the full new text.</summary>
    public event EventHandler<string>? TextEdited;

    public event EventHandler? EditorLostFocus;

    /// <summary>Ctrl + / Ctrl − / Ctrl+wheel: +1 or −1 step of the font size; 0 for Ctrl 0 (the default size).</summary>
    public event EventHandler<int>? FontZoomRequested;

    /// <summary>Ctrl+E – the view model owns the mode.</summary>
    public event EventHandler? ModeToggleRequested;

    /// <summary>Stores pasted or dropped image bytes (extension like ".png") in the note's assets; returns the link target.</summary>
    public Func<ReadOnlyMemory<byte>, string, Task<string?>>? ImagePasteHandler { get; set; }

    /// <summary>Folder of the open note – relative image links resolve against it; null shows no pictures.</summary>
    public string? NoteDirectory { get; set; }

    public string? NotesRoot { get; set; }

    public string? PreviewCacheDirectory { get; set; }

    public string OpenImageTip { get; set; } = "Double-click to open";

    public string Text => _text;

    public EditorTheme MarkdownTheme
    {
        get => _theme;
        set
        {
            _theme = value;
            _editor.FontFamily = value.Font;
            _editor.FontSize = value.FontSize;
            _editor.Foreground = new SolidColorBrush(value.Text);
            _editor.LineNumbersForeground = new SolidColorBrush(value.Muted);
            _previews.Clear(); // the preview height follows the font size
            Redraw();
        }
    }

    public EditorMode Mode
    {
        get => _mode;
        set
        {
            if (_mode != value)
            {
                _mode = value;
                Redraw();
            }
        }
    }

    public bool ShowLineNumbers
    {
        get => _lineNumbers.IsVisible;
        set => _lineNumbers.IsVisible = value;
    }

    /// <summary>1-based line of the caret (the line-number margin draws it darker, the chips sit on it).</summary>
    internal int CaretLine => _caretLine;

    /// <summary>Formats at the caret (toolbar buttons show them as pressed).</summary>
    public event EventHandler<IReadOnlySet<FormatCommand>>? ActiveFormatsChanged;

    public IReadOnlySet<FormatCommand> ActiveFormats => _activeFormats;

    /// <summary>True when text continues below the visible area ("continues ⌄" in the gutter).</summary>
    public bool HasMoreBelow { get; private set; }

    public event EventHandler<bool>? OverflowChanged;

    public void ScrollToEnd() => _editor.ScrollToEnd();

    private void UpdateOverflow()
    {
        var view = _editor.TextArea.TextView;
        var more = view.VerticalOffset + view.Bounds.Height < view.DocumentHeight - 1;
        if (more != HasMoreBelow)
        {
            HasMoreBelow = more;
            OverflowChanged?.Invoke(this, more);
        }
    }

    public void Load(string text, bool caretToEnd, bool readOnly)
    {
        text = text.Replace("\r\n", "\n");
        _applying++;
        try
        {
            _lines = [];
            _segments.Clear();
            _editor.Document.Text = text;
            _editor.Document.UndoStack.ClearAll();
            _editor.IsReadOnly = readOnly;
            _editor.CaretOffset = caretToEnd ? text.Length : 0;
            _text = text;
        }
        finally
        {
            _applying--;
        }

        // Plain text until the first tokens arrive – kept invisible so a note does not flash raw Markdown.
        _editor.Opacity = 0;
        _reveal.Start();
        _highlighter.Request(text);
        Dispatcher.UIThread.Post(() => _editor.TextArea.Caret.BringCaretToView(), DispatcherPriority.Background);
    }

    public void FocusEditor() => _editor.Focus();

    /// <summary>Toolbar formatting: the shared text functions on the selection or the caret's line.</summary>
    public void Execute(FormatCommand command)
    {
        if (_editor.IsReadOnly)
        {
            return;
        }

        var (start, length, caret) = (_editor.SelectionStart, _editor.SelectionLength, _editor.CaretOffset);
        Apply(command switch
        {
            FormatCommand.Bold => MarkdownEditing.ToggleWrap(_text, start, length, "**"),
            FormatCommand.Italic => MarkdownEditing.ToggleWrap(_text, start, length, "*"),
            FormatCommand.Strikethrough => MarkdownEditing.ToggleWrap(_text, start, length, "~~"),
            FormatCommand.Code => MarkdownEditing.ToggleWrap(_text, start, length, "`"),
            FormatCommand.Link => MarkdownEditing.InsertLink(_text, start, length),
            FormatCommand.Task => MarkdownEditing.ToggleTask(_text, caret),
            FormatCommand.Heading1 => MarkdownEditing.ToggleHeading(_text, caret, 1),
            FormatCommand.Heading2 => MarkdownEditing.ToggleHeading(_text, caret, 2),
            FormatCommand.Heading3 => MarkdownEditing.ToggleHeading(_text, caret, 3),
            FormatCommand.Quote => MarkdownEditing.SetBlock(_text, caret, BlockKind.Quote),
            FormatCommand.Bullet => MarkdownEditing.SetBlock(_text, caret, BlockKind.Bullet),
            FormatCommand.Numbered => MarkdownEditing.SetBlock(_text, caret, BlockKind.Numbered),
            _ => MarkdownEditing.SetBlock(_text, caret, BlockKind.Paragraph),
        });
        _editor.Focus();
    }

    /// <summary>Copy as plain text or HTML (the shared converter), for the toolbar menu.</summary>
    public string SelectionOrAll() => _editor.SelectionLength > 0 ? _editor.SelectedText : _text;

    // ------------------------------------------------------------------ for the colorizer and the generator

    internal MarkdownToken[]? TokensOf(DocumentLine line)
    {
        var index = line.LineNumber - 1;
        if (index >= _lines.Length)
        {
            return null;
        }

        var cached = _lines[index];
        return cached.Text.Length == line.Length && cached.Text == _editor.Document.GetText(line) ? cached.Tokens : null;
    }

    internal List<MarkupSegment> SegmentsOf(DocumentLine line)
    {
        if (_mode != EditorMode.Formatted)
        {
            return [];
        }

        if (!_segments.TryGetValue(line.LineNumber, out var segments))
        {
            segments = TokensOf(line) is { } tokens
                ? MarkupSegments.Build(_editor.Document.GetText(line), tokens, ResolveImage)
                : [];
            _segments[line.LineNumber] = segments;
        }

        return segments;
    }

    /// <summary>The preview of an image file, or null while it loads (the link shows as text meanwhile).</summary>
    internal ImagePreview? PreviewOf(string path)
    {
        if (_previews.TryGetValue(path, out var preview))
        {
            return preview;
        }

        if (_previewLoading.Add(path))
        {
            var pixels = (int)Math.Ceiling(PreviewRows * RowHeight * Scale);
            var cache = PreviewCacheDirectory;
            _ = Task.Run(() =>
            {
                try
                {
                    return ImagePreviews.Load(path, pixels, cache);
                }
                catch (Exception)
                {
                    return null; // missing, not an image – the link stays a link
                }
            }).ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                _previewLoading.Remove(path);
                _previews[path] = t.Result;
                Redraw();
            }), TaskScheduler.Default);
        }

        return null;
    }

    /// <summary><see cref="PreviewRows"/> text rows high (smaller pictures keep their size), never wider than the text.</summary>
    internal Size PreviewSize(ImagePreview preview)
    {
        var height = Math.Min(PreviewRows * RowHeight, preview.Pixels.Height / Scale);
        var width = height * preview.Pixels.Width / preview.Pixels.Height;
        var column = Math.Max(80, _editor.TextArea.TextView.Bounds.Width - 40);
        if (width > column)
        {
            width = column;
            height = width * preview.Pixels.Height / preview.Pixels.Width;
        }

        return new Size(Math.Round(width), Math.Round(height));
    }

    internal void OpenFile(string path)
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
        {
            _ = launcher.LaunchFileInfoAsync(new FileInfo(path));
        }
    }

    private double RowHeight => _theme.FontSize * 1.4;

    private double Scale => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    private string? ResolveImage(string url) =>
        NoteDirectory is { } dir && NotesRoot is { } root && MarkdownImageLink.TryResolveLocal(dir, root, url, out var full) ? full : null;

    // ------------------------------------------------------------------ text model

    private void OnTokens(LineTokens[] lines, int first, int last)
    {
        _lines = lines;
        Redraw();
        Reveal();
        OnCaretMoved();
    }

    private void OnCaretMoved()
    {
        var line = _editor.TextArea.Caret.Line;
        if (line != _caretLine)
        {
            _caretLine = line;
            _lineNumbers.InvalidateVisual();
        }

        _chips.InvalidateVisual();
        UpdateActiveFormats();
    }

    /// <summary>Heading level, task and the inline formats around the caret, from the line's tokens.</summary>
    private void UpdateActiveFormats()
    {
        var active = new HashSet<FormatCommand>();
        var doc = _editor.Document;
        if (_caretLine >= 1 && _caretLine <= doc.LineCount && TokensOf(doc.GetLineByNumber(_caretLine)) is { } tokens)
        {
            var column = _editor.TextArea.Caret.Column - 1;
            foreach (var t in tokens)
            {
                FormatCommand? line = t.Kind switch
                {
                    MarkdownTokenKind.Heading1 => FormatCommand.Heading1,
                    MarkdownTokenKind.Heading2 => FormatCommand.Heading2,
                    MarkdownTokenKind.Heading3 => FormatCommand.Heading3,
                    MarkdownTokenKind.TaskOpen or MarkdownTokenKind.TaskDone => FormatCommand.Task,
                    _ => null,
                };
                if (line is { } l)
                {
                    active.Add(l);
                }

                FormatCommand? inline = t.Kind switch
                {
                    MarkdownTokenKind.Strong => FormatCommand.Bold,
                    MarkdownTokenKind.Emphasis => FormatCommand.Italic,
                    MarkdownTokenKind.Strikethrough => FormatCommand.Strikethrough,
                    MarkdownTokenKind.InlineCode => FormatCommand.Code,
                    MarkdownTokenKind.LinkText or MarkdownTokenKind.WikiLink => FormatCommand.Link,
                    _ => null,
                };
                if (inline is { } i && t.Start <= column && column <= t.End)
                {
                    active.Add(i);
                }
            }
        }

        if (!active.SetEquals(_activeFormats))
        {
            _activeFormats = active;
            ActiveFormatsChanged?.Invoke(this, active);
        }
    }

    // ------------------------------------------------------------------ context menu

    /// <summary>Clipboard, the line's block type and inline formats, copy as plain text or HTML.</summary>
    private ContextMenu BuildContextMenu()
    {
        MenuItem Item(string key, Action action, string? gesture = null)
        {
            var item = new MenuItem { Header = Strings.Get(key) };
            if (gesture is not null)
            {
                item.InputGesture = KeyGesture.Parse(gesture);
            }

            item.Click += (_, _) => action();
            return item;
        }

        MenuItem Format(string key, FormatCommand command) => Item(key, () => Execute(command));
        var menu = new ContextMenu
        {
            Items =
            {
                Item(R.Menu_Cut, () => _editor.Cut(), "Ctrl+X"),
                Item(R.Menu_Copy, () => _editor.Copy(), "Ctrl+C"),
                Item(R.Menu_Paste, () => _ = PasteAsync(), "Ctrl+V"),
                Item(R.Menu_SelectAll, () => _editor.SelectAll(), "Ctrl+A"),
                new Separator(),
                Format(R.Format_Paragraph, FormatCommand.Paragraph),
                Item(R.Format_H1, () => Execute(FormatCommand.Heading1)),
                Item(R.Format_H2, () => Execute(FormatCommand.Heading2)),
                Item(R.Format_H3, () => Execute(FormatCommand.Heading3)),
                Format(R.Format_Quote, FormatCommand.Quote),
                Format(R.Format_Bullet, FormatCommand.Bullet),
                Format(R.Format_Numbered, FormatCommand.Numbered),
                Format(R.Format_Task, FormatCommand.Task),
                new Separator(),
                Format(R.Format_Bold, FormatCommand.Bold),
                Format(R.Format_Italic, FormatCommand.Italic),
                Format(R.Format_Strike, FormatCommand.Strikethrough),
                Format(R.Format_Code, FormatCommand.Code),
                Format(R.Format_Link, FormatCommand.Link),
                new Separator(),
                Item(R.Copy_Plain, () => _ = CopyAsync(MarkdownConvert.ToPlainText(SelectionOrAll()))),
                Item(R.Copy_Html, () => _ = CopyAsync(MarkdownConvert.ToHtml(SelectionOrAll()))),
            },
        };
        menu.Opening += (_, _) =>
        {
            // Editing entries only for editable notes; copying always.
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                var header = item.Header as string;
                item.IsEnabled = !_editor.IsReadOnly
                    || header == Strings.Get(R.Menu_Copy)
                    || header == Strings.Get(R.Menu_SelectAll)
                    || header == Strings.Get(R.Copy_Plain)
                    || header == Strings.Get(R.Copy_Html);
            }
        };
        return menu;
    }

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void Redraw()
    {
        _segments.Clear();
        _editor.TextArea.TextView.Redraw();
        _chips.InvalidateVisual();
    }

    private void Reveal()
    {
        _reveal.Stop();
        _editor.Opacity = 1;
    }

    private void OnTextChanged()
    {
        if (_applying > 0)
        {
            return;
        }

        var old = _text;
        _text = _editor.Document.Text;
        _highlighter.Request(_text);
        TextEdited?.Invoke(this, _text);
        if (_mode == EditorMode.Formatted && !_ownLinePending
            && MarkdownImageBlock.KeepOnOwnLine(old, _text, IsImageBlock) is not null)
        {
            // Typed beside a picture: the text gets a line of its own – after AvaloniaEdit finished this change.
            _ownLinePending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _ownLinePending = false;
                if (MarkdownImageBlock.KeepOnOwnLine(old, _text, IsImageBlock) is { } edit)
                {
                    Apply(edit);
                }
            });
        }
    }

    /// <summary>An image-only line shown as a picture (formatted mode treats it as a block).</summary>
    private bool IsImageBlock(string line)
    {
        if (!MarkdownImageBlock.IsImageLine(line))
        {
            return false;
        }

        var open = line.IndexOf("](", StringComparison.Ordinal);
        var url = open < 0 ? null : line[(open + 2)..].TrimEnd().TrimEnd(')');
        return url is not null && ResolveImage(url) is { } path && _previews.TryGetValue(path, out var p) && p is not null;
    }

    /// <summary>One undo step: replace a range, then place the selection.</summary>
    private void Apply(TextEdit edit)
    {
        var doc = _editor.Document;
        if (edit.Start < 0 || edit.Start + edit.Length > doc.TextLength)
        {
            return;
        }

        using (doc.RunUpdate())
        {
            doc.Replace(edit.Start, edit.Length, edit.Insert);
        }

        var start = Math.Clamp(edit.SelectionStart, 0, doc.TextLength);
        _editor.Select(start, Math.Clamp(edit.SelectionLength, 0, doc.TextLength - start));
        _editor.TextArea.Caret.BringCaretToView();
    }

    // ------------------------------------------------------------------ keys and pointer

    /// <summary>The font size step of a Ctrl shortcut, by the character on the layout: "+" (or "=") +1, "-" −1, "0" 0.</summary>
    private static int? ZoomStep(KeyEventArgs e) => e.Key switch
    {
        Key.Add => 1,
        Key.Subtract => -1,
        Key.D0 or Key.NumPad0 => 0,
        _ => e.KeySymbol switch
        {
            "+" or "=" => 1,
            "-" => -1,
            null => e.Key switch { Key.OemPlus => 1, Key.OemMinus => -1, _ => null },
            _ => null,
        },
    };

    private void OnZoomWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return;
        }

        // Ctrl+wheel zooms instead of scrolling; a touchpad sends fractions of a notch.
        e.Handled = true;
        _zoomWheel += e.Delta.Y;
        while (Math.Abs(_zoomWheel) >= 1)
        {
            var step = Math.Sign(_zoomWheel);
            _zoomWheel -= step;
            FontZoomRequested?.Invoke(this, step);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (ctrl && !e.KeyModifiers.HasFlag(KeyModifiers.Alt) && ZoomStep(e) is { } zoom)
        {
            e.Handled = true;
            FontZoomRequested?.Invoke(this, zoom);
            return;
        }

        var start = _editor.SelectionStart;
        var length = _editor.SelectionLength;
        var text = _text;
        if (ctrl && e.Key == Key.E && !shift)
        {
            e.Handled = true;
            ModeToggleRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_editor.IsReadOnly)
        {
            return;
        }

        TextEdit? edit = e.Key switch
        {
            Key.B when ctrl && !shift => MarkdownEditing.ToggleWrap(text, start, length, "**"),
            Key.I when ctrl && !shift => MarkdownEditing.ToggleWrap(text, start, length, "*"),
            Key.K when ctrl && !shift && length > 0 => MarkdownEditing.InsertLink(text, start, length),
            Key.L when ctrl && shift => MarkdownEditing.ToggleTask(text, start),
            Key.Enter when !ctrl && !shift && length == 0 => MarkdownEditing.ContinueList(text, start),
            Key.Tab when !ctrl => MarkdownEditing.Indent(text, start, length, outdent: shift),
            Key.Back when !ctrl && length == 0 && _mode == EditorMode.Formatted => MarkdownImageBlock.Backspace(text, start, IsImageBlock),
            Key.Delete when !ctrl && length == 0 && _mode == EditorMode.Formatted => MarkdownImageBlock.Delete(text, start, IsImageBlock),
            _ => null,
        };

        if (edit is { } change)
        {
            e.Handled = true;
            Apply(change);
            return;
        }

        if (ctrl && !shift && e.Key == Key.V)
        {
            e.Handled = true;
            _ = PasteAsync();
        }
    }

    /// <summary>A click on a task checkbox toggles it (formatted mode, editable notes).</summary>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_mode != EditorMode.Formatted || _editor.IsReadOnly || e.KeyModifiers != KeyModifiers.None
            || !e.GetCurrentPoint(_editor).Properties.IsLeftButtonPressed
            || _editor.GetPositionFromPoint(e.GetPosition(_editor)) is not { } position)
        {
            return;
        }

        var line = _editor.Document.GetLineByNumber(position.Line);
        var column = position.Column - 1;
        if (TokensOf(line) is { } tokens
            && tokens.Any(t => t.Kind is MarkdownTokenKind.TaskOpen or MarkdownTokenKind.TaskDone && t.Start <= column && column <= t.End))
        {
            e.Handled = true;
            Apply(MarkdownEditing.ToggleTask(_text, line.Offset + column));
        }
    }

    // ------------------------------------------------------------------ paste and drop

    private async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            if (await clipboard.TryGetFilesAsync() is { } files && await InsertImageFilesAsync(files, null))
            {
                return;
            }

            if (ImagePasteHandler is { } handler && await clipboard.TryGetBitmapAsync() is { } bitmap)
            {
                using var png = new MemoryStream();
                bitmap.Save(png, PngBitmapEncoderOptions.Default);
                if (await handler(png.ToArray(), ".png") is { } link)
                {
                    InsertImageLinks($"![]({link})", null);
                }

                return;
            }

            if (await clipboard.TryGetTextAsync() is { } text)
            {
                text = text.Replace("\r\n", "\n").Replace('\r', '\n');
                Apply(new TextEdit(_editor.SelectionStart, _editor.SelectionLength, text, _editor.SelectionStart + text.Length, 0));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // clipboard owned by another app – this paste is skipped
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!_editor.IsReadOnly && ImagePasteHandler is not null && e.DataTransfer.TryGetFiles() is { } files
            && files.Any(f => IsImageFile(f.TryGetLocalPath())))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_editor.IsReadOnly || e.DataTransfer.TryGetFiles() is not { } files)
        {
            return;
        }

        int? at = _editor.GetPositionFromPoint(e.GetPosition(_editor)) is { } position
            ? _editor.Document.GetOffset(position.Location)
            : null;
        e.Handled = true;
        await InsertImageFilesAsync(files, at);
    }

    /// <summary>Copies image files into the note's assets and links them on lines of their own.</summary>
    private async Task<bool> InsertImageFilesAsync(IEnumerable<IStorageItem> items, int? at)
    {
        var paths = items.Select(i => i.TryGetLocalPath()).Where(IsImageFile).Select(p => p!).ToList();
        if (paths.Count == 0 || ImagePasteHandler is not { } handler)
        {
            return false;
        }

        var links = new List<string>();
        foreach (var path in paths)
        {
            var bytes = await Task.Run(() => File.ReadAllBytesAsync(path));
            if (await handler(bytes, Path.GetExtension(path).ToLowerInvariant()) is { } link)
            {
                var alt = Path.GetFileNameWithoutExtension(path).Replace("[", "(").Replace("]", ")");
                links.Add($"![{alt}]({link})");
            }
        }

        if (links.Count > 0)
        {
            InsertImageLinks(string.Join("\n", links), at);
        }

        return true;
    }

    private void InsertImageLinks(string links, int? at) =>
        Apply(MarkdownImageBlock.InsertOnOwnLine(_text, at ?? _editor.SelectionStart, links));

    private static bool IsImageFile(string? path) =>
        !string.IsNullOrEmpty(path) && MarkdownImageLink.Extensions.Contains(Path.GetExtension(path));
}
