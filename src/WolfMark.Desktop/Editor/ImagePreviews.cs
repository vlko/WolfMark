using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;

namespace WolfMark.Desktop.Editor;

/// <summary>A picture ready to show, already scaled to at most the preview height.</summary>
internal sealed record ImagePreview(Bitmap Bitmap)
{
    public PixelSize Pixels => Bitmap.PixelSize;
}

/// <summary>
/// Downscaled previews with a machine-local disk cache, as on Windows (src/WolfMark.Editor/ImagePreviews.cs):
/// large pictures are scaled once to the preview height; the key covers path, size, time and target height.
/// Thread-pool only.
/// </summary>
internal static class ImagePreviews
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(60);
    private static int s_pruned;

    public static ImagePreview? Load(string path, int maxHeight, string? cacheDirectory)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0)
        {
            return null;
        }

        var cached = cacheDirectory is null ? null : Path.Combine(cacheDirectory, CacheKey(info, maxHeight) + ".png");
        if (cached is not null && File.Exists(cached))
        {
            try
            {
                return new ImagePreview(new Bitmap(cached));
            }
            catch (Exception)
            {
                // damaged copy – made again from the original
            }
        }

        var original = new Bitmap(path);
        var (width, height) = (original.PixelSize.Width, original.PixelSize.Height);
        var scale = Math.Min(1.0, Math.Min((double)maxHeight / height, maxHeight * 8.0 / width));
        if (scale >= 1)
        {
            return new ImagePreview(original);
        }

        var small = original.CreateScaledBitmap(
            new PixelSize(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale))),
            BitmapInterpolationMode.HighQuality);
        original.Dispose();
        if (cached is not null)
        {
            Store(cacheDirectory!, cached, small);
        }

        return new ImagePreview(small);
    }

    private static string CacheKey(FileInfo info, int maxHeight)
    {
        var key = $"{info.FullName.ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{maxHeight}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 16);
    }

    private static void Store(string directory, string file, Bitmap bitmap)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            bitmap.Save(temp, PngBitmapEncoderOptions.Default);
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
}
