using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor;

/// <summary>
/// Image previews, drag &amp; drop and opening for <c>![alt](url)</c> lines. The document stays the plain
/// Markdown text: the space for a preview is reserved with the paragraph's SpaceAfter and the picture is drawn
/// in an overlay below the line, so nothing is inserted into RichEdit. A line holding only an image collapses
/// to a 1pt row and acts as a block (<see cref="MarkdownImageBlock"/>). Previews are a fixed
/// <see cref="PreviewRows"/> text rows high, formatted mode only, one per line (its first image); a click on
/// one opens the file in the default Windows app. Only relative paths inside the notes root are shown
/// (<see cref="MarkdownImageLink.TryResolveLocal"/>).
/// </summary>
public sealed partial class MarkdownEditor
{
    private const int PreviewRows = 5;
    private const double PreviewGap = 4;

    // Hit-testable only where a preview is (no background): clicks place the caret, wheel is passed on.
    private readonly Canvas _images = new();

    // Previews placed by the last UpdateImages (line range, box in canvas coordinates) and the caret drawn
    // beside one: on an image line RichEdit's own caret is hidden behind the picture.
    private readonly List<(int Start, int End, Rect Box)> _imageBoxes = [];
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _imageCaret = new() { Width = 2, IsHitTestVisible = false };

    // Pixel size per file; null = could not be read (missing, not an image).
    private readonly Dictionary<string, (uint Width, uint Height)?> _imageSizes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _imageSizeLoading = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapImage> _bitmaps = new(StringComparer.OrdinalIgnoreCase);

    // Line text → image file, filled when lines are styled; keyed by text so edits that shift lines don't matter.
    private readonly Dictionary<string, string> _imageLines = new(StringComparer.Ordinal);
    private double _imageColumnWidth;

    /// <summary>Folder of the open note – relative image links resolve against it. Null disables previews.</summary>
    public string? NoteDirectory { get; set; }

    /// <summary>Notes root; images outside it are never shown.</summary>
    public string? NotesRoot { get; set; }

    /// <summary>Caption of the drag &amp; drop cursor (the app provides a localised text).</summary>
    public string DropImageCaption { get; set; } = "Insert image";

    /// <summary>Tooltip of a preview (localised by the app).</summary>
    public string OpenImageTip { get; set; } = "Double-click to open";

    private void ResetImages()
    {
        _imageLines.Clear();
        _imageBoxes.Clear();
        _images.Children.Clear();
        foreach (var failed in _imageSizes.Where(p => p.Value is null).Select(p => p.Key).ToList())
        {
            _imageSizes.Remove(failed); // the file may have arrived since (OneDrive)
        }
    }

    /// <summary>
    /// Called for every styled line: reserves (or releases) the room for its preview. Returns true for an image
    /// line whose picture is shown – the caller collapses its markup (<see cref="CollapseImageLine"/>).
    /// </summary>
    private bool ReserveImageSpace(LineStyle style, ITextRange lineRange, MarkdownEditorMode mode)
    {
        var height = 0.0;
        var collapse = false;
        if (mode == MarkdownEditorMode.Formatted && ImageOf(style) is { } path)
        {
            _imageLines[style.Text] = path;
            if (!_imageSizes.TryGetValue(path, out var size))
            {
                EnsureImageSize(path);
            }
            else if (size is { } s)
            {
                collapse = MarkdownImageBlock.IsImageLine(style.Text);
                // A collapsed line's picture starts on its own (tiny) row, so only the gap below is added.
                height = PreviewSize(s).Height + (collapse ? PreviewGap : 2 * PreviewGap);
            }
        }

        // Notes without images never touch paragraph formats.
        // Setting a paragraph format re-lays the paragraph out – only when the value changes.
        if (height > 0 || _imageLines.Count > 0)
        {
            var paragraph = lineRange.ParagraphFormat;
            var points = (float)(height * 0.75);
            if (Math.Abs(paragraph.SpaceAfter - points) > 0.1f)
            {
                paragraph.SpaceAfter = points;
            }
        }

        return collapse;
    }

    /// <summary>
    /// An image line's markup becomes 1pt transparent text (paragraph mark included): its row shrinks to about a
    /// pixel, so the picture below takes the line's place. Not hidden – RichEdit moves the caret and gives typed
    /// text hidden formatting next to hidden text; tiny visible characters keep "before" and "after" real
    /// caret positions.
    /// </summary>
    private static void CollapseImageLine(ITextRange withMark)
    {
        var f = withMark.CharacterFormat;
        f.Size = 1;
        f.ForegroundColor = Microsoft.UI.Colors.Transparent;
        f.BackgroundColor = Microsoft.UI.Colors.Transparent;
        f.Hidden = FormatEffect.Off;
    }

