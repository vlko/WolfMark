using System.Security.Cryptography;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WolfMark.Editor;

/// <summary>A picture ready to show: its pixel size and encoded bytes (a downscaled copy for large images).</summary>
internal sealed record ImagePreview(uint Width, uint Height, byte[] Data);

/// <summary>
/// Previews are only a few text rows high, so large pictures (screenshots, photos) are scaled down once and
/// the small copy is kept in a machine-local cache – reopening a note reads kilobytes instead of decoding the
/// original again (which may also be a OneDrive placeholder). The key covers path, size, time and target
/// height, so an edited file or another font size or display scale makes a new copy. Thread-pool only.
/// </summary>
internal static class ImagePreviews
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(60);
    private static int s_pruned;

    /// <summary>Null when the file is missing or not an image.</summary>
    public static async Task<ImagePreview?> LoadAsync(string path, int maxHeight, string? cacheDirectory)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0)
        {
            return null;
        }

        var cached = cacheDirectory is null ? null : Path.Combine(cacheDirectory, CacheKey(info, maxHeight) + ".png");
        if (cached is not null && File.Exists(cached) && await TryReadAsync(cached) is { } hit)
        {
            return hit;
        }

        var original = await File.ReadAllBytesAsync(path);
        using var stream = await ToStreamAsync(original);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var (width, height) = (decoder.PixelWidth, decoder.PixelHeight);
        if (width == 0 || height == 0)
        {
            return null;
        }

        // Very wide images are limited too; small ones are shown as they are.
        var scale = Math.Min(1.0, Math.Min((double)maxHeight / height, maxHeight * 8.0 / width));
        if (scale >= 1)
        {
            return new ImagePreview(width, height, original);
        }

        var transform = new BitmapTransform
        {
            ScaledWidth = Math.Max(1, (uint)Math.Round(width * scale)),
            ScaledHeight = Math.Max(1, (uint)Math.Round(height * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb);
        var small = await EncodePngAsync(bitmap);
        if (cached is not null)
        {
            Store(cacheDirectory!, cached, small);
        }

        return new ImagePreview(transform.ScaledWidth, transform.ScaledHeight, small);
    }

    private static async Task<ImagePreview?> TryReadAsync(string file)
    {
        try
        {
            var data = await File.ReadAllBytesAsync(file);
            using var stream = await ToStreamAsync(data);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return decoder.PixelWidth > 0 && decoder.PixelHeight > 0 ? new ImagePreview(decoder.PixelWidth, decoder.PixelHeight, data) : null;
        }
        catch (Exception)
        {
            return null; // damaged copy – made again from the original
        }
    }

    private static string CacheKey(FileInfo info, int maxHeight)
    {
        var key = $"{info.FullName.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{maxHeight}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 16);
    }

    /// <summary>Atomic write (temp + replace); a failed write only means no cache next time.</summary>
    private static void Store(string directory, string file, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, data);
            File.Move(temp, file, overwrite: true);
            if (Interlocked.Exchange(ref s_pruned, 1) == 0)
            {
                Prune(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Once per run: drops copies not made for a long time (old file versions, old font sizes).</summary>
    private static void Prune(string directory)
    {
        var limit = DateTime.UtcNow - MaxAge;
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles())
        {
            try
            {
                var stale = file.Name.EndsWith(".tmp", StringComparison.Ordinal) ? file.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1) : file.LastWriteTimeUtc < limit;
                if (stale)
                {
                    file.Delete();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<InMemoryRandomAccessStream> ToStreamAsync(byte[] data)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(data);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        return stream;
    }

    private static async Task<byte[]> EncodePngAsync(SoftwareBitmap bitmap)
    {
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        var bytes = new byte[output.Size];
        output.Seek(0);
        using var reader = new DataReader(output);
        await reader.LoadAsync((uint)output.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>A stream over the bytes for <c>BitmapImage.SetSourceAsync</c> (UI thread, no file access).</summary>
    public static Task<InMemoryRandomAccessStream> OpenAsync(ImagePreview preview) => ToStreamAsync(preview.Data);
}
