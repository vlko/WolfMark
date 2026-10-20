using WolfMark.Core.Markdown;

namespace WolfMark.Core.Tests;

public sealed class MarkdownSpellingTests
{
    private static string Prose(string text)
    {
        var model = new MarkdownDocumentModel();
        model.Update(text);
        var last = model.LineCount - 1;
        return MarkdownSpelling.ProseOnly(model.GetLineText(last), model.GetTokens(last));
    }

    [Fact]
    public void Code_links_tags_and_markers_are_blanked_with_columns_kept()
    {
        var line = "Text **tučný** `kód` [odkaz](https://x.sk) #tag";
        var prose = Prose(line);

        Assert.Equal(line.Length, prose.Length);
        Assert.Contains("Text", prose, StringComparison.Ordinal);
        Assert.Contains("tučný", prose, StringComparison.Ordinal);
        Assert.Contains("odkaz", prose, StringComparison.Ordinal);
        Assert.DoesNotContain("kód", prose, StringComparison.Ordinal);
        Assert.DoesNotContain("https", prose, StringComparison.Ordinal);
        Assert.DoesNotContain("#tag", prose, StringComparison.Ordinal);
        Assert.DoesNotContain("**", prose, StringComparison.Ordinal);
        Assert.Equal(line.IndexOf("tučný", StringComparison.Ordinal), prose.IndexOf("tučný", StringComparison.Ordinal));
    }

    [Fact]
    public void Code_block_lines_are_not_checked()
    {
        Assert.True(string.IsNullOrWhiteSpace(Prose("```\nvar x = Compute();")));
    }

    [Fact]
    public void Plain_prose_is_returned_as_is()
    {
        const string line = "obyčajná veta";
        Assert.Same(line, MarkdownSpelling.ProseOnly(line, []));
    }
}
