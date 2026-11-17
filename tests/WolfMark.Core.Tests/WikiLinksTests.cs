using WolfMark.Core.Markdown;
using WolfMark.Core.Notes;

namespace WolfMark.Core.Tests;

/// <summary>M9-04: wiki links between notes.</summary>
public sealed class WikiLinksTests
{
    [Theory]
    [InlineData("2026/11/16/praca_1403", WikiTargetKind.Note, "2026/11/16/praca_1403.md")]
    [InlineData(" 2026/11/16/praca_1403.md ", WikiTargetKind.Note, "2026/11/16/praca_1403.md")]
    [InlineData(@"2026\11\16\praca_1403", WikiTargetKind.Note, "2026/11/16/praca_1403.md")]
    [InlineData("2026/11/16/praca_1403|the plan", WikiTargetKind.Note, "2026/11/16/praca_1403.md")] // a label of its own
    [InlineData("2026-11-16", WikiTargetKind.Day, "2026-11-16")]
    [InlineData("praca_1403", WikiTargetKind.Name, "praca_1403")]
    [InlineData("ideas/someday", WikiTargetKind.Note, "ideas/someday.md")] // a note not named by the convention
    public void Reads_what_a_link_points_to(string inner, WikiTargetKind kind, string value)
    {
        var target = WikiLinks.Parse(inner);

        Assert.NotNull(target);
        Assert.Equal((kind, value), (target.Value.Kind, target.Value.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("|label only")]
    [InlineData("../outside/secret")] // never out of the notes
    [InlineData(".wolfmark/trash/x")]
    [InlineData("2026/11/16/assets/picture")]
    public void A_link_that_names_no_note_is_nothing(string inner)
    {
        Assert.Null(WikiLinks.Parse(inner));
    }

    [Fact]
    public void A_day_link_carries_the_date()
    {
        Assert.Equal(new DateOnly(2026, 11, 16), WikiLinks.Parse("2026-11-16")!.Value.Day);
        Assert.Equal(WikiTargetKind.Name, WikiLinks.Parse("2026-13-45")!.Value.Kind); // not a date: a file name
    }

    [Theory]
    [InlineData(3, null)]
    [InlineData(4, "2026/11/16/praca_1403")] // on the opening brackets
    [InlineData(12, "2026/11/16/praca_1403")]
    [InlineData(29, "2026/11/16/praca_1403")] // right after the closing brackets
    [InlineData(30, null)]
    public void Finds_the_link_under_the_pointer(int column, string? expected)
    {
        const string line = "see [[2026/11/16/praca_1403]] and more";
        var tokens = new List<MarkdownToken>();
        MarkdownLineLexer.Tokenize(line, LineState.Normal, tokens);

        Assert.Equal(expected, WikiLinks.TargetAt(line, tokens, column));
    }

    [Fact]
    public void A_note_is_linked_by_its_path_or_by_its_name_whatever_the_letter_case()
    {
        Assert.True(NotePath.TryParse("2026/11/16/Praca_1403.md", out var note));

        Assert.Equal(["2026/11/16/praca_1403", "praca_1403"], WikiLinks.KeysOf(note));
        Assert.Equal("2026/11/16/praca_1403", WikiLinks.Key(WikiLinks.Parse("2026/11/16/PRACA_1403")!.Value));
        Assert.Equal("praca_1403", WikiLinks.Key(WikiLinks.Parse("Praca_1403")!.Value));
        Assert.Equal("day:2026-11-16", WikiLinks.Key(WikiLinks.Parse("2026-11-16")!.Value));
    }

    [Fact]
    public void The_links_of_a_note_are_collected_once_each_and_not_from_code()
    {
        var info = NoteContentAnalyzer.Analyze("# Plan\n\nSee [[2026/11/16/praca_1403]] and [[praca_1403]], again [[2026/11/16/praca_1403]].\n\n```\n[[not/a/link]]\n```\n\nOn [[2026-11-18]].");

        Assert.Equal(["2026/11/16/praca_1403", "praca_1403", "day:2026-11-18"], info.Links);
    }
}
