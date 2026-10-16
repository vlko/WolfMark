using WolfMark.Core.Markdown;

namespace WolfMark.Core.Tests;

public sealed class MarkdownLexerTests
{
    private static List<MarkdownToken> Lex(string line, LineState? state = null)
    {
        var tokens = new List<MarkdownToken>();
        MarkdownLineLexer.Tokenize(line, state ?? LineState.Normal, tokens);
        return tokens;
    }

    private static string Text(string line, MarkdownToken t) => line.Substring(t.Start, t.Length);

    [Fact]
    public void Heading_marks_line_and_marker()
    {
        var tokens = Lex("## Nadpis");
        Assert.Contains(tokens, t => t.Kind == MarkdownTokenKind.Heading2 && t.Length == 9);
        Assert.Contains(tokens, t => t is { Kind: MarkdownTokenKind.Syntax, Start: 0, Length: 3 });
    }

    [Fact]
    public void Hash_without_space_is_a_tag_not_a_heading()
    {
        var line = "#praca a #ďalší-tag";
        var tags = Lex(line).Where(t => t.Kind == MarkdownTokenKind.Hashtag).Select(t => Text(line, t)).ToList();
        Assert.Equal(["#praca", "#ďalší-tag"], tags);
    }

    [Fact]
    public void Url_fragment_is_not_a_tag()
    {
        Assert.DoesNotContain(Lex("see https://example.com/#anchor"), t => t.Kind == MarkdownTokenKind.Hashtag);
    }

    [Theory]
    [InlineData("**bold**", MarkdownTokenKind.Strong, "bold")]
    [InlineData("__bold__", MarkdownTokenKind.Strong, "bold")]
    [InlineData("*it*", MarkdownTokenKind.Emphasis, "it")]
    [InlineData("_it_", MarkdownTokenKind.Emphasis, "it")]
    [InlineData("~~gone~~", MarkdownTokenKind.Strikethrough, "gone")]
    [InlineData("a `code` b", MarkdownTokenKind.InlineCode, "code")]
    [InlineData("[text](http://x)", MarkdownTokenKind.LinkText, "text")]
    [InlineData("[text](http://x)", MarkdownTokenKind.LinkUrl, "http://x")]
    [InlineData("[[2026-10-15]]", MarkdownTokenKind.WikiLink, "2026-10-15")]
    [InlineData("go https://x.sk/a.", MarkdownTokenKind.AutoLink, "https://x.sk/a")]
    public void Inline_spans(string line, MarkdownTokenKind kind, string expected)
    {
        Assert.Contains(Lex(line), t => t.Kind == kind && Text(line, t) == expected);
    }

    [Fact]
    public void Snake_case_is_not_emphasis()
    {
        Assert.DoesNotContain(Lex("some_snake_case_name"), t => t.Kind == MarkdownTokenKind.Emphasis);
    }

    [Fact]
    public void Tasks_are_recognised()
    {
        Assert.Contains(Lex("- [ ] todo"), t => t.Kind == MarkdownTokenKind.TaskOpen);
        Assert.Contains(Lex("  * [x] done"), t => t.Kind == MarkdownTokenKind.TaskDone);
        Assert.Contains(Lex("1. [X] done"), t => t.Kind == MarkdownTokenKind.TaskDoneText);
    }

    [Fact]
    public void Fenced_code_carries_state()
    {
        var tokens = new List<MarkdownToken>();
        var s1 = MarkdownLineLexer.Tokenize("```csharp", LineState.Normal, tokens);
        Assert.Equal(LineStateKind.FencedCode, s1.Kind);
        tokens.Clear();
        var s2 = MarkdownLineLexer.Tokenize("# not a heading", s1, tokens);
        Assert.Equal(s1, s2);
        Assert.Equal(MarkdownTokenKind.CodeBlock, Assert.Single(tokens).Kind);
        tokens.Clear();
        var s3 = MarkdownLineLexer.Tokenize("````", s2, tokens);
        Assert.Equal(LineStateKind.Normal, s3.Kind);
    }

    [Fact]
    public void Front_matter_only_at_document_start()
    {
        var tokens = new List<MarkdownToken>();
        Assert.Equal(LineStateKind.FrontMatter, MarkdownLineLexer.Tokenize("---", LineState.DocumentStart, tokens).Kind);
        Assert.Equal(LineStateKind.Normal, MarkdownLineLexer.Tokenize("---", LineState.Normal, tokens).Kind);
    }
}
