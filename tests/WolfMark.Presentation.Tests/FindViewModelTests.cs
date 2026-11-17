using WolfMark.App.ViewModels;
using WolfMark.Core.Markdown;

namespace WolfMark.Presentation.Tests;

/// <summary>Find and replace in the open note, shared by both apps.</summary>
public sealed class FindViewModelTests
{
    private const string Text = "ab ab cd ab"; // "ab" at 0, 3 and 9

    private static FindViewModel Find(string query) => new() { Query = query };

    [Fact]
    public void Opening_takes_the_selected_words_as_the_query_but_not_whole_lines()
    {
        var find = new FindViewModel();

        find.Open(replace: false, "release");
        Assert.True(find.IsOpen);
        Assert.False(find.ShowReplace);
        Assert.Equal("release", find.Query);

        find.Open(replace: true, "two\nlines");
        Assert.True(find.ShowReplace);
        Assert.Equal("release", find.Query);
    }

    [Fact]
    public void Typing_keeps_the_current_match_while_it_still_fits()
    {
        var find = Find("ab");

        Assert.Equal(new TextMatch(3, 2), find.FindFrom(Text, selectionStart: 3));
        Assert.Contains("2", find.Status);
        Assert.Contains("3", find.Status);
    }

    [Fact]
    public void Next_and_previous_step_through_the_matches_and_wrap()
    {
        var find = Find("ab");

        var first = find.FindNext(Text, 0, 0, backwards: false);
        var second = find.FindNext(Text, first!.Value.Start, first.Value.Length, backwards: false);
        var third = find.FindNext(Text, second!.Value.Start, second.Value.Length, backwards: false);
        var wrapped = find.FindNext(Text, third!.Value.Start, third.Value.Length, backwards: false);
        var back = find.FindNext(Text, wrapped!.Value.Start, wrapped.Value.Length, backwards: true);

        Assert.Equal([0, 3, 9, 0, 9], new[] { first.Value.Start, second.Value.Start, third.Value.Start, wrapped.Value.Start, back!.Value.Start });
    }

    [Fact]
    public void Nothing_found_says_so_and_an_empty_query_says_nothing()
    {
        var find = Find("xyz");

        Assert.Null(find.FindFrom(Text, 0));
        Assert.False(string.IsNullOrEmpty(find.Status));

        find.Query = string.Empty;
        Assert.Null(find.FindFrom(Text, 0));
        Assert.Equal(string.Empty, find.Status);
    }

    [Fact]
    public void Replace_changes_the_selected_match_only()
    {
        var find = Find("AB");
        find.Replacement = "x";

        Assert.Null(find.ReplaceCurrent(Text, 1, 2)); // "b " is not a match: nothing to replace yet
        var edit = find.ReplaceCurrent(Text, 3, 2);

        Assert.Equal("ab x cd ab", edit!.Value.ApplyTo(Text));

        find.MatchCase = true;
        Assert.Null(find.ReplaceCurrent(Text, 3, 2)); // "ab" is not "AB" when the case matters
    }

    [Fact]
    public void Replace_all_reports_how_many()
    {
        var find = Find("ab");
        find.Replacement = "x";

        var edit = find.ReplaceAll(Text);

        Assert.Equal("x x cd x", edit!.Value.ApplyTo(Text));
        Assert.Contains("3", find.Status);
        Assert.Null(Find("zz").ReplaceAll(Text));
    }
}
