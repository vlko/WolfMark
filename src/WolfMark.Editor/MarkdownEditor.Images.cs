using Microsoft.Graphics.Canvas;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using WolfMark.Core.Markdown;

namespace WolfMark.Editor;

/// <summary>
/// Image previews for <c>![alt](url)</c> lines in formatted mode: a picture <see cref="PreviewRows"/> text rows high
/// below the line. A line holding only an image shows just the picture; with the caret on it the link appears above
/// the picture for editing (the line grows by one row). Pictures come from <see cref="ImagePreviews"/> (downscaled,
/// cached on disk, read off the UI thread) as Win2D bitmaps. Double-click opens the file; image files and bitmaps
/// are pasted and dropped into the note's assets. Only relative paths inside the notes root are shown.
/// </summary>
public sealed partial class MarkdownEditor
{
    private const int PreviewRows = 5;
    private const double PreviewGap = 4;
    private const int MaxPictures = 128;

    private readonly Dictionary<string, Picture?> _pictures = new(StringComparer.OrdinalIgnoreCase); // null = unreadable
    private readonly HashSet<string> _picturesLoading = new(StringComparer.OrdinalIgnoreCase);
    private int _picturePixels;

    private sealed record Picture(uint PixelWidth, uint PixelHeight, CanvasBitmap? Bitmap);

    private double DisplayScale => XamlRoot?.RasterizationScale ?? 1;

    private double RowHeight => _emptyLineHeight;

    /// <summary>The line's first local image and whether the line is nothing but that image (computed once per line view).</summary>
    private (string? Path, bool Only) ImageOf(int line)
    {
        var view = _lines[line];
        if (view.ImageChecked)
        {
            return (view.ImagePath, view.ImageOnly);
        }

        view.ImageChecked = true;
        view.ImagePath = null;
        if (NoteDirectory is { } dir && NotesRoot is { } root)
        {
            var text = _doc.GetLineText(line);
            foreach (var token in _doc.GetTokens(line))
            {
                if (token.Kind == MarkdownTokenKind.Image
                    && MarkdownImageLink.TryParse(text.AsSpan(token.Start, token.Length), out _, out var url)
                    && MarkdownImageLink.TryResolveLocal(dir, root, url, out var full))
                {
                    view.ImagePath = full;
                    view.ImageOnly = MarkdownImageBlock.IsImageLine(text);
                    break;
                }
            }
        }

        return (view.ImagePath, view.ImageOnly);
    }

    /// <summary>The loaded picture of a line in formatted mode.</summary>
    /// <param name="load">Start loading a missing picture – only for lines being drawn, never for all lines at once.</param>
    private Picture? PictureOf(int line, out bool only, bool load = false)
    {
        only = false;
        if (_mode != MarkdownEditorMode.Formatted || ImageOf(line) is not { Path: { } path } image)
        {
            return null;
        }

        only = image.Only;
        if (_pictures.TryGetValue(path, out var picture))
        {
            return picture;
        }

        if (load)
        {
            RequestPicture(path);
        }

        return null;
    }

    /// <summary>The picture's size in DIPs: <see cref="PreviewRows"/> rows high at most, never wider than the text column.</summary>
    private Size PictureSize(Picture picture)
    {
        var height = Math.Min(PreviewRows * RowHeight, picture.PixelHeight / DisplayScale);
        var width = height * picture.PixelWidth / Math.Max(1, picture.PixelHeight);
        var column = Math.Max(80, _layoutWidth);
        if (width > column)
        {
            width = column;
            height = width * picture.PixelHeight / Math.Max(1, picture.PixelWidth);
        }

        return new Size(Math.Round(width), Math.Round(height));
    }

    /// <summary>
    /// An image-only line with its picture shown is one block: it shows only the picture, the caret stands only
    /// before or after it (full picture height), arrows step over it like over one character, and text typed at
    /// either side goes to a new line before or after it – the image always keeps a line of its own.
    /// </summary>
    private bool IsBlock(int line) => line >= 0 && line < _lines.Count && PictureOf(line, out var only) is not null && only;

    /// <summary>The block line at an offset, with its start and end.</summary>
    private (int Line, int Start, int End)? BlockAt(int offset)
    {
        var line = _doc.GetLineAtOffset(offset);
        if (!IsBlock(line))
        {
            return null;
        }

        var start = _doc.GetLineStart(line);
        return (line, start, start + _doc.GetLineText(line).Length);
    }

    /// <summary>For the <see cref="MarkdownImageBlock"/> rules: is this line text a shown image block?</summary>
    private bool IsShownBlock(string lineText) =>
        _mode == MarkdownEditorMode.Formatted && MarkdownImageBlock.IsImageLine(lineText)
        && NoteDirectory is { } dir && NotesRoot is { } root
        && lineText.Trim() is var link && link.IndexOf("](", StringComparison.Ordinal) is var mid and > 0
        && MarkdownImageLink.TryResolveLocal(dir, root, link[(mid + 2)..^1], out var path)
        && _pictures.TryGetValue(path, out var picture) && picture is not null;

