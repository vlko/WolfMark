using WolfMark.Core.Markdown;

namespace WolfMark.Core.Tests;

public sealed class MarkdownImageBlockTests
{
    private const string Img = "![](assets/1403-01.png)";

    [Theory]
    [InlineData(Img, true)]
    [InlineData("  ![photo](assets/a.jpg)  ", true)]
    [InlineData("text " + Img, false)]
    [InlineData(Img + " text", false)]
    [InlineData("![a](x.png) ![b](y.png)", false)]
    [InlineData("[link](x)", false)]
    [InlineData("", false)]
    public void Recognises_image_lines(string line, bool expected) =>
        Assert.Equal(expected, MarkdownImageBlock.IsImageLine(line));

    [Fact]
    public void Text_typed_before_an_image_goes_to_a_line_above()
    {
        const string old = "a\n" + Img + "\nb";
        var typed = "a\nX" + Img + "\nb";

        var edit = MarkdownImageBlock.KeepOnOwnLine(old, typed);

        Assert.NotNull(edit);
        Assert.Equal("a\nX\n" + Img + "\nb", edit.Value.ApplyTo(typed));
        Assert.Equal(3, edit.Value.SelectionStart); // after "X"
    }

    [Fact]
    public void Text_typed_after_an_image_goes_to_a_line_below()
    {
        const string old = Img;
        var typed = Img + "Hi";

        var edit = MarkdownImageBlock.KeepOnOwnLine(old, typed);

        Assert.NotNull(edit);
        Assert.Equal(Img + "\nHi", edit.Value.ApplyTo(typed));
        Assert.Equal(Img.Length + 3, edit.Value.SelectionStart);
    }

    [Fact]
    public void Text_typed_inside_the_hidden_markup_is_moved_out()
    {
        var typed = Img.Insert(Img.Length - 3, "Z");

        var edit = MarkdownImageBlock.KeepOnOwnLine(Img, typed);

        Assert.Equal(Img + "\nZ", edit!.Value.ApplyTo(typed));
    }

    [Fact]
    public void At_the_image_edges_only_a_line_break_is_inserted()
    {
        // The image line itself must not be rewritten (it would lose its formatting in the editor).
        var before = MarkdownImageBlock.KeepOnOwnLine(Img, "X" + Img)!.Value;
        var after = MarkdownImageBlock.KeepOnOwnLine(Img, Img + "X")!.Value;

        Assert.Equal((1, 0, "\n"), (before.Start, before.Length, before.Insert));
        Assert.Equal((Img.Length, 0, "\n"), (after.Start, after.Length, after.Insert));
    }

    [Fact]
    public void Only_lines_the_editor_shows_as_pictures_are_blocks()
    {
        Assert.Null(MarkdownImageBlock.KeepOnOwnLine(Img, Img + "X", isBlock: _ => false));
        Assert.Null(MarkdownImageBlock.Backspace(Img, Img.Length, isBlock: _ => false));
    }

    [Theory]
    [InlineData("plain\ntext", "plain\ntexts")]
    [InlineData(Img, Img + "\n")]
    [InlineData(Img + "\nb", Img + "\n")]
    public void Other_changes_are_left_alone(string old, string @new) =>
        Assert.Null(MarkdownImageBlock.KeepOnOwnLine(old, @new));

    [Fact]
    public void Backspace_after_an_image_removes_the_whole_line()
    {
        const string text = "a\n" + Img + "\nb";
        var edit = MarkdownImageBlock.Backspace(text, 2 + Img.Length);

        Assert.Equal("a\nb", edit!.Value.ApplyTo(text));
        Assert.Equal(2, edit.Value.SelectionStart);
    }

    [Fact]
    public void Backspace_before_an_image_steps_over_instead_of_joining()
    {
        const string text = "a\n" + Img;
        var edit = MarkdownImageBlock.Backspace(text, 2);

        Assert.Equal(text, edit!.Value.ApplyTo(text));
        Assert.Equal(1, edit.Value.SelectionStart);
        Assert.Null(MarkdownImageBlock.Backspace("\n" + Img, 1)); // an empty line above just goes away
    }

    [Fact]
    public void Backspace_at_the_start_of_text_below_an_image_goes_behind_the_image()
    {
        const string text = Img + "\nb";
        var edit = MarkdownImageBlock.Backspace(text, Img.Length + 1);

        Assert.Equal(Img.Length, edit!.Value.SelectionStart);
        Assert.Equal(text, edit.Value.ApplyTo(text));
    }

    [Fact]
    public void Delete_before_an_image_removes_it_and_after_it_steps_over()
    {
        const string text = "a\n" + Img + "\nb";

        Assert.Equal("a\nb", MarkdownImageBlock.Delete(text, 2)!.Value.ApplyTo(text));
        Assert.Equal(3 + Img.Length, MarkdownImageBlock.Delete(text, 2 + Img.Length)!.Value.SelectionStart);
        Assert.Equal(2, MarkdownImageBlock.Delete(text, 1)!.Value.SelectionStart); // end of "a" → before image
    }

    [Fact]
    public void Removing_the_last_image_takes_the_newline_before_it()
    {
        const string text = "a\n" + Img;
        Assert.Equal("a", MarkdownImageBlock.Delete(text, 2)!.Value.ApplyTo(text));
    }

    [Theory]
    [InlineData("", 0, Img)]
    [InlineData("ab", 1, "a\n" + Img + "\nb")]
    [InlineData("ab", 2, "ab\n" + Img)]
    [InlineData("ab\n", 3, "ab\n" + Img)]
    public void Inserted_images_get_their_own_line(string text, int at, string expected)
    {
        var edit = MarkdownImageBlock.InsertOnOwnLine(text, at, Img);

        Assert.Equal(expected, edit.ApplyTo(text));
        Assert.Equal(expected.IndexOf(Img, StringComparison.Ordinal) + Img.Length, edit.SelectionStart);
    }
}
