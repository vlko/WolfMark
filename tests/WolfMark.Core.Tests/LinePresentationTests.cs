using WolfMark.Core.Markdown;

namespace WolfMark.Core.Tests;

public sealed class LinePresentationTests
{
    private static LinePresentation Present(string line, bool hide = true)
    {
        var model = new MarkdownDocumentModel();
        model.Update(line);
        return LinePresentation.Create(model.GetLineText(0), model.GetTokens(0), hide);
    }

    [Fact]
    public void Heading_marker_is_hidden_and_the_caret_starts_in_the_title()
    {
        var p = Present("# Title");

        Assert.Equal("Title", p.Text);
        Assert.Equal(2, p.ToSource(0));
        Assert.Equal(7, p.ToSource(5));
        Assert.Equal(0, p.ToShown(0)); // inside the hidden marker: the next shown position
        Assert.Contains(p.Tokens, t => t.Kind == MarkdownTokenKind.Heading1 && t.Start == 0 && t.Length == 5);
    }

    [Fact]
    public void Caret_is_inside_bold_at_its_start_and_after_the_markers_at_its_end()
    {
        var p = Present("a **b** c");

        Assert.Equal("a b c", p.Text);
        Assert.Equal(4, p.ToSource(2)); // before "b": after the opening "**"
        Assert.Equal(7, p.ToSource(3)); // after "b": after the closing "**"
        Assert.Equal((4, 5), p.SourceRangeOf(2, 3));
        Assert.Contains(p.Tokens, t => t.Kind == MarkdownTokenKind.Strong && t.Start == 2 && t.Length == 1);
        Assert.DoesNotContain(p.Tokens, t => t.Kind == MarkdownTokenKind.Syntax);
    }

    [Fact]
    public void Link_shows_only_its_text()
    {
        Assert.Equal("see link", Present("see [link](https://example.com)").Text);
    }

    [Fact]
    public void Raw_mode_shows_the_source()
    {
        var p = Present("a **b** c", hide: false);

        Assert.Equal("a **b** c", p.Text);
        Assert.Equal(4, p.ToSource(4));
        Assert.Equal(4, p.ToShown(4));
        Assert.Contains(p.Tokens, t => t.Kind == MarkdownTokenKind.Syntax);
    }

    [Fact]
    public void Columns_round_trip_for_every_shown_position()
    {
        var p = Present("- [ ] **task** with `code` and [l](u) #tag");
        for (var d = 0; d <= p.Text.Length; d++)
        {
            Assert.Equal(d, p.ToShown(p.ToSource(d)));
        }
    }
}