    /// <summary>An image line shown as a picture: the editor treats it as one block (formatted mode).</summary>
    private bool IsCollapsedImageLine(string line) =>
        _mode == MarkdownEditorMode.Formatted
        && MarkdownImageBlock.IsImageLine(line)
        && _imageLines.TryGetValue(line, out var path)
        && _imageSizes.TryGetValue(path, out var size)
        && size is not null;

    private bool _snapPending;
    private long _lastSnap;

    /// <summary>
    /// Keeps the caret off the inside of an image's markup: it snaps to the nearer side. The move is queued, not
    /// done inside the selection notification – calling back into RichEdit while it is still updating its
    /// selection is the kind of re-entrancy that can corrupt its state.
    /// </summary>
    private bool SnapCaretOutOfImage()
    {
        if (_snapPending || Volatile.Read(ref _applying) > 0 || SnapTarget() is null)
        {
            return false;
        }

        // If RichEdit put the caret back inside, snapping again and again would starve the UI thread.
        var now = Environment.TickCount64;
        if (now - _lastSnap < 250)
        {
            return false;
        }

        _lastSnap = now;

        _snapPending = _dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            _snapPending = false;
            if (Volatile.Read(ref _applying) == 0 && SnapTarget() is { } to)
            {
                _box.TextDocument.Selection.SetRange(to, to); // re-checked: the caret may have moved meanwhile
            }
        });
        return _snapPending;
    }

    private int? SnapTarget()
    {
        var (caret, length) = GetSelection();
        var (start, end) = MarkdownEditing.LineAt(_text, caret);
        if (length > 0 || caret == start || caret == end || !IsCollapsedImageLine(_text[start..end]))
        {
            return null;
        }

        return caret - start < end - caret ? start : end;
    }

    /// <summary>Left/Right over an image: from one side straight to the other, or null for the default move.</summary>
    private int? ImageEdgeStep(int caret, bool forward)
    {
        var (start, end) = MarkdownEditing.LineAt(_text, caret);
        if (!IsCollapsedImageLine(_text[start..end]))
        {
            return null;
        }

        return forward && caret == start ? end : !forward && caret == end ? start : null;
    }

    private string? ImageOf(LineStyle style)
    {
        if (NoteDirectory is not { } dir || NotesRoot is not { } root)
        {
            return null;
        }

        foreach (var token in style.Tokens)
        {
            if (token.Kind == MarkdownTokenKind.Image
                && MarkdownImageLink.TryParse(style.Text.AsSpan(token.Start, token.Length), out _, out var url)
                && MarkdownImageLink.TryResolveLocal(dir, root, url, out var full))
            {
                return full;
            }
        }

        return null;
    }

    private void EnsureImageSize(string path)
    {
        if (_imageSizeLoading.Add(path))
        {
            _ = LoadImageSizeAsync(path);
        }
    }

    private async Task LoadImageSizeAsync(string path)
    {
        (uint, uint)? size = null;
        try
        {
            // Header only, off the UI thread.
            size = await Task.Run(async () =>
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenReadAsync();
                var decoder = await BitmapDecoder.CreateAsync(stream);
                return ((uint, uint)?)(decoder.PixelWidth, decoder.PixelHeight);
            });
        }
        catch (Exception)
        {
            // missing, not an image or not readable – the link just stays a link
        }

        _imageSizeLoading.Remove(path);
        _imageSizes[path] = size is { Item1: > 0, Item2: > 0 } ? size : null;
        RestyleImageLines();
    }

    /// <summary>
    /// Preview size in DIPs: <see cref="PreviewRows"/> text rows high (smaller images keep their size at the
    /// screen scale), narrower when the text column is too narrow for that.
    /// </summary>
    private Size PreviewSize((uint Width, uint Height) pixels)
    {
        var scale = XamlRoot?.RasterizationScale ?? 1;
        var rows = PreviewRows * TextRowHeight;
        var height = Math.Min(rows, pixels.Height / scale);
        var width = height * pixels.Width / pixels.Height;
        var column = Math.Max(80, _imageColumnWidth);
        if (width > column)
        {
            width = column;
            height = width * pixels.Height / pixels.Width;
        }

        return new Size(Math.Round(width), Math.Round(height));
    }

    private double TextRowHeight => _theme.FontSize * 1.4;


    private void OnImageColumnResized()
    {
        var width = Math.Max(0, _box.ActualWidth - _box.Padding.Left - _box.Padding.Right - 16);
        if (Math.Abs(width - _imageColumnWidth) < 1)
        {
            return;
        }

        _imageColumnWidth = width;
        if (_imageLines.Count > 0)
        {
            RestyleImageLines();
        }
    }

    /// <summary>Asks the highlighter to restyle every line that may hold an image (sizes changed).</summary>
    private void RestyleImageLines()
    {
        foreach (var (line, _, _) in ImageCandidateLines())
        {
            RequestHighlight(full: false, extraLine: line);
        }
    }

    /// <summary>Lines of the current text containing "![" (line index, start, end).</summary>
    private IEnumerable<(int Line, int Start, int End)> ImageCandidateLines()
    {
        var text = _text;
        var line = 0;
        var counted = 0;
        var at = text.IndexOf("![", StringComparison.Ordinal);
        while (at >= 0)
        {
            line += text.AsSpan(counted, at - counted).Count('\n');
            counted = at;
            var (start, end) = MarkdownEditing.LineAt(text, at);
            yield return (line, start, end);
            at = end < text.Length ? text.IndexOf("![", end, StringComparison.Ordinal) : -1;
        }
    }

    /// <summary>Places the previews of the visible image lines (after scrolling, resizing and styling).</summary>
    private void UpdateImages()
    {
        _images.Children.Clear();
        _imageBoxes.Clear();
        if (_imageLines.Count == 0 || _mode != MarkdownEditorMode.Formatted || _scrollViewer is not { } sv)
        {
            return;
        }

        var origin = ViewportOrigin();
        var shift = ScrollShift();
        var viewportHeight = sv.ViewportHeight;
        _images.Clip = new RectangleGeometry { Rect = new Rect(0, origin.Y, Math.Max(0, ActualWidth), Math.Max(0, viewportHeight)) };
        var doc = _box.TextDocument;
        foreach (var (_, start, end) in ImageCandidateLines())
        {
            var text = _text[start..end];
            if (!_imageLines.TryGetValue(text, out var path) || !_imageSizes.TryGetValue(path, out var pixels) || pixels is not { } px)
            {
                continue;
            }

            var size = PreviewSize(px);
            double top, left;
            var block = MarkdownImageBlock.IsImageLine(text);
            if (block)
            {
                // Collapsed line: the picture starts on its tiny row and covers the 1pt markup – RichEdit ignores
                // the transparent colour, so the markup would show as a thin line of glyphs above the picture.
                doc.GetRange(start, start + 1).GetRect(PointOptions.ClientCoordinates, out var firstRect, out _);
                top = firstRect.Top;
                left = firstRect.Left;
            }
            else
            {
                // After other text the picture goes below the line, measured on the paragraph mark (the line's
                // markup is hidden and hidden text has no usable position). The mark's rectangle may include the
                // reserved SpaceAfter, so the line bottom is capped at one text row.
                doc.GetRange(end, end + 1).GetRect(PointOptions.ClientCoordinates, out var markRect, out _);
                top = markRect.Top + Math.Min(markRect.Height, TextRowHeight) + PreviewGap;
                left = 0;
            }

            top += shift;
            if (top > viewportHeight || top + size.Height < 0)
            {
                continue;
            }

            var box = new Rect(origin.X + left, origin.Y + top, size.Width, size.Height);
            var image = new Image { Source = Bitmap(path, size.Height), Width = size.Width, Height = size.Height, Stretch = Stretch.Uniform };
            var (lineStart, lineEnd, file) = (start, end, path);
            image.Tapped += (_, e) =>
            {
                // Like an inline picture: the left half puts the caret before it, the right half after it.
                e.Handled = true;
                var after = e.GetPosition(image).X > image.ActualWidth / 2;
                _box.Focus(FocusState.Pointer);
                _box.TextDocument.Selection.SetRange(after ? lineEnd : lineStart, after ? lineEnd : lineStart);
            };
            image.DoubleTapped += (_, e) =>
            {
                e.Handled = true;
                _ = OpenFileAsync(file);
            };
            image.PointerWheelChanged += (_, e) =>
            {
                // The preview sits above the editor – scrolling over it must still scroll the note.
                var delta = e.GetCurrentPoint(image).Properties.MouseWheelDelta;
                sv.ChangeView(null, sv.VerticalOffset - delta, null);
                e.Handled = true;
            };
            ToolTipService.SetToolTip(image, OpenImageTip);
            Canvas.SetLeft(image, box.X);
            Canvas.SetTop(image, box.Y);
            _images.Children.Add(image);
            if (block)
            {
                _imageBoxes.Add((start, end, box));
            }
        }

        UpdateImageCaret();
    }

    /// <summary>Shows the caret before or after the picture when it is on an image line.</summary>
    private void UpdateImageCaret()
    {
        _images.Children.Remove(_imageCaret);
        var (caret, length) = GetSelection();
        if (length > 0 || _box.FocusState == FocusState.Unfocused)
        {
            return;
        }

        foreach (var (start, end, box) in _imageBoxes)
        {
            if (caret < start || caret > end)
            {
                continue;
            }

            var before = caret - start < end - caret;
            _imageCaret.Height = box.Height;
            _imageCaret.Fill = new SolidColorBrush(_theme.Text);
            Canvas.SetLeft(_imageCaret, before ? box.Left - 4 : box.Right + 2);
            Canvas.SetTop(_imageCaret, box.Top);
            _images.Children.Add(_imageCaret);
            return;
        }
    }

    private BitmapImage Bitmap(string path, double height)
    {
        var decodeHeight = (int)Math.Ceiling(height * (XamlRoot?.RasterizationScale ?? 1));
        if (_bitmaps.TryGetValue(path, out var bitmap) && bitmap.DecodePixelHeight >= decodeHeight)
        {
            return bitmap;
        }

        // A file:// UriSource does not load in an unpackaged app – hand WinUI the file stream instead.
        // Decoded off the UI thread, at the displayed size only.
        bitmap = new BitmapImage { DecodePixelHeight = decodeHeight };
        _bitmaps[path] = bitmap;
        _ = SetBitmapSourceAsync(bitmap, path);
        return bitmap;
    }

    private static async Task SetBitmapSourceAsync(BitmapImage bitmap, string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            await bitmap.SetSourceAsync(stream);
        }
        catch (Exception)
        {
            // unreadable – the space stays empty, the link still opens
        }
    }

    // ---------------------------------------------------------------- opening

    /// <summary>Ctrl+click on the text of an image line opens the file (a plain click on the preview does too).</summary>
    private bool TryOpenImageAt(Point point)
    {
        var pos = _box.TextDocument.GetRangeFromPoint(point, PointOptions.ClientCoordinates).StartPosition;
        if (pos < 0 || pos > _text.Length)
        {
            return false;
        }

        var (start, end) = MarkdownEditing.LineAt(_text, pos);
        if (!_imageLines.TryGetValue(_text[start..end], out var path))
        {
            return false;
        }

        _ = OpenFileAsync(path);
        return true;
    }

    private static async Task OpenFileAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            await Windows.System.Launcher.LaunchFileAsync(file);
        }
        catch (Exception)
        {
            // file gone or no associated app – nothing to open
        }
    }

    // ---------------------------------------------------------------- drag & drop, file paste

    private void InitImageDrop()
    {
        _box.AllowDrop = true;
        _box.AddHandler(DragOverEvent, new DragEventHandler(OnImageDragOver), handledEventsToo: true);
        _box.AddHandler(DropEvent, new DragEventHandler(OnImageDrop), handledEventsToo: true);
    }

    private async void OnImageDragOver(object sender, DragEventArgs e)
    {
        if (_readOnly || ImagePasteHandler is null || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.OfType<StorageFile>().Any(IsImageFile))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = DropImageCaption;
                e.Handled = true;
            }
        }
        catch (Exception)
        {
            // the source went away mid-drag
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void OnImageDrop(object sender, DragEventArgs e)
    {
        if (_readOnly || ImagePasteHandler is null || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.Handled = true;
        var at = _box.TextDocument.GetRangeFromPoint(e.GetPosition(_box), PointOptions.ClientCoordinates).StartPosition;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            await InsertImageFilesAsync(items, at);
        }
        catch (Exception)
        {
            // the source went away mid-drop
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// Copies image files into the note's assets (via <see cref="ImagePasteHandler"/>) and inserts their links at
    /// <paramref name="at"/> (null = the selection). Returns false when none of the items is an image.
    /// </summary>
    private async Task<bool> InsertImageFilesAsync(IReadOnlyList<IStorageItem> items, int? at)
    {
        var files = items.OfType<StorageFile>().Where(IsImageFile).ToList();
        if (files.Count == 0 || ImagePasteHandler is not { } handler)
        {
            return false;
        }

        var links = new List<string>();
        foreach (var file in files)
        {
            var bytes = await Task.Run(() => File.ReadAllBytesAsync(file.Path));
            if (await handler(bytes, Path.GetExtension(file.Path).ToLowerInvariant()) is { } link)
            {
                links.Add($"![{EscapeAlt(Path.GetFileNameWithoutExtension(file.Path))}]({link})");
            }
        }

        if (links.Count == 0)
        {
            return true;
        }

        InsertImageLinks(string.Join("\n", links), at);
        return true;
    }

    /// <summary>Inserts image links on lines of their own (they are blocks in formatted mode).</summary>
    private void InsertImageLinks(string links, int? at)
    {
        var offset = at ?? GetSelection().Start;
        _undo.Seal();
        ApplyEdit(MarkdownImageBlock.InsertOnOwnLine(_text, offset, links), recordUndo: true);
        _undo.Seal();
    }

    private static bool IsImageFile(StorageFile file) =>
        !string.IsNullOrEmpty(file.Path) && MarkdownImageLink.Extensions.Contains(Path.GetExtension(file.Path));

    private static string EscapeAlt(string name) => name.Replace("[", "(").Replace("]", ")");
}
