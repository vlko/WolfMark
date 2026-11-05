namespace WolfMark.Storage.Tests;

public sealed class ExternalDocumentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Saving_keeps_the_byte_order_mark_and_line_ends(bool bom, bool crlf)
    {
        using var dir = new TempDirectory();
        var file = Path.Combine(dir.Path, "readme.md");
        var eol = crlf ? "\r\n" : "\n";
        byte[] original = [.. (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : []), .. System.Text.Encoding.UTF8.GetBytes($"# Nadpis{eol}čo ďalej{eol}")];
        await File.WriteAllBytesAsync(file, original, Ct);

        var document = await ExternalDocument.LoadAsync(file, Ct);
        Assert.Equal("# Nadpis\nčo ďalej\n", document.Text);
        Assert.Equal(bom, document.HasBom);
        Assert.Equal(crlf, document.UsesCrLf);

        // Unchanged text: the same bytes.
        await document.SaveAsync(document.Text, Ct);
        Assert.Equal(original, await File.ReadAllBytesAsync(file, Ct));

        var saved = await document.SaveAsync("# Nadpis\nnový riadok\n", Ct);
        Assert.Equal("# Nadpis\nnový riadok\n", (await ExternalDocument.LoadAsync(file, Ct)).Text);
        Assert.Equal(File.GetLastWriteTimeUtc(file), saved.ModifiedUtc);
        Assert.Empty(Directory.GetFiles(dir.Path, "*" + AtomicFile.TempSuffix)); // the temp file is gone
    }
}
