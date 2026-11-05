using WolfMark.Core.Markdown;

namespace WolfMark.Core.Tests;

public sealed class MarkdownLinksTests
{
    private static string? UrlAt(string line, int column)
    {
        var tokens = new List<MarkdownToken>();
        MarkdownLineLexer.Tokenize(line, LineState.Normal, tokens);
        return MarkdownLinks.WebUrlAt(line, tokens, column);
    }

    [Theory]
    [InlineData(5)] // on the text
    [InlineData(1)] // right after "["
    [InlineData(22)] // on the address
    public void A_markdown_link_gives_its_address(int column) =>
        Assert.Equal("https://wolfmark.dev/a", UrlAt("[WolfMark](https://wolfmark.dev/a) and more", column));

    [Fact]
    public void Title_and_angle_brackets_are_not_part_of_the_address()
    {
        Assert.Equal("https://a.sk", UrlAt("[a](https://a.sk \"titulok\")", 1));
        Assert.Equal("https://a.sk/x y", UrlAt("[a](<https://a.sk/x y>)", 1));
    }

    [Fact]
    public void A_bare_address_is_a_link() =>
        Assert.Equal("https://github.com/vlko/WolfMark", UrlAt("pozri https://github.com/vlko/WolfMark.", 12));

    [Fact]
    public void Mail_is_a_link() => Assert.Equal("mailto:a@b.sk", UrlAt("[napíš](mailto:a@b.sk)", 2));

    [Theory]
    [InlineData("[subor](C:/notes/a.md)")]
    [InlineData("[zdielane](file://server/share)")]
    [InlineData("[poznamka](2026/11/05/a.md)")]
    [InlineData("[js](javascript:alert(1))")]
    public void Other_targets_are_not_opened(string line) => Assert.Null(UrlAt(line, 2));

    [Fact]
    public void Outside_a_link_there_is_none() => Assert.Null(UrlAt("text [a](https://a.sk)", 2));
}
