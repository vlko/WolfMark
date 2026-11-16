using WolfMark.Core.Links;
using WolfMark.Core.Notes;

namespace WolfMark.Core.Tests;

public sealed class AppLinkTests
{
    [Theory]
    [InlineData("wolfmark://today", AppLinkKind.Today)]
    [InlineData("WolfMark://TODAY/", AppLinkKind.Today)]
    [InlineData("wolfmark://new", AppLinkKind.New)]
    [InlineData("wolfmark://search", AppLinkKind.Search)]
    [InlineData("wolfmark://day/2026-11-16", AppLinkKind.Day)]
    [InlineData("wolfmark://note/2026/11/16/praca_1403", AppLinkKind.Note)]
    public void Parses_each_kind(string text, AppLinkKind kind)
    {
        Assert.True(AppLink.TryParse(text, out var link));
        Assert.Equal(kind, link.Kind);
    }

    [Fact]
    public void Day_and_note_carry_their_target()
    {
        Assert.True(AppLink.TryParse("wolfmark://day/2026-02-28", out var day));
        Assert.Equal(new DateOnly(2026, 2, 28), day.Day);

        Assert.True(AppLink.TryParse("wolfmark://note/2026/11/16/praca_1403", out var note));
        Assert.Equal("2026/11/16/praca_1403.md", note.Note!.RelativePath);
        Assert.True(AppLink.TryParse("wolfmark://note/2026/11/16/praca_1403.md", out var withExtension));
        Assert.Equal(note.Note, withExtension.Note);
    }

    [Theory]
    [InlineData("wolfmark://search?q=release%20checklist", "release checklist")]
    [InlineData("wolfmark://search?q=a+b&x=1", "a b")]
    [InlineData("wolfmark://search?x=1&Q=%23work", "#work")]
    [InlineData("wolfmark://search?q=", null)]
    [InlineData("wolfmark://search?q=%E2%9C%93%20hotovo", "✓ hotovo")]
    public void Search_query_is_unescaped(string text, string? query)
    {
        Assert.True(AppLink.TryParse(text, out var link));
        Assert.Equal(query, link.Query);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("today")]
    [InlineData("https://today")]
    [InlineData("wolfmark:today")]
    [InlineData("wolfmark://")]
    [InlineData("wolfmark://delete/2026/11/16/praca_1403")]
    [InlineData("wolfmark://today/extra")]
    [InlineData("wolfmark://day/2026-13-01")]
    [InlineData("wolfmark://day/16.11.2026")]
    [InlineData("wolfmark://note/../secret")]
    [InlineData("wolfmark://note/2026/11/../../../etc/passwd")]
    [InlineData("wolfmark://note/2026%2F..%2F..%2Fx")]
    [InlineData("wolfmark://note/2026/11/16/..%5C..%5Cx")]
    [InlineData("wolfmark://note/C:%5CWindows%5Cwin")]
    [InlineData("wolfmark://note/.wolfmark/trash/x")]
    [InlineData("wolfmark://note/2026/11/16/assets/x")]
    public void Rejects_what_is_not_a_link_into_the_notes(string? text) => Assert.False(AppLink.TryParse(text, out _));

    [Fact]
    public void Formats_and_parses_back()
    {
        Assert.True(NotePath.TryParse("2026/11/16/plán týždňa_0930.md", out var path));
        var links = new[]
        {
            AppLink.Today,
            AppLink.New,
            AppLink.ForDay(new DateOnly(2026, 11, 16)),
            AppLink.ForSearch("#work release"),
            AppLink.ForSearch(null),
            AppLink.ForNote(path),
        };
        foreach (var link in links)
        {
            Assert.True(AppLink.TryParse(link.ToString(), out var back), link.ToString());
            Assert.Equal(link, back);
        }

        Assert.Equal("wolfmark://note/2026/11/16/pl%C3%A1n%20t%C3%BD%C5%BEd%C5%88a_0930", AppLink.ForNote(path).ToString());
        Assert.Equal("wolfmark://day/2026-11-16", AppLink.ForDay(new DateOnly(2026, 11, 16)).ToString());
    }
}