    /// <summary>A caret inside a block's (hidden) markup goes to the nearer side.</summary>
    private int SnapOutOfBlock(int offset) =>
        BlockAt(offset) is { } b && offset > b.Start && offset < b.End
            ? (offset - b.Start <= b.End - offset ? b.Start : b.End)
            : offset;

    /// <summary>The caret beside a block: the picture's left or right edge, its full height.</summary>
    private Rect BlockCaretRect(int line, bool after)
    {
        var picture = PictureOf(line, out _)!;
        var r = PictureRect(line, picture, LineTop(line));
        return new Rect(after ? r.Right + 1 : r.Left - 2.5, r.Top, 1.5, r.Height);
    }

    /// <summary>A line's height on screen: its text, plus the picture below it (or, for a block, only the picture).</summary>
    private double LineHeight(int line)
    {
        var view = _lines[line];
        var text = view.Height < 0 ? _emptyLineHeight : view.Height;
        if (PictureOf(line, out var only) is not { } picture)
        {
            return text;
        }

        var image = PictureSize(picture).Height + 2 * PreviewGap;
        return only ? image : text + image;
    }

    private void RequestPicture(string path)
    {
        var pixels = (int)Math.Ceiling(PreviewRows * RowHeight * DisplayScale);
        if (pixels != _picturePixels)
        {
            ClearPictures(); // another font size or display scale: pictures of another height
            _picturePixels = pixels;
        }

        if (_picturesLoading.Add(path))
        {
            _ = LoadPictureAsync(path, pixels);
        }
    }

