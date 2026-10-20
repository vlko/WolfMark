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
    private const int MaxCachedPreviews = 32;

    // Hit-testable only where a preview is (no background): clicks place the caret, wheel is passed on.
    // _images clips to the text viewport; _imageLayer holds the pictures in document coordinates and only
    // moves when the note scrolls – no RichEdit measuring per scroll frame.
    private readonly Canvas _images = new();
    private readonly Canvas _imageLayer = new() { RenderTransform = new TranslateTransform() };

    // Previews placed by the last LayoutImages (line range, box in layer coordinates) and the caret drawn
    // beside one: on an image line RichEdit's own caret is hidden behind the picture.
    private readonly List<(int Start, int End, Rect Box)> _imageBoxes = [];
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _imageCaret = new() { Width = 2, IsHitTestVisible = false };

    // Image elements reused between layouts (pictures, handlers and tooltips are not rebuilt).
    private readonly List<Image> _imagePool = [];

    // Downscaled picture per file; null = could not be read (missing, not an image).
    private readonly Dictionary<string, ImagePreview?> _previews = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _previewLoading = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BitmapImage> _bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private int _previewPixels;

    // Line text → image file, filled when lines are styled; keyed by text so edits that shift lines don't matter.
    private readonly Dictionary<string, string> _imageLines = new(StringComparer.Ordinal);
    private double _imageColumnWidth;
    private bool _imageLayoutPending;
    private Point _imageOrigin;

    /// <summary>Folder of the open note – relative image links resolve against it. Null disables previews.</summary>
    public string? NoteDirectory { get; set; }

    /// <summary>Notes root; images outside it are never shown.</summary>
    public string? NotesRoot { get; set; }

    /// <summary>Machine-local folder for the downscaled previews; null = kept in memory only.</summary>
    public string? PreviewCacheDirectory { get; set; }

    /// <summary>Caption of the drag &amp; drop cursor (the app provides a localised text).</summary>
    public string DropImageCaption { get; set; } = "Insert image";

    /// <summary>Tooltip of a preview (localised by the app).</summary>
    public string OpenImageTip { get; set; } = "Double-click to open";

    private void InitImages()
    {
        _images.Children.Add(_imageLayer);
        InitImageDrop();
    }

    private void ResetImages()
    {
        _imageLines.Clear();
        _imageBoxes.Clear();
        _imageLayer.Children.Clear();
        _imagePool.Clear();
        if (_previews.Count > MaxCachedPreviews)
        {
            // Pictures of earlier notes; the disk cache brings them back quickly.
            _previews.Clear();
            _bitmaps.Clear();
        }

        foreach (var failed in _previews.Where(p => p.Value is null).Select(p => p.Key).ToList())
        {
            _previews.Remove(failed); // the file may have arrived since (OneDrive)
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
            CheckPreviewHeight();
            _imageLines[style.Text] = path;
            if (!_previews.TryGetValue(path, out var preview))
            {
                EnsurePreview(path);
            }
            else if (preview is not null)
            {
                collapse = MarkdownImageBlock.IsImageLine(style.Text);
                // A collapsed line's picture starts on its own (tiny) row, so only the gap below is added.
                height = PreviewSize(preview).Height + (collapse ? PreviewGap : 2 * PreviewGap);
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
        && _previews.TryGetValue(path, out var preview)
        && preview is not null;

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

    private void EnsurePreview(string path)
    {
        if (_previewLoading.Add(path))
        {
            _ = LoadPreviewAsync(path, _previewPixels);
        }
    }

    /// <summary>Another font size or display scale needs pictures of another height: they are made again.</summary>
    private void CheckPreviewHeight()
    {
        var pixels = PreviewPixelHeight;
        if (pixels != _previewPixels)
        {
            _previewPixels = pixels;
            _previews.Clear();
            _bitmaps.Clear();
        }
    }

    private async Task LoadPreviewAsync(string path, int pixels)
    {
        ImagePreview? preview = null;
        var cache = PreviewCacheDirectory;
        try
        {
            // Reading, decoding and downscaling all off the UI thread.
            preview = await Task.Run(() => ImagePreviews.LoadAsync(path, pixels, cache));
        }
        catch (Exception)
        {
            // missing, not an image or not readable – the link just stays a link
        }

        _previewLoading.Remove(path);
        if (pixels != _previewPixels)
        {
            return; // made for another size; a newer request is on its way
        }

        _previews[path] = preview;
        RestyleImageLines();
    }

    /// <summary>
    /// Preview size in DIPs: <see cref="PreviewRows"/> text rows high (smaller images keep their size at the
    /// screen scale), narrower when the text column is too narrow for that.
    /// </summary>
    private Size PreviewSize(ImagePreview pixels)
    {
        var scale = DisplayScale;
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

    private double DisplayScale => XamlRoot?.RasterizationScale ?? 1;

    private int PreviewPixelHeight => (int)Math.Ceiling(PreviewRows * TextRowHeight * DisplayScale);

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

    /// <summary>
    /// Lays the previews out again once the current dispatcher work is done – styling batches, edits and
    /// resizes come in bursts, and measuring the lines in RichEdit is the expensive part.
    /// </summary>
    private void ScheduleImageLayout()
    {
        if (_imageLayoutPending)
        {
            return;
        }

        if (_imageLines.Count == 0 && _imageLayer.Children.Count == 0)
        {
            return; // no pictures – nothing to place or clear
        }

        _imageLayoutPending = _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _imageLayoutPending = false;
            LayoutImages();
        });
    }

    /// <summary>Measures the image lines and places their previews in document coordinates.</summary>
    private void LayoutImages()
    {
        var traceStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var rects = _rectQueries;
        try
        {
            LayoutImagesCore();
        }
        finally
        {
            TraceLayout(WolfMark.Core.Diagnostics.EditorPhase.ImageLayout, traceStart, rects);
        }
    }

    private void LayoutImagesCore()
    {
        _imageBoxes.Clear();
        if (_imageLines.Count == 0 || _mode != MarkdownEditorMode.Formatted || _scrollViewer is not { } sv)
        {
            _imageLayer.Children.Clear();
            _imagePool.Clear();
            return;
        }

        _imageOrigin = ViewportOrigin();
        // GetRect is viewport-relative, or document-relative while scrolled (ScrollShift); either way adding
        // the offset back gives the position in the document.
        var toDocument = ScrollShift() + sv.VerticalOffset;
        var doc = _box.TextDocument;
        var used = 0;
        foreach (var (_, start, end) in ImageCandidateLines())
        {
            var text = _text[start..end];
            if (!_imageLines.TryGetValue(text, out var path) || !_previews.TryGetValue(path, out var preview) || preview is null)
            {
                continue;
            }

            var size = PreviewSize(preview);
            double top, left;
            var block = MarkdownImageBlock.IsImageLine(text);
            if (block)
            {
                // Collapsed line: the picture starts on its tiny row and covers the 1pt markup – RichEdit ignores
                // the transparent colour, so the markup would show as a thin line of glyphs above the picture.
                _rectQueries++;
                doc.GetRange(start, start + 1).GetRect(PointOptions.ClientCoordinates, out var firstRect, out _);
                top = firstRect.Top;
                left = firstRect.Left;
            }
            else
            {
                // After other text the picture goes below the line, measured on the paragraph mark (the line's
                // markup is hidden and hidden text has no usable position). The mark's rectangle may include the
                // reserved SpaceAfter, so the line bottom is capped at one text row.
                _rectQueries++;
                doc.GetRange(end, end + 1).GetRect(PointOptions.ClientCoordinates, out var markRect, out _);
                top = markRect.Top + Math.Min(markRect.Height, TextRowHeight) + PreviewGap;
                left = 0;
            }

            var box = new Rect(left, top + toDocument, size.Width, size.Height);
            var image = PooledImage(used++);
            image.Tag = new ImageTarget(start, end, path);
            image.Source = Bitmap(path, preview);
            image.Width = size.Width;
            image.Height = size.Height;
            Canvas.SetLeft(image, box.X);
            Canvas.SetTop(image, box.Y);
            if (block)
            {
                _imageBoxes.Add((start, end, box));
            }
        }

        while (_imagePool.Count > used)
        {
            _imageLayer.Children.Remove(_imagePool[^1]);
            _imagePool.RemoveAt(_imagePool.Count - 1);
        }

        ScrollImages();
        UpdateImageCaret();
    }

    /// <summary>Scrolling only moves the layer – the previews keep their place in the document.</summary>
    private void ScrollImages()
    {
        if (_scrollViewer is not { } sv || _imageLayer.Children.Count == 0)
        {
            return;
        }

        var transform = (TranslateTransform)_imageLayer.RenderTransform;
        transform.X = _imageOrigin.X;
        transform.Y = _imageOrigin.Y - sv.VerticalOffset;
        _images.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, _imageOrigin.Y, Math.Max(0, ActualWidth), Math.Max(0, sv.ViewportHeight)),
        };
    }

    private sealed record ImageTarget(int Start, int End, string Path);

    private Image PooledImage(int index)
    {
        if (index < _imagePool.Count)
        {
            return _imagePool[index];
        }

        var image = new Image { Stretch = Stretch.Uniform };
        image.Tapped += (_, e) =>
        {
            // Like an inline picture: the left half puts the caret before it, the right half after it.
            if (image.Tag is not ImageTarget target)
            {
                return;
            }

            e.Handled = true;
            var at = e.GetPosition(image).X > image.ActualWidth / 2 ? target.End : target.Start;
            _box.Focus(FocusState.Pointer);
            _box.TextDocument.Selection.SetRange(at, at);
        };
        image.DoubleTapped += (_, e) =>
        {
            if (image.Tag is ImageTarget target)
            {
                e.Handled = true;
                _ = OpenFileAsync(target.Path);
            }
        };
        image.PointerWheelChanged += (_, e) =>
        {
            // The preview sits above the editor – scrolling over it must still scroll the note.
            if (_scrollViewer is { } sv)
            {
                var delta = e.GetCurrentPoint(image).Properties.MouseWheelDelta;
                sv.ChangeView(null, sv.VerticalOffset - delta, null);
                e.Handled = true;
            }
        };
        ToolTipService.SetToolTip(image, OpenImageTip);
        _imagePool.Add(image);
        _imageLayer.Children.Add(image);
        return image;
    }

    private SolidColorBrush? _imageCaretBrush;

    /// <summary>Shows the caret before or after the picture when it is on an image line.</summary>
    private void UpdateImageCaret()
    {
        var (caret, length) = GetSelection();
        if (length == 0 && _box.FocusState != FocusState.Unfocused)
        {
            foreach (var (start, end, box) in _imageBoxes)
            {
                if (caret < start || caret > end)
                {
                    continue;
                }

                var before = caret - start < end - caret;
                if (_imageCaretBrush?.Color != _theme.Text)
                {
                    _imageCaretBrush = new SolidColorBrush(_theme.Text);
                }

                _imageCaret.Fill = _imageCaretBrush;
                _imageCaret.Height = box.Height;
                Canvas.SetLeft(_imageCaret, before ? box.Left - 4 : box.Right + 2);
                Canvas.SetTop(_imageCaret, box.Top);
                if (_imageCaret.Parent is null)
                {
                    _imageLayer.Children.Add(_imageCaret);
                }

                return;
            }
        }

        _imageLayer.Children.Remove(_imageCaret);
    }

    private BitmapImage Bitmap(string path, ImagePreview preview)
    {
        if (_bitmaps.TryGetValue(path, out var bitmap))
        {
            return bitmap;
        }

        // From the bytes already read off the UI thread (a file:// UriSource does not load in an unpackaged
        // app anyway); large pictures arrive downscaled, so WinUI decodes only a small image.
        bitmap = new BitmapImage();
        _bitmaps[path] = bitmap;
        _ = SetBitmapSourceAsync(bitmap, preview);
        return bitmap;
    }

    private static async Task SetBitmapSourceAsync(BitmapImage bitmap, ImagePreview preview)
    {
        try
        {
            using var stream = await ImagePreviews.OpenAsync(preview);
            await bitmap.SetSourceAsync(stream);
        }
        catch (Exception)
        {
            // undecodable – the space stays empty, the link still opens
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
                links.Add($"![{EscapeAlt(Path.GetFileName(file.Path))}]({link})");
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
