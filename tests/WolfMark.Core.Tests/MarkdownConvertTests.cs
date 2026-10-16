using WolfMark.Core.Markdown;

namespace WolfMark.Core.Tests;

public sealed class MarkdownConvertTests
{
    [Fact]
    public void Converts_blocks_and_inlines_to_html()
    {
        const string md = """
            ---
            tags: [a]
            ---
            # Title with **bold**

            Some *text* and `code` and [link](https://x.sk?a=1&b=2).
            second line

            - one
            - [x] done ~~old~~
            1. first

            > quoted <tag>

            ```
            var a = 1 < 2;
            ```
            ---
            ![pic](assets/1403-01.png)
            """;

        var html = MarkdownConvert.ToHtml(md);
        Assert.DoesNotContain("tags:", html);
        Assert.Contains("<h1>Title with <strong>bold</strong></h1>", html);
        Assert.Contains("<p>Some <em>text</em> and <code>code</code> and <a href=\"https://x.sk?a=1&amp;b=2\">link</a>.<br>\nsecond line</p>", html);
        Assert.Contains("<ul>\n<li>one</li>\n<li><input type=\"checkbox\" checked disabled> done <del>old</del></li>\n</ul>", html);
        Assert.Contains("<ol>\n<li>first</li>\n</ol>", html);
        Assert.Contains("<blockquote><p>quoted &lt;tag&gt;</p></blockquote>", html);
        Assert.Contains("<pre><code>var a = 1 &lt; 2;\n</code></pre>", html);
        Assert.Contains("<hr>", html);
        Assert.Contains("<img src=\"assets/1403-01.png\" alt=\"pic\">", html);
    }

    [Fact]
    public void Converts_to_plain_text()
    {
        const string md = "## Heading **b**\n\n- item *i*\n- [ ] task\n  2. nested\n> q [l](u)\n```\ncode\n```";
        Assert.Equal("Heading b\n\n- item i\n☐ task\n  2. nested\nq l\ncode", MarkdownConvert.ToPlainText(md));
    }

    [Theory]
    [InlineData("a **bold** b", 4, 8, "**bold**")]
    [InlineData("## Head", 3, 7, "## Head")]
    [InlineData("see [docs](http://x) now", 5, 9, "[docs](http://x)")]
    [InlineData("plain text", 0, 5, "plain")]
    [InlineData("x *it* y", 0, 8, "x *it* y")]
    public void Selection_expands_to_hidden_markup(string text, int start, int end, string expected)
    {
        var (s, e) = MarkdownEditing.ExpandToMarkup(text, start, end);
        Assert.Equal(expected, text[s..e]);
    }
}
