using WolfMark.Core.Markdown;

namespace WolfMark.Core.Tests;

/// <summary>Find and replace within one note (Ctrl+F / Ctrl+H).</summary>
public sealed class TextSearchTests
{
    [Fact]
    public void Finds_every_occurrence_ignoring_letter_case_by_default()
    {
        var matches = TextSearch.FindAll("Plán: plán na týždeň, PLÁN B", "plán", matchCase: false);

        Assert.Equal([new TextMatch(0, 4), new TextMatch(6, 4), new TextMatch(22, 4)], matches);
        Assert.Equal([new TextMatch(6, 4)], TextSearch.FindAll("Plán: plán na týždeň, PLÁN B", "plán", matchCase: true));
    }

    [Fact]
    public void Matches_do_not_overlap_and_an_empty_query_finds_nothing()
    {
        Assert.Equal([new TextMatch(0, 2), new TextMatch(2, 2)], TextSearch.FindAll("aaaaa", "aa", matchCase: true));
        Assert.Empty(TextSearch.FindAll("text", string.Empty, matchCase: false));
    }

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(1, false, 1)] // inside the first match: the next one
    [InlineData(11, false, 0)] // past the last: wraps to the first
    [InlineData(10, true, 2)] // the last one starting before
    [InlineData(9, true, 1)] // from the start of a match: the one before it
    [InlineData(5, true, 1)]
    [InlineData(0, true, 2)] // before the first: wraps to the last
    public void Going_on_wraps_around(int from, bool backwards, int expected)
    {
        var matches = TextSearch.FindAll("ab ab cd ab", "ab", matchCase: true); // at 0, 3, 9

        Assert.Equal(expected, TextSearch.IndexFrom(matches, from, backwards));
        Assert.Equal(-1, TextSearch.IndexFrom([], from, backwards));
    }

    [Fact]
    public void Replacing_one_puts_the_caret_after_the_new_text()
    {
        var edit = TextSearch.Replace(new TextMatch(3, 2), "xyz");

        Assert.Equal("ab xyz cd", edit.ApplyTo("ab ab cd"));
        Assert.Equal((6, 0), (edit.SelectionStart, edit.SelectionLength));
    }

    [Fact]
    public void Replacing_all_is_one_edit_from_the_first_match_to_the_last()
    {
        const string text = "- [ ] úloha\n- [ ] ďalšia úloha\nkoniec";
        var matches = TextSearch.FindAll(text, "úloha", matchCase: false);

        var edit = TextSearch.ReplaceAll(text, matches, "vec");

        Assert.NotNull(edit);
        Assert.Equal("- [ ] vec\n- [ ] ďalšia vec\nkoniec", edit.Value.ApplyTo(text));
        Assert.Equal(matches[0].Start, edit.Value.Start);
        Assert.Null(TextSearch.ReplaceAll(text, [], "vec"));
    }

    [Fact]
    public void Replacing_with_text_that_contains_the_query_does_not_loop()
    {
        const string text = "a a a";

        var edit = TextSearch.ReplaceAll(text, TextSearch.FindAll(text, "a", matchCase: true), "aa");

        Assert.Equal("aa aa aa", edit!.Value.ApplyTo(text));
    }
}
