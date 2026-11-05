using System.Text;

namespace WolfMark.Storage;

/// <summary>
/// A Markdown file outside the notes tree, opened from the file manager or the command line. The text uses
/// <c>\n</c> line ends in the editor; saving puts back the file's own line ends and byte order mark, so an
/// unchanged file stays byte for byte the same and other tools see no difference.
/// </summary>
public sealed record ExternalDocument(string FullPath, string Text, bool HasBom, bool UsesCrLf, DateTime CreatedUtc, DateTime ModifiedUtc)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<ExternalDocument> LoadAsync(string fullPath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var text = DirectoryEngine.Decode(bytes);
        var info = new FileInfo(fullPath);
        return new ExternalDocument(
            fullPath,
            text.Replace("\r\n", "\n", StringComparison.Ordinal),
            HasBom: bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]),
            UsesCrLf: text.Contains("\r\n", StringComparison.Ordinal),
            info.CreationTimeUtc,
            info.LastWriteTimeUtc);
    }

    /// <summary>The bytes for <paramref name="text"/> (with <c>\n</c> line ends) in this file's encoding and line ends.</summary>
    public byte[] Encode(string text)
    {
        var content = UsesCrLf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;
        var body = Utf8NoBom.GetBytes(content);
        return HasBom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    /// <summary>Writes <paramref name="text"/> atomically; returns the document as it now is on disk.</summary>
    public async Task<ExternalDocument> SaveAsync(string text, CancellationToken cancellationToken)
    {
        await AtomicFile.WriteAllBytesAsync(FullPath, Encode(text), overwrite: true, cancellationToken).ConfigureAwait(false);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            // The replace leaves the temp file's creation time; the file was created when it was, not now.
            File.SetCreationTimeUtc(FullPath, CreatedUtc);
        }

        return this with { Text = text, ModifiedUtc = File.GetLastWriteTimeUtc(FullPath) };
    }
}
