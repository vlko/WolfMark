using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace WolfMark.Editor;

/// <summary>Re-encodes clipboard bitmaps (often DIB) as PNG for <c>assets/</c>.</summary>
internal static class ImageEncoding
{
    public static async Task<byte[]> ToPngAsync(IRandomAccessStream source)
    {
        var decoder = await BitmapDecoder.CreateAsync(source);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
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
}