    private async Task LoadPictureAsync(string path, int pixels)
    {
        Picture? picture = null;
        try
        {
            var cache = PreviewCacheDirectory;
            if (await Task.Run(() => ImagePreviews.LoadAsync(path, pixels, cache)) is { } preview)
            {
                using var stream = await ImagePreviews.OpenAsync(preview);
                var bitmap = await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), stream);
                picture = new Picture(preview.Width, preview.Height, bitmap);
            }
        }
        catch (Exception)
        {
            // missing, not an image or not readable – the link just stays a link
        }

        _picturesLoading.Remove(path);
        if (pixels != _picturePixels)
        {
            picture?.Bitmap?.Dispose();
            return; // made for another size; a newer request is on its way
        }

        if (_pictures.Count >= MaxPictures)
        {
            // The oldest quarter goes (insertion order); lines in view ask again when drawn.
            foreach (var old in _pictures.Keys.Take(MaxPictures / 4).ToList())
            {
                _pictures[old]?.Bitmap?.Dispose();
                _pictures.Remove(old);
            }

            _topsDirty = true;
        }

        _pictures[path] = picture;
        if (picture is not null)
        {
            var anchor = ViewAnchor();
            _topsDirty = true;
            UpdateCanvasSize();
            RestoreViewAnchor(anchor);
            _canvas.Invalidate();
        }
    }

    private void ClearPictures()
    {
        foreach (var picture in _pictures.Values)
        {
            picture?.Bitmap?.Dispose();
        }

        _pictures.Clear();
        _topsDirty = true;
    }

    /// <summary>The picture of a line below its text (or in its place).</summary>
    private void DrawPicture(CanvasDrawingSession ds, int line, float top)
    {
        if (PictureOf(line, out _, load: true) is not { Bitmap: { } bitmap } picture)
        {
            return;
        }

        ds.DrawImage(bitmap, PictureRect(line, picture, top));
    }

    private Rect PictureRect(int line, Picture picture, double top)
    {
        var size = PictureSize(picture);
        var y = top + PreviewGap + (IsBlock(line) ? 0 : (_lines[line].Height < 0 ? _emptyLineHeight : _lines[line].Height));
        return new Rect(PadLeft, y, size.Width, size.Height);
    }

    /// <summary>A double-click on a picture opens the file in its Windows app.</summary>
    private bool TryOpenPictureAt(Point point)
    {
        var line = LineAtY(point.Y);
        if (PictureOf(line, out _) is not { } picture || ImageOf(line).Path is not { } path
            || !PictureRect(line, picture, LineTop(line)).Contains(point))
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
            await Windows.System.Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(path));
        }
        catch (Exception)
        {
            // file gone or no associated app – nothing to open
        }
    }

    // ---------------------------------------------------------------- paste and drop

    private void InitImageDrop()
    {
        _canvas.AllowDrop = true;
        _canvas.DragOver += OnImageDragOver;
        _canvas.Drop += OnImageDrop;

        // The hint over a picture lives above the scrolled canvas, not in it.
        var host = new Grid();
        Content = null;
        host.Children.Add(_scroll);
        host.Children.Add(_hint);
        Content = host;
        _hint.Child = _hintText;
        _canvas.PointerExited += (_, _) => UpdatePictureHover(null);
    }

    // ---------------------------------------------------------------- hover hint

    private readonly Border _hint = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Padding = new Thickness(6, 2, 6, 3),
        CornerRadius = new CornerRadius(3),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private readonly TextBlock _hintText = new() { FontSize = 12 };
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _hintTimer;
    private Rect? _hoverPicture;
    private bool _hoverTask;

    /// <summary>Over a picture: a hand cursor and, after a moment, "double-click to open" at its lower edge.</summary>
    private void UpdatePictureHover(Point? point)
    {
        Rect? over = null;
        if (point is { } p && _lines.Count > 0)
        {
            var line = LineAtY(p.Y);
            if (PictureOf(line, out _) is { } picture && PictureRect(line, picture, LineTop(line)) is var r && r.Contains(p))
            {
                over = r;
            }
        }

        // Over a task box: the hand too (a click toggles it).
        var overTask = over is null && point is { } q && TaskBoxAt(q) is not null;
        if (overTask != _hoverTask)
        {
            _hoverTask = overTask;
            ProtectedCursor = InputSystemCursor.Create(overTask ? InputSystemCursorShape.Hand : InputSystemCursorShape.IBeam);
        }

        if (over == _hoverPicture)
        {
            return;
        }

        _hoverPicture = over;
        _hint.Visibility = Visibility.Collapsed;
        ProtectedCursor = InputSystemCursor.Create(over is null ? InputSystemCursorShape.IBeam : InputSystemCursorShape.Hand);
        if (over is null)
        {
            _hintTimer?.Stop();
            return;
        }

        if (_hintTimer is null)
        {
            _hintTimer = _dispatcher.CreateTimer();
            _hintTimer.Interval = TimeSpan.FromMilliseconds(500);
            _hintTimer.IsRepeating = false;
            _hintTimer.Tick += (_, _) => ShowPictureHint();
        }

        _hintTimer.Stop();
        _hintTimer.Start();
    }

    private void ShowPictureHint()
    {
        if (_hoverPicture is not { } r)
        {
            return;
        }

        _hintText.Text = OpenImageTip;
        _hintText.Foreground = new SolidColorBrush(_theme.Text);
        _hint.Background = new SolidColorBrush(_theme.CodeBackground);
        var y = r.Bottom - _scroll.VerticalOffset - 26;
        _hint.Margin = new Thickness(r.Left + 6, Math.Max(0, y), 0, 0);
        _hint.Visibility = Visibility.Visible;
    }

    /// <summary>Image files copied in Explorer and bitmaps go into the note's assets; true when the clipboard held one.</summary>
    private async Task<bool> PasteImageAsync(DataPackageView content)
    {
        if (content.Contains(StandardDataFormats.StorageItems)
            && await InsertImageFilesAsync(await content.GetStorageItemsAsync(), at: null))
        {
            return true;
        }

        if (!content.Contains(StandardDataFormats.Bitmap) || ImagePasteHandler is not { } handler)
        {
            return false;
        }

        var reference = await content.GetBitmapAsync();
        using var stream = await reference.OpenReadAsync();
        var png = await ImageEncoding.ToPngAsync(stream);
        if (await handler(png, ".png") is { } link)
        {
            InsertImageLinks($"![]({link})", at: null);
        }

        return true;
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
        var at = OffsetAt(e.GetPosition(_canvas));
        var deferral = e.GetDeferral();
        try
        {
            await InsertImageFilesAsync(await e.DataView.GetStorageItemsAsync(), at);
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

    /// <summary>Copies image files into the assets (<see cref="ImagePasteHandler"/>) and links them; false when none is an image.</summary>
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
                links.Add($"![{Path.GetFileName(file.Path).Replace('[', '(').Replace(']', ')')}]({link})");
            }
        }

        if (links.Count > 0)
        {
            InsertImageLinks(string.Join('\n', links), at);
        }

        return true;
    }

    /// <summary>Image links go on lines of their own (each shows as a picture).</summary>
    private void InsertImageLinks(string links, int? at)
    {
        _undo.Seal();
        Edit(MarkdownImageBlock.InsertOnOwnLine(_doc.GetText(), at ?? Selection.Start, links));
        _undo.Seal();
    }

    private static bool IsImageFile(StorageFile file) =>
        !string.IsNullOrEmpty(file.Path) && MarkdownImageLink.Extensions.Contains(Path.GetExtension(file.Path));
}
