using WolfMark.Core.Markdown;

namespace WolfMark.Core.Tests;

public sealed class MarkdownImageLinkTests
{
    [Theory]
    [InlineData("![](assets/1403-01.png)", "", "assets/1403-01.png")]
    [InlineData("![screen shot](assets/a.png \"Title\")", "screen shot", "assets/a.png")]
    [InlineData("![x](<assets/with space.png>)", "x", "assets/with space.png")]
    [InlineData("![x]( assets/a.png )", "x", "assets/a.png")]
    public void Parses_alt_and_url(string token, string alt, string url)
    {
        Assert.True(MarkdownImageLink.TryParse(token, out var a, out var u));
        Assert.Equal(alt, a);
        Assert.Equal(url, u);
    }

    [Theory]
    [InlineData("[x](a.png)")]
    [InlineData("![x]")]
    [InlineData("![x](a.png")]
    public void Rejects_non_images(string token) => Assert.False(MarkdownImageLink.TryParse(token, out _, out _));

    [Theory]
    [InlineData("assets/1403-01.png", "2026/10/19/assets/1403-01.png")]
    [InlineData("assets/with%20space.png", "2026/10/19/assets/with space.png")]
    [InlineData("assets\\b.png", "2026/10/19/assets/b.png")]
    [InlineData("../18/assets/a.png", "2026/10/18/assets/a.png")]
    [InlineData("./assets/a.png", "2026/10/19/assets/a.png")]
    public void Resolves_relative_paths_inside_the_root(string url, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "wm-root");
        var noteDir = Path.Combine(root, "2026", "10", "19");

        Assert.True(MarkdownImageLink.TryResolveLocal(noteDir, root, url, out var full));
        Assert.Equal(Path.GetFullPath(Path.Combine(root, expected.Replace('/', Path.DirectorySeparatorChar))), full);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com/a.png")]
    [InlineData("file:///C:/a.png")]
    [InlineData("C:\\Users\\a.png")]
    [InlineData("\\\\host\\share\\a.png")]
    [InlineData("//host/share/a.png")]
    [InlineData("/etc/a.png")]
    [InlineData("%5C%5Chost%5Cshare%5Ca.png")]
    [InlineData("../../../../../outside.png")]
    public void Rejects_remote_absolute_and_escaping_paths(string url)
    {
        var root = Path.Combine(Path.GetTempPath(), "wm-root");
        var noteDir = Path.Combine(root, "2026", "10", "19");

        Assert.False(MarkdownImageLink.TryResolveLocal(noteDir, root, url, out _));
    }
}
