using System.Text;

namespace WolfMark.Storage;

/// <summary>
/// Crash-safe writes (SPEC §4.1): content goes to <c>.&lt;name&gt;.tmp</c> next to the target, is flushed
/// to disk and then moved over the target. A crash leaves either the old or the new file, never a torn one.
/// </summary>
public static class AtomicFile
{
    public const string TempSuffix = ".tmp";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string GetTempPath(string fullPath) =>
        Path.Combine(Path.GetDirectoryName(fullPath)!, "." + Path.GetFileName(fullPath) + TempSuffix);

    public static bool IsTempFile(string fileName) =>
        fileName.StartsWith('.') && fileName.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase);

    public static Task WriteAllTextAsync(string fullPath, string content, CancellationToken cancellationToken) =>
        WriteAllBytesAsync(fullPath, Utf8NoBom.GetBytes(content), overwrite: true, cancellationToken);

    /// <summary>Writes atomically. With <paramref name="overwrite"/> = false, throws <see cref="IOException"/> if the target exists.</summary>
    public static Task WriteAllBytesAsync(string fullPath, ReadOnlyMemory<byte> content, bool overwrite, CancellationToken cancellationToken) =>
        WriteAllBytesAsync(fullPath, content, overwrite, lastWriteTimeUtc: null, cancellationToken);

    /// <param name="lastWriteTimeUtc">
    /// Modification time to give the file (set on the temp file before the replace, so the target never
    /// carries the write time); null = now.
    /// </param>
    public static async Task WriteAllBytesAsync(string fullPath, ReadOnlyMemory<byte> content, bool overwrite, DateTime? lastWriteTimeUtc, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temp = GetTempPath(fullPath);
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                PreallocationSize = content.Length,
            };

            await using (var stream = new FileStream(temp, options))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (lastWriteTimeUtc is { } mtime)
            {
                File.SetLastWriteTimeUtc(temp, DateTime.SpecifyKind(mtime, DateTimeKind.Utc));
            }

            File.Move(temp, fullPath, overwrite);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
