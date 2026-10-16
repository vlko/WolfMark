using WolfMark.Core.Text;

namespace WolfMark.Core.Tests;

public sealed class TextTests
{
    [Fact]
    public void Hash_is_stable()
    {
        Assert.Equal(ContentHash.Compute("ahoj"), ContentHash.Compute("ahoj"));
        Assert.NotEqual(ContentHash.Compute("ahoj"), ContentHash.Compute("Ahoj"));
        Assert.Equal(16, ContentHash.Compute(string.Empty).Length);
    }

    [Fact]
    public void Chunker_groups_paragraphs_and_splits_long_ones()
    {
        var text = "a\n\nb\n\n" + new string('x', 120) + " " + new string('y', 120);
        var chunks = TextChunker.Chunk(text, 100);
        Assert.Equal("a\n\nb", chunks[0]);
        Assert.True(chunks.Skip(1).All(c => c.Length <= 100));
        Assert.Equal(240, string.Concat(chunks.Skip(1)).Replace(" ", string.Empty).Length);
    }

    [Fact]
    public void Front_matter_block_list()
    {
        var fm = FrontMatter.Parse(["---", "tags:", "  - a", "  - \"b\"", "title: x", "---", "body"]);
        Assert.Equal(6, fm.LineCount);
        Assert.Equal(["a", "b"], fm.GetList("tags"));
        Assert.Equal("x", fm.Get("title"));
    }

    [Fact]
    public void Unterminated_front_matter_is_ignored()
    {
        Assert.False(FrontMatter.Parse(["---", "tags: [a]"]).IsPresent);
    }
}
